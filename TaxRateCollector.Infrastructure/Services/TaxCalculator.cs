using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.Infrastructure.Services;

public class TaxCalculator(IDbContextFactory<AppDbContext> dbFactory) : ITaxCalculator
{
    /// <summary>Guards the parent walk against a corrupt (cyclic) hierarchy.</summary>
    private const int MaxChainDepth = 16;

    public async Task<TaxCalcResult?> CalculateAsync(
        int jurisdictionId, decimal price, int quantity, TaxCalcOptions? options = null)
    {
        options ??= new TaxCalcOptions();

        await using var db = await dbFactory.CreateDbContextAsync();

        var jur = await db.Jurisdictions.FindAsync(jurisdictionId);
        if (jur is null) return null;

        var subtotal = price * quantity;

        // ── Origin-based sourcing rule ──────────────────────────────────────
        // For intrastate sales in origin-based states (IL, TX, TN, PA, etc.) the
        // applicable rates are those at the SELLER's location, not the buyer's.
        // When SellerJurisdictionId is supplied and the state uses OriginBased
        // sourcing, swap the effective jurisdiction used for rate lookup.
        if (options.SellerJurisdictionId.HasValue)
        {
            var sellerJur = await db.Jurisdictions.FindAsync(options.SellerJurisdictionId.Value);
            if (sellerJur?.StateCode == jur.StateCode) // intrastate only
            {
                var profile = await db.StateTaxProfiles
                    .FirstOrDefaultAsync(p => p.StateCode == jur.StateCode);
                if (profile?.IntrastateSourcingRule == SourcingRule.OriginBased)
                    jur = sellerJur;
                // Modified (CA): state/county = origin, city = destination.
                // Full Mixed-tier support requires splitting the query by tier — deferred.
            }
        }

        // ── Jurisdiction chain (TRC-LAW-6) ──────────────────────────────────
        // A point-of-sale rate is cumulative up the hierarchy: the effective jurisdiction
        // plus every State / County / City / District ancestor. The Country node is not a
        // tier of the sales-tax chain (the law lists State + County + City + districts), so
        // it only contributes when it is itself the jurisdiction being calculated.
        var chain = await LoadChainAsync(db, jur);
        var tierById = chain.ToDictionary(j => j.Id, j => j.JurisdictionType);
        var chainIds = tierById.Keys.ToList();

        // ── Load rates ──────────────────────────────────────────────────────
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // Exclude unpopulated placeholder rows (Rate == 0 with no source document).
        // True 0% laws (e.g. grocery exemptions) live in StateCategoryRule, not TaxRates,
        // so any Rate==0 row in this table is a skeleton waiting for a real scrape.
        var candidates = await db.TaxRates
            .Where(t => chainIds.Contains(t.JurisdictionId)
                     && t.IsCurrent
                     && t.AutoApprove
                     && t.Rate > 0
                     && (t.EffectiveDate == null || t.EffectiveDate <= today)
                     && (t.ExpirationDate == null || t.ExpirationDate > today))
            .ToListAsync();

        var rates = SelectApplicableRates(candidates, options.TaxCategoryId)
            .OrderBy(r => (int)tierById[r.JurisdictionId])
            .ThenBy(r => r.Name)
            .ToList();
        if (rates.Count == 0) return null;

        // ── Combined rows (TRC-LAW-6) ───────────────────────────────────────
        // Some sources (CA/IL/TX statewide lists) publish one combined point-of-sale rate per
        // locality. Such a row already contains every ancestor tier's sales tax, so the
        // deepest combined row stands in for itself AND its ancestors' sales-tax percentages;
        // rows below it (districts) and non-sales-tax rows (excise etc.) still stack.
        var depthById = chain.Select((j, i) => (j.Id, i)).ToDictionary(p => p.Id, p => p.i);
        var combined = rates
            .Where(r => r.IsCombinedRate && IsChainSalesPercentage(r))
            .OrderBy(r => depthById[r.JurisdictionId])
            .FirstOrDefault();
        if (combined is not null)
        {
            var combinedDepth = depthById[combined.JurisdictionId];
            rates = rates
                .Where(r => ReferenceEquals(r, combined)
                            || !IsChainSalesPercentage(r)
                            || (!r.IsCombinedRate && depthById[r.JurisdictionId] < combinedDepth))
                .ToList();
        }

        // ── Filter by SaleContext and ABV ───────────────────────────────────
        if (options.SaleContext != SaleContext.Any)
            rates = rates
                .Where(r => r.SaleContext == SaleContext.Any || r.SaleContext == options.SaleContext)
                .ToList();

        if (options.Abv.HasValue)
        {
            var abv = options.Abv.Value;
            var all = rates;
            rates = rates.Where(r => AbvMatches(r, abv, all)).ToList();
        }

        if (rates.Count == 0) return null;

        // ── Two-pass: non-compound first, then compound ─────────────────────
        // Compound rates (e.g. Chicago Matching Tax) apply to price + all retail taxes.
        var lines = new List<TaxRateLine>();
        var localLines = new List<(TaxRateLine Line, decimal Rate)>();
        decimal retailSubtotal = 0m;   // running sum of non-included-in-price, non-compound taxes

        foreach (var r in rates.Where(r => !r.IsCompound))
        {
            var tax = ComputeTax(r, subtotal, quantity, options);
            var line = new TaxRateLine(r.Name, r.Rate, r.RateBasis, r.Unit, r.SaleContext,
                r.RemittancePoint, tax, r.EffectiveDate, r.IsIncludedInPrice, IsCompound: false);
            lines.Add(line);
            if (!r.IsIncludedInPrice)
                retailSubtotal += tax;

            // Local tier (County / City / District) percentage taxes count toward a state's
            // combined local-rate cap unless the row says it is statutorily excluded. A combined
            // row includes the state rate, so it is not a local portion and is never capped.
            if (!r.IsCombinedRate && IsLocalTier(tierById[r.JurisdictionId]) &&
                r.RateBasis == RateBasis.Percentage && !r.IsIncludedInPrice &&
                r.CountsTowardLocalCap != false)
                localLines.Add((line, r.Rate));
        }

        // Compound base = pre-tax price + all non-compound retailer taxes
        var compoundBase = subtotal + retailSubtotal;

        foreach (var r in rates.Where(r => r.IsCompound))
        {
            var tax = r.RateBasis == RateBasis.Percentage
                ? compoundBase * r.Rate
                : ComputeTax(r, subtotal, quantity, options);
            lines.Add(new TaxRateLine(r.Name, r.Rate, r.RateBasis, r.Unit, r.SaleContext,
                r.RemittancePoint, tax, r.EffectiveDate, r.IsIncludedInPrice, IsCompound: true));
        }

        var customerTax  = lines.Where(l => !l.IsIncludedInPrice).Sum(l => l.TaxAmount);
        var embeddedTax  = lines.Where(l => l.IsIncludedInPrice).Sum(l => l.TaxAmount);
        // Compound rates (tax-on-tax) have a different base (subtotal + retail taxes),
        // so they cannot be meaningfully summed with regular percentage rates.
        var combinedRate = rates
            .Where(r => r.RateBasis == RateBasis.Percentage && !r.IsIncludedInPrice && !r.IsCompound)
            .Sum(r => r.Rate);

        // ── Local rate cap (e.g. Texas caps combined local tax at 2%) ───────
        // Only the local tiers' cap-eligible percentage taxes are capped; the state rate,
        // flat-rate excise taxes, compound taxes and cap-exempt districts are not.
        var localRate = localLines.Sum(l => l.Rate);
        if (localRate > 0)
        {
            var profile = await db.StateTaxProfiles
                .FirstOrDefaultAsync(p => p.StateCode == jur.StateCode);
            if (profile?.HasLocalRateCap == true && profile.LocalRateCap.HasValue &&
                localRate > profile.LocalRateCap.Value)
            {
                var cap = profile.LocalRateCap.Value;
                var localTax = localLines.Sum(l => l.Line.TaxAmount);
                // Scale the local taxes down to the cap (equals subtotal × cap when no
                // transaction bracket narrows any of them).
                customerTax  = customerTax - localTax + localTax * cap / localRate;
                combinedRate = combinedRate - localRate + cap;
            }
        }

        return new TaxCalcResult(customerTax, embeddedTax, combinedRate, lines, jur.JurisdictionName);
    }

