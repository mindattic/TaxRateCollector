using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.Infrastructure.Services;

public class TaxCalculator(IDbContextFactory<AppDbContext> dbFactory) : ITaxCalculator
{
    public async Task<TaxCalcResult?> CalculateAsync(
        int jurisdictionId, decimal price, int quantity, TaxCalcOptions? options = null)
    {
        options ??= new TaxCalcOptions();

        await using var db = await dbFactory.CreateDbContextAsync();

        var jur = await db.Jurisdictions.FindAsync(jurisdictionId);
        if (jur is null) return null;

        var subtotal = price * quantity;

        // ── Origin-based sourcing rule ────────────────────────────────────────
        // For intrastate sales in origin-based states (IL, TX, TN, PA, etc.) the
        // applicable rates are those at the SELLER's location, not the buyer's.
        // When SellerJurisdictionId is supplied and the state uses OriginBased
        // sourcing, swap the effective jurisdiction used for rate lookup.
        var effectiveJurisdictionId = jurisdictionId;
        if (options.SellerJurisdictionId.HasValue)
        {
            var sellerJur = await db.Jurisdictions.FindAsync(options.SellerJurisdictionId.Value);
            if (sellerJur?.StateCode == jur.StateCode) // intrastate only
            {
                var profile = await db.StateTaxProfiles
                    .FirstOrDefaultAsync(p => p.StateCode == jur.StateCode);
                if (profile?.IntrastateSourcingRule == SourcingRule.OriginBased)
                {
                    effectiveJurisdictionId = options.SellerJurisdictionId.Value;
                    jur = sellerJur;
                }
                // Modified (CA): state/county = origin, city = destination.
                // Full Mixed-tier support requires splitting the query by tier — deferred.
            }
        }

        // ── Load rates ────────────────────────────────────────────────────────
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var ratesQuery = db.TaxRates
            .Where(t => t.JurisdictionId == effectiveJurisdictionId
                     && t.IsCurrent
                     && t.AutoApprove
                     && (t.EffectiveDate == null || t.EffectiveDate <= today)
                     && (t.ExpirationDate == null || t.ExpirationDate > today));

        if (options.TaxCategoryId.HasValue)
            ratesQuery = ratesQuery.Where(t => t.TaxCategoryId == options.TaxCategoryId);

        var rates = await ratesQuery.OrderBy(t => t.Name).ToListAsync();
        if (rates.Count == 0) return null;

        // ── Filter by SaleContext and ABV ─────────────────────────────────────
        if (options.SaleContext != SaleContext.Any)
            rates = rates
                .Where(r => r.SaleContext == SaleContext.Any || r.SaleContext == options.SaleContext)
                .ToList();

        if (options.Abv.HasValue)
        {
            var abv = options.Abv.Value;
            rates = rates
                .Where(r => (r.MinAbv == null || abv >= r.MinAbv) &&
                            (r.MaxAbv == null || abv < r.MaxAbv))
                .ToList();
        }

        if (rates.Count == 0) return null;

        // ── Two-pass: non-compound first, then compound ───────────────────────
        // Compound rates (e.g. Chicago Matching Tax) apply to price + all retail taxes.
        var lines = new List<TaxRateLine>();
        decimal retailSubtotal = 0m;   // running sum of non-included-in-price, non-compound taxes

        foreach (var r in rates.Where(r => !r.IsCompound))
        {
            var tax = ComputeTax(r, subtotal, quantity, options);
            lines.Add(new TaxRateLine(r.Name, r.Rate, r.RateBasis, r.Unit, r.SaleContext,
                r.RemittancePoint, tax, r.EffectiveDate, r.IsIncludedInPrice, IsCompound: false));
            if (!r.IsIncludedInPrice)
                retailSubtotal += tax;
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

        // ── Local rate cap (e.g. Texas caps combined local tax at 2%) ──────────
        // For county/city tier jurisdictions, the entire rate set is "local".
        // Cap is enforced at both the dollar amount and combined rate fields.
        // Flat-rate excise taxes and compound taxes are not subject to the cap.
        if (jur.JurisdictionType != JurisdictionType.Country &&
            jur.JurisdictionType != JurisdictionType.State)
        {
            var profile = await db.StateTaxProfiles
                .FirstOrDefaultAsync(p => p.StateCode == jur.StateCode);
            if (profile?.HasLocalRateCap == true && profile.LocalRateCap.HasValue &&
                combinedRate > profile.LocalRateCap.Value)
            {
                var exemptFromCap = lines
                    .Where(l => !l.IsIncludedInPrice && (l.IsCompound || l.Basis != RateBasis.Percentage))
                    .Sum(l => l.TaxAmount);
                customerTax  = exemptFromCap + subtotal * profile.LocalRateCap.Value;
                combinedRate = profile.LocalRateCap.Value;
            }
        }

        return new TaxCalcResult(customerTax, embeddedTax, combinedRate, lines, jur.JurisdictionName);
    }

    // ── Per-rate calculation ──────────────────────────────────────────────────

    private static decimal ComputeTax(TaxRate r, decimal subtotal, int quantity, TaxCalcOptions options)
    {
        var taxable = ApplyBracket(r, subtotal, quantity);

        decimal raw = r.RateBasis switch
        {
            RateBasis.Percentage            => taxable * r.Rate,
            RateBasis.PercentageOfWholesale => (options.WholesalePrice ?? subtotal) * r.Rate,
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

    // ── Bracket helper ────────────────────────────────────────────────────────

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
