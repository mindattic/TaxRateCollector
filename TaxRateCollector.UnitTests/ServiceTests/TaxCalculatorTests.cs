using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Data;
using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.UnitTests.ServiceTests;

[TestFixture]
public class TaxCalculatorTests
{
    // ── Factory helpers ───────────────────────────────────────────────────────

    private static IDbContextFactory<AppDbContext> CreateFactory(string? name = null)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString())
            .Options;
        return new TestFactory(opts);
    }

    private sealed class TestFactory(DbContextOptions<AppDbContext> opts) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(opts);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(new AppDbContext(opts));
    }

    private static async Task<(AppDbContext db, Jurisdiction jur)> SeedJurisdictionAsync(
        IDbContextFactory<AppDbContext> factory, string name = "Test State")
    {
        var db = factory.CreateDbContext();
        var jur = new Jurisdiction
        {
            JurisdictionName = name,
            StateCode = "IL",
            FipsCode = "17",
            JurisdictionType = JurisdictionType.State,
            IsActive = true,
        };
        db.Jurisdictions.Add(jur);
        await db.SaveChangesAsync();
        return (db, jur);
    }

    private static TaxRate MakeRate(
        int jurisdictionId,
        string name = "Sales Tax",
        decimal rate = 0.0625m,
        RateBasis basis = RateBasis.Percentage,
        bool isCurrent = true,
        bool isCompound = false,
        bool isIncludedInPrice = false,
        SaleContext saleContext = SaleContext.Any,
        decimal? minAbv = null,
        decimal? maxAbv = null,
        decimal? minTaxableAmount = null,
        decimal? maxTaxableAmount = null,
        decimal? flatCapPerUnit = null,
        string unit = "") => new()
    {
        JurisdictionId = jurisdictionId,
        ScrapeRunId = 1,
        Name = name,
        Rate = rate,
        RateBasis = basis,
        IsCurrent = isCurrent,
        IsCompound = isCompound,
        IsIncludedInPrice = isIncludedInPrice,
        SaleContext = saleContext,
        MinAbv = minAbv,
        MaxAbv = maxAbv,
        MinTaxableAmount = minTaxableAmount,
        MaxTaxableAmount = maxTaxableAmount,
        FlatCapPerUnit = flatCapPerUnit,
        Unit = unit,
        ScrapedAt = DateTime.UtcNow.ToString("o"),
        NeedsReview = false,
    };

    // ── Tests: null / empty results ───────────────────────────────────────────

    [Test]
    public async Task UnknownJurisdiction_ReturnsNull()
    {
        var factory = CreateFactory();
        var calc = new TaxCalculator(factory);
        var result = await calc.CalculateAsync(999, 100m, 1);
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task NoCurrentRates_ReturnsNull()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        db.TaxRates.Add(MakeRate(jur.Id, isCurrent: false));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        var result = await calc.CalculateAsync(jur.Id, 100m, 1);
        Assert.That(result, Is.Null);
    }

    // ── Tests: basic percentage rate ──────────────────────────────────────────

    [Test]
    public async Task PercentageRate_CalculatesCorrectly()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        db.TaxRates.Add(MakeRate(jur.Id, rate: 0.0625m));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        var result = await calc.CalculateAsync(jur.Id, 100m, 1);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.TotalTaxAmount, Is.EqualTo(6.25m));
    }

    [Test]
    public async Task PercentageRate_MultipleQuantity_ScalesCorrectly()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        db.TaxRates.Add(MakeRate(jur.Id, rate: 0.10m));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        var result = await calc.CalculateAsync(jur.Id, 50m, 4);

        // subtotal = 200, tax = 20
        Assert.That(result!.TotalTaxAmount, Is.EqualTo(20m));
    }

    [Test]
    public async Task MultipleRates_TotalsAreSummed()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        db.TaxRates.Add(MakeRate(jur.Id, "State Tax",  rate: 0.0625m));
        db.TaxRates.Add(MakeRate(jur.Id, "County Tax", rate: 0.0100m));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        var result = await calc.CalculateAsync(jur.Id, 100m, 1);

        Assert.That(result!.TotalTaxAmount, Is.EqualTo(7.25m));
        Assert.That(result.RateLines, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task JurisdictionName_IsPopulatedOnResult()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory, "Illinois");
        db.TaxRates.Add(MakeRate(jur.Id));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        var result = await calc.CalculateAsync(jur.Id, 100m, 1);

        Assert.That(result!.JurisdictionName, Is.EqualTo("Illinois"));
    }

    // ── Tests: flat rate bases ────────────────────────────────────────────────

    [Test]
    public async Task FlatPerUnit_Rate_CalculatesCorrectly()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        db.TaxRates.Add(MakeRate(jur.Id, rate: 0.50m, basis: RateBasis.FlatPerUnit));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        var result = await calc.CalculateAsync(jur.Id, 10m, 3);

        // 0.50 * 3 units = 1.50
        Assert.That(result!.TotalTaxAmount, Is.EqualTo(1.50m));
    }

    [Test]
    public async Task FlatPerVolume_UsesProvidedVolume()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        db.TaxRates.Add(MakeRate(jur.Id, rate: 2.00m, basis: RateBasis.FlatPerVolume));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        var result = await calc.CalculateAsync(jur.Id, 20m, 2,
            new TaxCalcOptions(Volume: 0.75m)); // 0.75 gallons/unit

        // 2.00 * 0.75 * 2 = 3.00
        Assert.That(result!.TotalTaxAmount, Is.EqualTo(3.00m));
    }

    [Test]
    public async Task FlatPerVolume_DefaultsVolumeToOne_WhenNotProvided()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        db.TaxRates.Add(MakeRate(jur.Id, rate: 2.00m, basis: RateBasis.FlatPerVolume));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        var result = await calc.CalculateAsync(jur.Id, 20m, 3);

        // volume defaults to 1, so 2.00 * 1 * 3 = 6.00
        Assert.That(result!.TotalTaxAmount, Is.EqualTo(6.00m));
    }

    [Test]
    public async Task PercentageOfWholesale_UsesWholesalePrice()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        db.TaxRates.Add(MakeRate(jur.Id, rate: 0.10m, basis: RateBasis.PercentageOfWholesale));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        var result = await calc.CalculateAsync(jur.Id, 100m, 1,
            new TaxCalcOptions(WholesalePrice: 60m));

        // 60 * 0.10 = 6.00
        Assert.That(result!.TotalTaxAmount, Is.EqualTo(6.00m));
    }

    [Test]
    public async Task PercentageOfWholesale_FallsBackToSubtotal_WhenNoWholesalePrice()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        db.TaxRates.Add(MakeRate(jur.Id, rate: 0.10m, basis: RateBasis.PercentageOfWholesale));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        // No WholesalePrice → falls back to subtotal (100 * 1 = 100); 100 * 0.10 = 10.00
        var result = await calc.CalculateAsync(jur.Id, 100m, 1);

        Assert.That(result!.TotalTaxAmount, Is.EqualTo(10.00m));
    }

    [Test]
    public async Task FlatPerWeight_UsesProvidedWeight()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        // $1.20/oz smokeless tobacco excise
        db.TaxRates.Add(MakeRate(jur.Id, rate: 1.20m, basis: RateBasis.FlatPerWeight));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        // 2 cans, 1.2 oz each → 1.20 * 1.2 * 2 = $2.88
        var result = await calc.CalculateAsync(jur.Id, 10m, 2,
            new TaxCalcOptions(Weight: 1.2m));

        Assert.That(result!.TotalTaxAmount, Is.EqualTo(2.88m));
    }

    [Test]
    public async Task FlatPerWeight_DefaultsWeightToOne_WhenNotProvided()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        db.TaxRates.Add(MakeRate(jur.Id, rate: 1.20m, basis: RateBasis.FlatPerWeight));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        // no Weight option → defaults to 1, so 1.20 * 1 * 3 = $3.60
        var result = await calc.CalculateAsync(jur.Id, 10m, 3);

        Assert.That(result!.TotalTaxAmount, Is.EqualTo(3.60m));
    }

    [Test]
    public async Task FlatPerProofGallon_UsesVolumeAsProofGallons()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        // Federal spirits excise: $13.50/proof gallon
        db.TaxRates.Add(MakeRate(jur.Id, rate: 13.50m, basis: RateBasis.FlatPerProofGallon));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        // 2 bottles, 0.375 proof gallons each → 13.50 * 0.375 * 2 = $10.125
        var result = await calc.CalculateAsync(jur.Id, 30m, 2,
            new TaxCalcOptions(Volume: 0.375m));

        Assert.That(result!.TotalTaxAmount, Is.EqualTo(10.125m));
    }

    [Test]
    public async Task FlatPerProofGallon_DefaultsVolumeToOne_WhenNotProvided()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        db.TaxRates.Add(MakeRate(jur.Id, rate: 13.50m, basis: RateBasis.FlatPerProofGallon));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        // volume defaults to 1 proof gallon per unit, quantity=1 → $13.50
        var result = await calc.CalculateAsync(jur.Id, 30m, 1);

        Assert.That(result!.TotalTaxAmount, Is.EqualTo(13.50m));
    }

    // ── Tests: FlatCapPerUnit ─────────────────────────────────────────────────

    [Test]
    public async Task FlatCapPerUnit_LimitsExcessivePercentageTax()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        // 52.75% rate capped at $0.4026/unit (federal large cigar scenario)
        db.TaxRates.Add(MakeRate(jur.Id, rate: 0.5275m, basis: RateBasis.Percentage,
            flatCapPerUnit: 0.4026m));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        // price = $10/unit → uncapped tax = $5.275, capped at $0.4026
        var result = await calc.CalculateAsync(jur.Id, 10m, 1);

        Assert.That(result!.TotalTaxAmount, Is.EqualTo(0.4026m));
    }

    [Test]
    public async Task FlatCapPerUnit_DoesNotCapWhenBelowCap()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        db.TaxRates.Add(MakeRate(jur.Id, rate: 0.0625m, basis: RateBasis.Percentage,
            flatCapPerUnit: 100m));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        var result = await calc.CalculateAsync(jur.Id, 10m, 1);

        // 10 * 6.25% = 0.625 < cap of 100 → not capped
        Assert.That(result!.TotalTaxAmount, Is.EqualTo(0.625m));
    }

    [Test]
    public async Task FlatCapPerUnit_MultipliesCapByQuantity()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        db.TaxRates.Add(MakeRate(jur.Id, rate: 0.5275m, basis: RateBasis.Percentage,
            flatCapPerUnit: 0.4026m));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        // 5 units, each capped at $0.4026 → total $2.013
        var result = await calc.CalculateAsync(jur.Id, 10m, 5);

        Assert.That(result!.TotalTaxAmount, Is.EqualTo(0.4026m * 5));
    }

    // ── Tests: bracket (MinTaxableAmount / MaxTaxableAmount) ─────────────────

    [Test]
    public async Task MinTaxableAmount_ExcludesPortionBelowBracket()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        // Only the amount above $1000 is taxable
        db.TaxRates.Add(MakeRate(jur.Id, rate: 0.10m, minTaxableAmount: 1000m));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        // price = $1200/unit; taxable = $200; tax = $20
        var result = await calc.CalculateAsync(jur.Id, 1200m, 1);

        Assert.That(result!.TotalTaxAmount, Is.EqualTo(20m));
    }

    [Test]
    public async Task MinTaxableAmount_BelowFloor_ProducesZeroTax()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        db.TaxRates.Add(MakeRate(jur.Id, rate: 0.10m, minTaxableAmount: 500m));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        var result = await calc.CalculateAsync(jur.Id, 100m, 1);

        // 100 < 500 → taxable portion = 0 → but result still non-null (rate exists)
        Assert.That(result!.TotalTaxAmount, Is.EqualTo(0m));
    }

    [Test]
    public async Task MaxTaxableAmount_CapsAmountBelowCeiling()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        // Only taxable up to $500
        db.TaxRates.Add(MakeRate(jur.Id, rate: 0.10m, maxTaxableAmount: 500m));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        // price = $1000; taxable = $500; tax = $50
        var result = await calc.CalculateAsync(jur.Id, 1000m, 1);

        Assert.That(result!.TotalTaxAmount, Is.EqualTo(50m));
    }

    [Test]
    public async Task Bracket_AppliesPerUnit_NotToSubtotal()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        // Taxable: amount above $50/unit
        db.TaxRates.Add(MakeRate(jur.Id, rate: 0.10m, minTaxableAmount: 50m));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        // 3 units at $80 each; per unit taxable = $30; total taxable = $90; tax = $9
        var result = await calc.CalculateAsync(jur.Id, 80m, 3);

        Assert.That(result!.TotalTaxAmount, Is.EqualTo(9m));
    }

    // ── Tests: compound rates ─────────────────────────────────────────────────

    [Test]
    public async Task CompoundRate_AppliesOnTopOfRetailTaxes()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        // Base: 10% retail sales tax
        db.TaxRates.Add(MakeRate(jur.Id, "Retail Tax", rate: 0.10m, isCompound: false));
        // Compound: 2% applied to price + retail taxes
        db.TaxRates.Add(MakeRate(jur.Id, "Matching Tax", rate: 0.02m, isCompound: true));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        // price = $100, retail tax = $10, compound base = $110, compound tax = $2.20
        var result = await calc.CalculateAsync(jur.Id, 100m, 1);

        var matchingLine = result!.RateLines.Single(l => l.RateName == "Matching Tax");
        Assert.That(matchingLine.TaxAmount, Is.EqualTo(2.20m));
        Assert.That(result.TotalTaxAmount, Is.EqualTo(12.20m)); // 10 + 2.20
    }

    [Test]
    public async Task CompoundRate_IsMarkedInRateLine()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        db.TaxRates.Add(MakeRate(jur.Id, "Base", rate: 0.10m));
        db.TaxRates.Add(MakeRate(jur.Id, "Compound", rate: 0.02m, isCompound: true));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        var result = await calc.CalculateAsync(jur.Id, 100m, 1);

        Assert.That(result!.RateLines.Single(l => l.RateName == "Compound").IsCompound, Is.True);
        Assert.That(result.RateLines.Single(l => l.RateName == "Base").IsCompound, Is.False);
    }

    // ── Tests: IsIncludedInPrice ──────────────────────────────────────────────

    [Test]
    public async Task IsIncludedInPrice_SegregatesEmbeddedTax()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        db.TaxRates.Add(MakeRate(jur.Id, "Sales Tax",  rate: 0.06m, isIncludedInPrice: false));
        db.TaxRates.Add(MakeRate(jur.Id, "Excise Tax",  rate: 0.10m, isIncludedInPrice: true));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        var result = await calc.CalculateAsync(jur.Id, 100m, 1);

        Assert.That(result!.TotalTaxAmount,             Is.EqualTo(6m));
        Assert.That(result.TotalIncludedInPriceAmount,  Is.EqualTo(10m));
    }

    [Test]
    public async Task IsIncludedInPrice_DoesNotContributeToCompoundBase()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        // Embedded excise (not added to retail base for compound calculation)
        db.TaxRates.Add(MakeRate(jur.Id, "Excise", rate: 0.20m, isIncludedInPrice: true));
        // Retail sales tax
        db.TaxRates.Add(MakeRate(jur.Id, "Sales",  rate: 0.10m, isIncludedInPrice: false));
        // Compound applied to price + retail (excise excluded from compound base)
        db.TaxRates.Add(MakeRate(jur.Id, "Compound", rate: 0.02m, isCompound: true));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        // price=$100, retail=$10, compoundBase=$110 (not $130), compound=$2.20
        var result = await calc.CalculateAsync(jur.Id, 100m, 1);

        var compoundLine = result!.RateLines.Single(l => l.RateName == "Compound");
        Assert.That(compoundLine.TaxAmount, Is.EqualTo(2.20m));
    }

    // ── Tests: TotalPercentageRate ────────────────────────────────────────────

    [Test]
    public async Task TotalPercentageRate_SumsOnlyNonEmbeddedPercentageRates()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        db.TaxRates.Add(MakeRate(jur.Id, "State",  rate: 0.0625m, isIncludedInPrice: false));
        db.TaxRates.Add(MakeRate(jur.Id, "County", rate: 0.0100m, isIncludedInPrice: false));
        db.TaxRates.Add(MakeRate(jur.Id, "Excise", rate: 0.2000m, isIncludedInPrice: true));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        var result = await calc.CalculateAsync(jur.Id, 100m, 1);

        Assert.That(result!.TotalPercentageRate, Is.EqualTo(0.0725m));
    }

    // ── Tests: SaleContext filtering ──────────────────────────────────────────

    [Test]
    public async Task SaleContext_Filter_ExcludesNonMatchingRate()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        db.TaxRates.Add(MakeRate(jur.Id, "DTC Rate",     rate: 0.10m, saleContext: SaleContext.DirectToConsumer));
        db.TaxRates.Add(MakeRate(jur.Id, "On-Premise Rate", rate: 0.08m, saleContext: SaleContext.OnPremise));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        var result = await calc.CalculateAsync(jur.Id, 100m, 1,
            new TaxCalcOptions(SaleContext: SaleContext.OnPremise));

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.RateLines, Has.Count.EqualTo(1));
        Assert.That(result.TotalTaxAmount, Is.EqualTo(8m));
    }

    [Test]
    public async Task SaleContext_Any_IncludesRatesWithAnyContext()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        db.TaxRates.Add(MakeRate(jur.Id, "General", rate: 0.06m, saleContext: SaleContext.Any));
        db.TaxRates.Add(MakeRate(jur.Id, "DTC",     rate: 0.02m, saleContext: SaleContext.DirectToConsumer));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        // In-person sale: only "Any" rate applies, not "Online"
        var result = await calc.CalculateAsync(jur.Id, 100m, 1,
            new TaxCalcOptions(SaleContext: SaleContext.OnPremise));

        Assert.That(result!.RateLines, Has.Count.EqualTo(1));
        Assert.That(result.TotalTaxAmount, Is.EqualTo(6m));
    }

    [Test]
    public async Task SaleContext_NoOptionsFilter_IncludesAllRates()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        db.TaxRates.Add(MakeRate(jur.Id, "General", rate: 0.06m, saleContext: SaleContext.Any));
        db.TaxRates.Add(MakeRate(jur.Id, "DTC",     rate: 0.02m, saleContext: SaleContext.DirectToConsumer));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        // Default SaleContext = Any: all rates pass the filter
        var result = await calc.CalculateAsync(jur.Id, 100m, 1);

        Assert.That(result!.RateLines, Has.Count.EqualTo(2));
        Assert.That(result.TotalTaxAmount, Is.EqualTo(8m));
    }

    // ── Tests: ABV filtering ──────────────────────────────────────────────────

    [Test]
    public async Task AbvFilter_ExcludesRateBelowMinAbv()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        // High-ABV spirits rate only applies above 20%
        db.TaxRates.Add(MakeRate(jur.Id, "Spirits",  rate: 0.20m, minAbv: 0.20m));
        db.TaxRates.Add(MakeRate(jur.Id, "Beer/Wine", rate: 0.05m, maxAbv: 0.20m));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        // ABV = 0.05 (5%) — below spirits threshold, within beer/wine range
        var result = await calc.CalculateAsync(jur.Id, 100m, 1,
            new TaxCalcOptions(Abv: 0.05m));

        Assert.That(result!.RateLines, Has.Count.EqualTo(1));
        Assert.That(result.RateLines[0].RateName, Is.EqualTo("Beer/Wine"));
    }

    [Test]
    public async Task AbvFilter_IncludesRateWithNoAbvConstraint()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        db.TaxRates.Add(MakeRate(jur.Id, "General", rate: 0.06m)); // no ABV constraint
        db.TaxRates.Add(MakeRate(jur.Id, "Spirits",  rate: 0.20m, minAbv: 0.20m));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        var result = await calc.CalculateAsync(jur.Id, 100m, 1, new TaxCalcOptions(Abv: 0.05m));

        // Only the unconstrained rate passes
        Assert.That(result!.RateLines, Has.Count.EqualTo(1));
        Assert.That(result.RateLines[0].RateName, Is.EqualTo("General"));
    }

    [Test]
    public async Task AbvFilter_AboveMaxAbv_ExcludesRate()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);
        db.TaxRates.Add(MakeRate(jur.Id, "Beer/Wine", rate: 0.05m, maxAbv: 0.20m));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        // ABV 0.40 (40%) exceeds maxAbv 0.20
        var result = await calc.CalculateAsync(jur.Id, 100m, 1, new TaxCalcOptions(Abv: 0.40m));

        Assert.That(result, Is.Null, "All rates filtered by ABV → null result");
    }

    // ── Tests: TaxCategoryId filtering ───────────────────────────────────────

    // ── Tests: origin-based sourcing ─────────────────────────────────────────

    [Test]
    public async Task OriginBased_IntrastateSale_UsesSellerJurisdiction()
    {
        var factory = CreateFactory();
        await using var db = factory.CreateDbContext();

        // Two jurisdictions in the same state (IL)
        var buyer = new Jurisdiction { JurisdictionName = "Chicago", StateCode = "IL", FipsCode = "17140", JurisdictionType = JurisdictionType.City, IsActive = true };
        var seller = new Jurisdiction { JurisdictionName = "Springfield", StateCode = "IL", FipsCode = "17170", JurisdictionType = JurisdictionType.City, IsActive = true };
        db.Jurisdictions.AddRange(buyer, seller);

        var profile = new StateTaxProfile { StateCode = "IL", StateName = "Illinois", IntrastateSourcingRule = SourcingRule.OriginBased, UpdatedAt = DateTime.UtcNow.ToString("o") };
        db.StateTaxProfiles.Add(profile);
        await db.SaveChangesAsync();

        db.TaxRates.Add(MakeRate(buyer.Id,  "Chicago Tax",     rate: 0.085m));
        db.TaxRates.Add(MakeRate(seller.Id, "Springfield Tax", rate: 0.065m));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        // Buyer is in Chicago, but seller is in Springfield; IL is origin-based
        var result = await calc.CalculateAsync(buyer.Id, 100m, 1,
            new TaxCalcOptions(SellerJurisdictionId: seller.Id));

        // Must use seller (Springfield) rate = 6.5%, not buyer (Chicago) 8.5%
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.TotalTaxAmount, Is.EqualTo(6.5m));
        Assert.That(result.JurisdictionName, Is.EqualTo("Springfield"));
    }

    [Test]
    public async Task OriginBased_InterstateSale_UsesBuyerJurisdiction()
    {
        var factory = CreateFactory();
        await using var db = factory.CreateDbContext();

        var buyer  = new Jurisdiction { JurisdictionName = "Chicago", StateCode = "IL", FipsCode = "17140", JurisdictionType = JurisdictionType.City, IsActive = true };
        var seller = new Jurisdiction { JurisdictionName = "Houston",  StateCode = "TX", FipsCode = "48201", JurisdictionType = JurisdictionType.City, IsActive = true };
        db.Jurisdictions.AddRange(buyer, seller);

        // IL is origin-based, but this is an interstate sale (seller in TX)
        var profile = new StateTaxProfile { StateCode = "IL", StateName = "Illinois", IntrastateSourcingRule = SourcingRule.OriginBased, UpdatedAt = DateTime.UtcNow.ToString("o") };
        db.StateTaxProfiles.Add(profile);
        await db.SaveChangesAsync();

        db.TaxRates.Add(MakeRate(buyer.Id,  "Chicago Tax", rate: 0.085m));
        db.TaxRates.Add(MakeRate(seller.Id, "Houston Tax", rate: 0.082m));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        // Interstate sale → destination-based regardless of IL origin rule
        var result = await calc.CalculateAsync(buyer.Id, 100m, 1,
            new TaxCalcOptions(SellerJurisdictionId: seller.Id));

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.TotalTaxAmount, Is.EqualTo(8.5m));
        Assert.That(result.JurisdictionName, Is.EqualTo("Chicago"));
    }

    [Test]
    public async Task DestinationBased_IntrastateSale_UsesBuyerJurisdiction()
    {
        var factory = CreateFactory();
        await using var db = factory.CreateDbContext();

        var buyer  = new Jurisdiction { JurisdictionName = "New York City", StateCode = "NY", FipsCode = "36061", JurisdictionType = JurisdictionType.City, IsActive = true };
        var seller = new Jurisdiction { JurisdictionName = "Albany",        StateCode = "NY", FipsCode = "36001", JurisdictionType = JurisdictionType.City, IsActive = true };
        db.Jurisdictions.AddRange(buyer, seller);

        // NY is destination-based
        var profile = new StateTaxProfile { StateCode = "NY", StateName = "New York", IntrastateSourcingRule = SourcingRule.DestinationBased, UpdatedAt = DateTime.UtcNow.ToString("o") };
        db.StateTaxProfiles.Add(profile);
        await db.SaveChangesAsync();

        db.TaxRates.Add(MakeRate(buyer.Id,  "NYC Tax",    rate: 0.085m));
        db.TaxRates.Add(MakeRate(seller.Id, "Albany Tax", rate: 0.04m));
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        // Even with SellerJurisdictionId set, NY is destination-based → use buyer's rate
        var result = await calc.CalculateAsync(buyer.Id, 100m, 1,
            new TaxCalcOptions(SellerJurisdictionId: seller.Id));

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.TotalTaxAmount, Is.EqualTo(8.5m));
        Assert.That(result.JurisdictionName, Is.EqualTo("New York City"));
    }

    [Test]
    public async Task TaxCategoryId_Filter_LimitsToMatchingCategory()
    {
        var factory = CreateFactory();
        var (db, jur) = await SeedJurisdictionAsync(factory);

        var cat = new TaxCategory { Name = "Food", TopLevelType = "Goods", IsLeaf = true, SortOrder = 1 };
        db.TaxCategories.Add(cat);
        await db.SaveChangesAsync();

        db.TaxRates.Add(MakeRate(jur.Id, "Food Rate",    rate: 0.00m));
        db.TaxRates.Add(MakeRate(jur.Id, "General Rate", rate: 0.06m));
        var foodRate = db.TaxRates.Local.First(t => t.Name == "Food Rate");
        foodRate.TaxCategoryId = cat.Id;
        await db.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        var result = await calc.CalculateAsync(jur.Id, 100m, 1,
            new TaxCalcOptions(TaxCategoryId: cat.Id));

        Assert.That(result!.RateLines, Has.Count.EqualTo(1));
        Assert.That(result.RateLines[0].RateName, Is.EqualTo("Food Rate"));
    }
}