    // ── Chain resolution ────────────────────────────────────────────────────

    private static async Task<List<Jurisdiction>> LoadChainAsync(AppDbContext db, Jurisdiction start)
    {
        var chain = new List<Jurisdiction> { start };
        var seen = new HashSet<int> { start.Id };
        var parentId = start.ParentId;

        while (parentId.HasValue && chain.Count < MaxChainDepth && seen.Add(parentId.Value))
        {
            var parent = await db.Jurisdictions.FindAsync(parentId.Value);
            if (parent is null || parent.JurisdictionType == JurisdictionType.Country) break;
            chain.Add(parent);
            parentId = parent.ParentId;
        }

        return chain;
    }

    /// <summary>A retail sales-tax percentage: the kind of row a combined rate already includes.</summary>
    private static bool IsChainSalesPercentage(TaxRate r) =>
        r.TaxType == TaxType.SalesTax && r.RateBasis == RateBasis.Percentage
        && !r.IsIncludedInPrice && !r.IsCompound;

    private static bool IsLocalTier(JurisdictionType t) =>
        t is JurisdictionType.County or JurisdictionType.City or JurisdictionType.District;

    /// <summary>
    /// Picks each jurisdiction's applicable rows. With a category, a jurisdiction's rows for
    /// that category win; a tier with none falls back to its general (uncategorised) rows, so
    /// a county's general add-on still stacks on a state's category rate. Rows that differ
    /// only by category (the per-leaf-category seed copies of one general rate) count once.
    /// </summary>
    private static IEnumerable<TaxRate> SelectApplicableRates(List<TaxRate> candidates, int? taxCategoryId)
    {
        foreach (var perJurisdiction in candidates.GroupBy(r => r.JurisdictionId))
        {
            IEnumerable<TaxRate> rows = perJurisdiction;
            if (taxCategoryId.HasValue)
            {
                var forCategory = perJurisdiction.Where(r => r.TaxCategoryId == taxCategoryId).ToList();
                rows = forCategory.Count > 0
                    ? forCategory
                    : perJurisdiction.Where(r => r.TaxCategoryId == null);
            }

            foreach (var r in rows
                .GroupBy(r => (r.Name, r.Rate, r.RateBasis, r.TaxType, r.ProductCategory, r.SaleContext,
                               r.MinAbv, r.MaxAbv, r.MinTaxableAmount, r.MaxTaxableAmount,
                               r.IsCompound, r.IsIncludedInPrice))
                .Select(g => g.OrderBy(r => r.Id).First()))
                yield return r;
        }
    }

