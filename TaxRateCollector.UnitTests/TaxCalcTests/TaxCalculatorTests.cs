using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Data;
using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.UnitTests.TaxCalcTests;

[TestFixture]
public class TaxCalculatorTests
{
    private static IDbContextFactory<AppDbContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContextFactory(opts);
    }

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> opts)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(opts);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(new AppDbContext(opts));
    }

    private static async Task<(Jurisdiction j, ScrapeRun run)> SeedJurisdiction(
        AppDbContext db, string name = "Cook County")
    {
        var run = new ScrapeRun { StartedAt = DateTime.UtcNow.ToString("o"), Status = ScrapeStatus.Completed };
        db.ScrapeRuns.Add(run);
        var j = new Jurisdiction { StateCode = "IL", JurisdictionName = name, FipsCode = "17031", JurisdictionType = JurisdictionType.County };
        db.Jurisdictions.Add(j);
        await db.SaveChangesAsync();
        return (j, run);
    }

    private static TaxRate MakeRate(int jId, int runId, decimal rate,
        RateBasis basis = RateBasis.Percentage,
        bool isIncluded = false, bool isCompound = false,
        decimal? minTaxable = null, decimal? maxTaxable = null,
        decimal? flatCap = null, string name = "Sales Tax",
        decimal? minAbv = null, decimal? maxAbv = null,
        SaleContext saleContext = SaleContext.Any)
        => new TaxRate
        {
            JurisdictionId = jId, ScrapeRunId = runId,
            Name = name, Rate = rate, RateBasis = basis,
            IsIncludedInPrice = isIncluded, IsCompound = isCompound,
            MinTaxableAmount = minTaxable, MaxTaxableAmount = maxTaxable,
            FlatCapPerUnit = flatCap,
            MinAbv = minAbv, MaxAbv = maxAbv,
            SaleContext = saleContext,
            EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ScrapedAt = DateTime.UtcNow.ToString("o"),
            IsCurrent = true,
        };

    // ── Basic percentage ──────────────────────────────────────────────────────

    [Test]
    public async Task Calculate_PercentageRate_ReturnsCorrectAmount()
    {
        var factory = CreateFactory();
        await using var db = factory.CreateDbContext();
        var (j, run) = await SeedJurisdiction(db);
        db.TaxRates.Add(MakeRate(j.Id, run.Id, 0.1025m));
        await db.SaveChangesAsync();

        var result = await new TaxCalculator(factory).CalculateAsync(j.Id, 100m, 2);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.TotalTaxAmount, Is.EqualTo(20.50m));
        Assert.That(result.TotalPercentageRate, Is.EqualTo(0.1025m));
    }

    [Test]
    public async Task Calculate_UnknownJurisdiction_ReturnsNull()
    {
        var factory = CreateFactory();
        Assert.That(await new TaxCalculator(factory).CalculateAsync(9999, 100m, 1), Is.Null);
    }

    [Test]
    public async Task Calculate_NoCurrentRates_ReturnsNull()
    {
        var factory = CreateFactory();
        await using var db = factory.CreateDbContext();
        var (j, _) = await SeedJurisdiction(db);
        // Jurisdiction exists but no IsCurrent rates

        Assert.That(await new TaxCalculator(factory).CalculateAsync(j.Id, 100m, 1), Is.Null);
    }

    // ── IsIncludedInPrice ─────────────────────────────────────────────────────

    [Test]
    public async Task Calculate_IncludedInPriceTax_ExcludedFromCustomerTotal()
    {
        var factory = CreateFactory();
        await using var db = factory.CreateDbContext();
        var (j, run) = await SeedJurisdiction(db);
        db.TaxRates.Add(MakeRate(j.Id, run.Id, 0.10m, isIncluded: false, name: "Sales Tax"));
        db.TaxRates.Add(MakeRate(j.Id, run.Id, 0.05m, isIncluded: true, name: "Beer Excise"));
        await db.SaveChangesAsync();

        var result = await new TaxCalculator(factory).CalculateAsync(j.Id, 100m, 1);

        Assert.That(result!.TotalTaxAmount, Is.EqualTo(10m));
        Assert.That(result.TotalIncludedInPriceAmount, Is.EqualTo(5m));
    }

    [Test]
    public async Task Calculate_IncludedInPriceTax_NotInTotalPercentageRate()
    {
        var factory = CreateFactory();
        await using var db = factory.CreateDbContext();
        var (j, run) = await SeedJurisdiction(db);
        db.TaxRates.Add(MakeRate(j.Id, run.Id, 0.10m, isIncluded: false, name: "Sales Tax"));
        db.TaxRates.Add(MakeRate(j.Id, run.Id, 0.05m, isIncluded: true, name: "Excise"));
        await db.SaveChangesAsync();

        var result = await new TaxCalculator(factory).CalculateAsync(j.Id, 100m, 1);

        Assert.That(result!.TotalPercentageRate, Is.EqualTo(0.10m));
    }

    // ── IsCompound ────────────────────────────────────────────────────────────

    [Test]
    public async Task Calculate_CompoundTax_AppliesTo_PricePlusOtherTaxes()
    {
        var factory = CreateFactory();
        await using var db = factory.CreateDbContext();
        var (j, run) = await SeedJurisdiction(db);
        db.TaxRates.Add(MakeRate(j.Id, run.Id, 0.10m, isCompound: false, name: "Base Tax"));
        db.TaxRates.Add(MakeRate(j.Id, run.Id, 0.01m, isCompound: true, name: "Matching Tax"));
        await db.SaveChangesAsync();

        // Price: $100, Base Tax: $10, Compound Base: $110, Matching: $1.10
        var result = await new TaxCalculator(factory).CalculateAsync(j.Id, 100m, 1);

        var matchingLine = result!.RateLines.Single(l => l.RateName == "Matching Tax");
        Assert.That(matchingLine.TaxAmount, Is.EqualTo(1.10m).Within(0.001m));
        Assert.That(matchingLine.IsCompound, Is.True);
    }

    // ── MinTaxableAmount / MaxTaxableAmount brackets ──────────────────────────

    [Test]
    public async Task Calculate_Bracket_OnlyTaxesAmountWithinRange()
    {
        var factory = CreateFactory();
        await using var db = factory.CreateDbContext();
        var (j, run) = await SeedJurisdiction(db);
        // Tennessee single-article: 2.75% on $1600–$3200 per article
        db.TaxRates.Add(MakeRate(j.Id, run.Id, 0.0275m,
            minTaxable: 1600m, maxTaxable: 3200m, name: "Middle Bracket"));
        await db.SaveChangesAsync();

        // Unit price $2000: taxable = $2000 - $1600 = $400
        var result = await new TaxCalculator(factory).CalculateAsync(j.Id, 2000m, 1);

        Assert.That(result!.TotalTaxAmount, Is.EqualTo(400m * 0.0275m).Within(0.001m));
    }

    [Test]
    public async Task Calculate_Bracket_AmountBelowMin_ProducesZeroTax()
    {
        var factory = CreateFactory();
        await using var db = factory.CreateDbContext();
        var (j, run) = await SeedJurisdiction(db);
        db.TaxRates.Add(MakeRate(j.Id, run.Id, 0.0275m,
            minTaxable: 1600m, maxTaxable: 3200m));
        await db.SaveChangesAsync();

        // $500 is below the $1600 min — rate exists but taxable amount is zero
        var result = await new TaxCalculator(factory).CalculateAsync(j.Id, 500m, 1);

        Assert.That(result!.TotalTaxAmount, Is.EqualTo(0m));
    }

    [Test]
    public async Task Calculate_Bracket_AmountAboveMax_CapsBracket()
    {
        var factory = CreateFactory();
        await using var db = factory.CreateDbContext();
        var (j, run) = await SeedJurisdiction(db);
        db.TaxRates.Add(MakeRate(j.Id, run.Id, 0.0275m,
            minTaxable: 1600m, maxTaxable: 3200m));
        await db.SaveChangesAsync();

        // $5000 is above the $3200 max — only tax $3200 - $1600 = $1600
        var result = await new TaxCalculator(factory).CalculateAsync(j.Id, 5000m, 1);

        Assert.That(result!.TotalTaxAmount, Is.EqualTo(1600m * 0.0275m).Within(0.001m));
    }

    // ── FlatCapPerUnit ────────────────────────────────────────────────────────

    [Test]
    public async Task Calculate_FlatCapPerUnit_CapsCalculatedTax()
    {
        var factory = CreateFactory();
        await using var db = factory.CreateDbContext();
        var (j, run) = await SeedJurisdiction(db);
        // 52.75% of price BUT capped at $0.4026/unit
        db.TaxRates.Add(MakeRate(j.Id, run.Id, 0.5275m, flatCap: 0.4026m, name: "Large Cigar"));
        await db.SaveChangesAsync();

        // $5 cigar: 52.75% = $2.6375, capped at $0.4026
        var result = await new TaxCalculator(factory).CalculateAsync(j.Id, 5m, 1);

        Assert.That(result!.TotalTaxAmount, Is.EqualTo(0.4026m).Within(0.0001m));
    }

    [Test]
    public async Task Calculate_FlatCapPerUnit_NotApplied_WhenTaxBelowCap()
    {
        var factory = CreateFactory();
        await using var db = factory.CreateDbContext();
        var (j, run) = await SeedJurisdiction(db);
        db.TaxRates.Add(MakeRate(j.Id, run.Id, 0.10m, flatCap: 100m, name: "Tax"));
        await db.SaveChangesAsync();

        // 10% of $5 = $0.50, well under $100 cap
        var result = await new TaxCalculator(factory).CalculateAsync(j.Id, 5m, 1);

        Assert.That(result!.TotalTaxAmount, Is.EqualTo(0.50m).Within(0.001m));
    }

    // ── FlatPerUnit ───────────────────────────────────────────────────────────

    [Test]
    public async Task Calculate_FlatPerUnit_MultipliesRateByQuantity()
    {
        var factory = CreateFactory();
        await using var db = factory.CreateDbContext();
        var (j, run) = await SeedJurisdiction(db);
        db.TaxRates.Add(MakeRate(j.Id, run.Id, 0.231m, basis: RateBasis.FlatPerUnit, name: "Cigarette Tax"));
        await db.SaveChangesAsync();

        var result = await new TaxCalculator(factory).CalculateAsync(j.Id, 7m, 3);

        Assert.That(result!.TotalTaxAmount, Is.EqualTo(0.231m * 3).Within(0.001m));
    }

    // ── FlatPerVolume ─────────────────────────────────────────────────────────

    [Test]
    public async Task Calculate_FlatPerVolume_UsesVolumeOption()
    {
        var factory = CreateFactory();
        await using var db = factory.CreateDbContext();
        var (j, run) = await SeedJurisdiction(db);
        // $1.07/gallon beer excise
        db.TaxRates.Add(MakeRate(j.Id, run.Id, 1.07m, basis: RateBasis.FlatPerVolume, name: "Beer Excise"));
        await db.SaveChangesAsync();

        // 2 cases, each 0.5 gal = 1 gallon total
        var result = await new TaxCalculator(factory).CalculateAsync(j.Id, 10m, 2,
            new TaxCalcOptions(Volume: 0.5m));

        Assert.That(result!.TotalTaxAmount, Is.EqualTo(1.07m * 2 * 0.5m).Within(0.001m));
    }

    // ── PercentageOfWholesale ─────────────────────────────────────────────────

    [Test]
    public async Task Calculate_PercentageOfWholesale_UsesWholesalePrice()
    {
        var factory = CreateFactory();
        await using var db = factory.CreateDbContext();
        var (j, run) = await SeedJurisdiction(db);
        // Rhode Island: 80% of wholesale price on OTP
        db.TaxRates.Add(MakeRate(j.Id, run.Id, 0.80m,
            basis: RateBasis.PercentageOfWholesale, name: "OTP Excise"));
        await db.SaveChangesAsync();

        var result = await new TaxCalculator(factory).CalculateAsync(j.Id, 15m, 1,
            new TaxCalcOptions(WholesalePrice: 10m));

        // 80% of $10 wholesale = $8
        Assert.That(result!.TotalTaxAmount, Is.EqualTo(8m).Within(0.001m));
    }

    // ── ABV filtering ─────────────────────────────────────────────────────────

    [Test]
    public async Task Calculate_AbvFilter_ExcludesRatesOutsideAbvRange()
    {
        var factory = CreateFactory();
        await using var db = factory.CreateDbContext();
        var (j, run) = await SeedJurisdiction(db);
        // Spirits > 40% ABV rate
        db.TaxRates.Add(MakeRate(j.Id, run.Id, 0.36m, name: "Spirits High",
            minAbv: 0.40m, basis: RateBasis.FlatPerUnit));
        // Beer < 15% ABV rate
        db.TaxRates.Add(MakeRate(j.Id, run.Id, 0.05m, name: "Beer Low",
            maxAbv: 0.15m, basis: RateBasis.FlatPerUnit));
        await db.SaveChangesAsync();

        // Wine at 13% — should match Beer Low only
        var result = await new TaxCalculator(factory).CalculateAsync(j.Id, 20m, 1,
            new TaxCalcOptions(Abv: 0.13m));

        Assert.That(result!.RateLines, Has.Count.EqualTo(1));
        Assert.That(result.RateLines[0].RateName, Is.EqualTo("Beer Low"));
    }

    [Test]
    public async Task Calculate_AbvFilter_IncludesRatesWithNoAbvConstraint()
    {
        var factory = CreateFactory();
        await using var db = factory.CreateDbContext();
        var (j, run) = await SeedJurisdiction(db);
        db.TaxRates.Add(MakeRate(j.Id, run.Id, 0.10m, name: "Sales Tax")); // no ABV constraint
        db.TaxRates.Add(MakeRate(j.Id, run.Id, 0.36m, name: "High ABV", minAbv: 0.40m, basis: RateBasis.FlatPerUnit));
        await db.SaveChangesAsync();

        // 5% beer — Sales Tax matches (no constraint), High ABV filtered out
        var result = await new TaxCalculator(factory).CalculateAsync(j.Id, 10m, 1,
            new TaxCalcOptions(Abv: 0.05m));

        Assert.That(result!.RateLines.All(l => l.RateName == "Sales Tax"), Is.True);
    }

    // ── SaleContext filtering ─────────────────────────────────────────────────

    [Test]
    public async Task Calculate_SaleContextFilter_ExcludesNonMatchingContext()
    {
        var factory = CreateFactory();
        await using var db = factory.CreateDbContext();
        var (j, run) = await SeedJurisdiction(db);
        db.TaxRates.Add(MakeRate(j.Id, run.Id, 0.10m, name: "Any Tax", saleContext: SaleContext.Any));
        db.TaxRates.Add(MakeRate(j.Id, run.Id, 0.03m, name: "On-Prem Tax", saleContext: SaleContext.OnPremise));
        await db.SaveChangesAsync();

        // Off-premise sale — should only get the Any rate
        var result = await new TaxCalculator(factory).CalculateAsync(j.Id, 100m, 1,
            new TaxCalcOptions(SaleContext: SaleContext.OffPremise));

        Assert.That(result!.RateLines, Has.Count.EqualTo(1));
        Assert.That(result.RateLines[0].RateName, Is.EqualTo("Any Tax"));
    }

    // ── Multiple rates ────────────────────────────────────────────────────────

    [Test]
    public async Task Calculate_MultipleRates_SumAllToTotalTaxAmount()
    {
        var factory = CreateFactory();
        await using var db = factory.CreateDbContext();
        var (j, run) = await SeedJurisdiction(db);
        db.TaxRates.Add(MakeRate(j.Id, run.Id, 0.0625m, name: "State"));
        db.TaxRates.Add(MakeRate(j.Id, run.Id, 0.02m,   name: "County"));
        db.TaxRates.Add(MakeRate(j.Id, run.Id, 0.0175m, name: "City"));
        await db.SaveChangesAsync();

        var result = await new TaxCalculator(factory).CalculateAsync(j.Id, 100m, 1);

        Assert.That(result!.TotalTaxAmount, Is.EqualTo(10m).Within(0.001m));
        Assert.That(result.RateLines, Has.Count.EqualTo(3));
    }
}