    /// <summary>
    /// ABV tiers: the upper bound is inclusive ("X% or less"). A value exactly on a tier
    /// boundary belongs to the lower tier, so the lower bound is exclusive only when another
    /// candidate rate of the same product ends at that boundary; a stand-alone lower bound
    /// ("0.5% or more") stays inclusive.
    /// </summary>
    private static bool AbvMatches(TaxRate r, decimal abv, IReadOnlyList<TaxRate> all)
    {
        if (r.MaxAbv.HasValue && abv > r.MaxAbv.Value) return false;
        if (!r.MinAbv.HasValue || abv > r.MinAbv.Value) return true;
        if (abv < r.MinAbv.Value) return false;

        // abv == MinAbv: exclusive when an adjacent lower tier claims the boundary.
        var min = r.MinAbv.Value;
        return !all.Any(o => !ReferenceEquals(o, r)
                             && o.ProductCategory == r.ProductCategory
                             && o.JurisdictionId == r.JurisdictionId
                             && o.MaxAbv == min);
    }

    // ── Per-rate calculation ────────────────────────────────────────────────

    private static decimal ComputeTax(TaxRate r, decimal subtotal, int quantity, TaxCalcOptions options)
    {
        var taxable = ApplyBracket(r, subtotal, quantity);

        decimal raw = r.RateBasis switch
        {
            RateBasis.Percentage            => taxable * r.Rate,
            // WholesalePrice is per unit (see TaxCalcOptions); scale it by quantity like the retail subtotal.
            RateBasis.PercentageOfWholesale => (options.WholesalePrice.HasValue ? options.WholesalePrice.Value * quantity : subtotal) * r.Rate,
            RateBasis.FlatPerUnit           => r.Rate * quantity,
            RateBasis.FlatPerVolume         => r.Rate * ((options.Volume ?? 1m) * quantity),
            RateBasis.FlatPerWeight         => r.Rate * ((options.Weight ?? 1m) * quantity),
            RateBasis.FlatPerProofGallon    => r.Rate * ((options.Volume ?? 1m) * quantity),
            _                               => 0m,
        };

        // Per-unit cap (e.g. federal large cigar: 52.75% but capped at $0.4026/cigar)
        if (r.FlatCapPerUnit.HasValue)
            raw = Math.Min(raw, r.FlatCapPerUnit.Value * quantity);

        return raw;
    }

    // ── Bracket helper ──────────────────────────────────────────────────────

    private static decimal ApplyBracket(TaxRate r, decimal subtotal, int quantity)
    {
        if (r.MinTaxableAmount == null && r.MaxTaxableAmount == null)
            return subtotal;

        // Brackets apply per unit
        var unitPrice   = subtotal / quantity;
        var bracketMin  = r.MinTaxableAmount ?? 0m;
        var bracketMax  = r.MaxTaxableAmount ?? decimal.MaxValue;
        var taxableUnit = Math.Max(0m, Math.Min(unitPrice, bracketMax) - bracketMin);
        return taxableUnit * quantity;
    }
}
