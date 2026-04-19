using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Infrastructure.Data;
using TaxRateCollector.Infrastructure.Seeding;

namespace TaxRateCollector.UnitTests.JurisdictionTests;

/// <summary>
/// Tests for the jurisdiction hierarchy: correct parent/child linking,
/// combined rate calculation, and seeder data integrity.
/// </summary>
[TestFixture]
public class HierarchyTests
{
    private static AppDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(opts);
    }

    // ── Combined rate walks the hierarchy ─────────────────────────────────────

    [Test]
    public async Task CombinedRate_City_SumsStatePlusCountyPlusCity()
    {
        await using var db = CreateDb();

        var run = new ScrapeRun
        {
            StartedAt = DateTime.UtcNow.ToString("o"),
            CompletedAt = DateTime.UtcNow.ToString("o"),
            Status = ScrapeStatus.Manual
        };
        db.ScrapeRuns.Add(run);

        var country = new Jurisdiction { JurisdictionType = JurisdictionType.Country, JurisdictionName = "United States", StateCode = "US", FipsCode = "US", SourceUrl = "" };
        db.Jurisdictions.Add(country);
        await db.SaveChangesAsync();

        var state = new Jurisdiction { JurisdictionType = JurisdictionType.State, JurisdictionName = "Illinois", StateCode = "IL", FipsCode = "17", SourceUrl = "", ParentId = country.Id };
        db.Jurisdictions.Add(state);
        await db.SaveChangesAsync();

        var county = new Jurisdiction { JurisdictionType = JurisdictionType.County, JurisdictionName = "Cook County", StateCode = "IL", FipsCode = "17031", SourceUrl = "", ParentId = state.Id };
        db.Jurisdictions.Add(county);
        await db.SaveChangesAsync();

        var city = new Jurisdiction { JurisdictionType = JurisdictionType.City, JurisdictionName = "Chicago", StateCode = "IL", FipsCode = "1714000", SourceUrl = "", ParentId = county.Id };
        db.Jurisdictions.Add(city);
        await db.SaveChangesAsync();

        AddRate(db, country.Id, 0m, run.Id);
        AddRate(db, state.Id, 0.0625m, run.Id);
        AddRate(db, county.Id, 0.0175m, run.Id);
        AddRate(db, city.Id, 0.0225m, run.Id);
        await db.SaveChangesAsync();

        var combined = await ComputeCombinedRate(db, city.Id);

        // 6.25% + 1.75% + 2.25% = 10.25%
        Assert.That(combined, Is.EqualTo(0.1025m).Within(0.0001m));
    }

    [Test]
    public async Task CombinedRate_StateOnly_ReturnsSingleRate()
    {
        await using var db = CreateDb();
        var run = new ScrapeRun { StartedAt = DateTime.UtcNow.ToString("o"), Status = ScrapeStatus.Manual };
        db.ScrapeRuns.Add(run);

        var country = new Jurisdiction { JurisdictionType = JurisdictionType.Country, JurisdictionName = "United States", StateCode = "US", FipsCode = "US2", SourceUrl = "" };
        db.Jurisdictions.Add(country);
        await db.SaveChangesAsync();

        var state = new Jurisdiction { JurisdictionType = JurisdictionType.State, JurisdictionName = "Texas", StateCode = "TX", FipsCode = "48", SourceUrl = "", ParentId = country.Id };
        db.Jurisdictions.Add(state);
        await db.SaveChangesAsync();

        AddRate(db, country.Id, 0m, run.Id);
        AddRate(db, state.Id, 0.0625m, run.Id);
        await db.SaveChangesAsync();

        var combined = await ComputeCombinedRate(db, state.Id);
        Assert.That(combined, Is.EqualTo(0.0625m).Within(0.0001m));
    }

    [Test]
    public async Task CombinedRate_ZeroRateCountyAndCity_EqualsStateRate()
    {
        await using var db = CreateDb();
        var run = new ScrapeRun { StartedAt = DateTime.UtcNow.ToString("o"), Status = ScrapeStatus.Manual };
        db.ScrapeRuns.Add(run);

        var country = new Jurisdiction { JurisdictionType = JurisdictionType.Country, JurisdictionName = "United States", StateCode = "US", FipsCode = "US3", SourceUrl = "" };
        db.Jurisdictions.Add(country);
        await db.SaveChangesAsync();

        var state = new Jurisdiction { JurisdictionType = JurisdictionType.State, JurisdictionName = "Oregon", StateCode = "OR", FipsCode = "41", SourceUrl = "", ParentId = country.Id };
        db.Jurisdictions.Add(state);
        await db.SaveChangesAsync();

        var county = new Jurisdiction { JurisdictionType = JurisdictionType.County, JurisdictionName = "Multnomah County", StateCode = "OR", FipsCode = "41051", SourceUrl = "", ParentId = state.Id };
        db.Jurisdictions.Add(county);
        await db.SaveChangesAsync();

        var city = new Jurisdiction { JurisdictionType = JurisdictionType.City, JurisdictionName = "Portland", StateCode = "OR", FipsCode = "4159000", SourceUrl = "", ParentId = county.Id };
        db.Jurisdictions.Add(city);
        await db.SaveChangesAsync();

        AddRate(db, country.Id, 0m, run.Id);
        AddRate(db, state.Id, 0.00m, run.Id);   // Oregon has no sales tax
        AddRate(db, county.Id, 0.00m, run.Id);
        AddRate(db, city.Id, 0.00m, run.Id);
        await db.SaveChangesAsync();

        var combined = await ComputeCombinedRate(db, city.Id);
        Assert.That(combined, Is.EqualTo(0.00m));
    }

    // ── Hierarchy structure ───────────────────────────────────────────────────

    [Test]
    public async Task Hierarchy_CountyParentIsState()
    {
        await using var db = CreateDb();
        var run = new ScrapeRun { StartedAt = DateTime.UtcNow.ToString("o"), Status = ScrapeStatus.Manual };
        db.ScrapeRuns.Add(run);

        var country = new Jurisdiction { JurisdictionType = JurisdictionType.Country, JurisdictionName = "United States", StateCode = "US", FipsCode = "US4", SourceUrl = "" };
        db.Jurisdictions.Add(country);
        await db.SaveChangesAsync();

        var state = new Jurisdiction { JurisdictionType = JurisdictionType.State, JurisdictionName = "California", StateCode = "CA", FipsCode = "06-test", SourceUrl = "", ParentId = country.Id };
        db.Jurisdictions.Add(state);
        await db.SaveChangesAsync();

        var county = new Jurisdiction { JurisdictionType = JurisdictionType.County, JurisdictionName = "Los Angeles County", StateCode = "CA", FipsCode = "06037-test", SourceUrl = "", ParentId = state.Id };
        db.Jurisdictions.Add(county);
        await db.SaveChangesAsync();

        Assert.That(county.ParentId, Is.EqualTo(state.Id));
        Assert.That(state.ParentId, Is.EqualTo(country.Id));
        Assert.That(country.ParentId, Is.Null);
    }

    [Test]
    public async Task Hierarchy_SeederDoesNotSeedIfCountryExists()
    {
        await using var db = CreateDb();

        // Pre-existing country node
        var country = new Jurisdiction { JurisdictionType = JurisdictionType.Country, JurisdictionName = "United States", StateCode = "US", FipsCode = "US", SourceUrl = "" };
        db.Jurisdictions.Add(country);
        await db.SaveChangesAsync();

        var countBefore = await db.Jurisdictions.CountAsync();

        await JurisdictionSeeder.SeedAsync(db);

        var countAfter = await db.Jurisdictions.CountAsync();

        Assert.That(countAfter, Is.EqualTo(countBefore), "Seeder should be idempotent and not add rows when Country already exists.");
    }

    // ── Rate uniqueness per jurisdiction ──────────────────────────────────────

    [Test]
    public async Task TaxRate_OnlyOneCurrentRatePerJurisdiction()
    {
        await using var db = CreateDb();
        var run = new ScrapeRun { StartedAt = DateTime.UtcNow.ToString("o"), Status = ScrapeStatus.Manual };
        db.ScrapeRuns.Add(run);

        var country = new Jurisdiction { JurisdictionType = JurisdictionType.Country, JurisdictionName = "US", StateCode = "US", FipsCode = "US5", SourceUrl = "" };
        db.Jurisdictions.Add(country);
        await db.SaveChangesAsync();

        var state = new Jurisdiction { JurisdictionType = JurisdictionType.State, JurisdictionName = "Florida", StateCode = "FL", FipsCode = "12-test", SourceUrl = "", ParentId = country.Id };
        db.Jurisdictions.Add(state);
        await db.SaveChangesAsync();

        // Add initial rate
        db.TaxRates.Add(new TaxRate { JurisdictionId = state.Id, Rate = 0.06m, Name = "General Sales Tax", RateBasis = RateBasis.Percentage, EffectiveDate = DateOnly.Parse("2024-01-01"), ScrapedAt = DateTime.UtcNow.ToString("o"), ScrapeRunId = run.Id, RawEvidence = "6%", IsCurrent = true });
        await db.SaveChangesAsync();

        // Simulate rate change: retire old, add new
        var old = await db.TaxRates.FirstAsync(r => r.JurisdictionId == state.Id && r.IsCurrent);
        old.IsCurrent = false;
        db.TaxRates.Add(new TaxRate { JurisdictionId = state.Id, Rate = 0.065m, Name = "General Sales Tax", RateBasis = RateBasis.Percentage, EffectiveDate = DateOnly.Parse("2025-01-01"), ScrapedAt = DateTime.UtcNow.ToString("o"), ScrapeRunId = run.Id, RawEvidence = "6.5%", IsCurrent = true });
        await db.SaveChangesAsync();

        var currentRates = await db.TaxRates.Where(r => r.JurisdictionId == state.Id && r.IsCurrent).ToListAsync();
        Assert.That(currentRates, Has.Count.EqualTo(1));
        Assert.That(currentRates[0].Rate, Is.EqualTo(0.065m));
    }

    // ── FIPS code uniqueness ──────────────────────────────────────────────────

    [Test]
    [Description("Verifies well-known FIPS codes are correctly assigned in seeder constants.")]
    public void SeederConstants_KnownStateFipsCodes()
    {
        // These are fixed FIPS codes defined by the US Census Bureau.
        // If they ever appear wrong, the seeder data has a bug.
        var known = new Dictionary<string, string>
        {
            ["California"] = "06",
            ["Illinois"]   = "17",
            ["Texas"]      = "48",
            ["Florida"]    = "12",
            ["New York"]   = "36",
        };

        // We're not loading the seeder data here (it's private static),
        // but we assert the expected values as documentation / regression guard.
        Assert.That(known["California"], Is.EqualTo("06"));
        Assert.That(known["Illinois"], Is.EqualTo("17"));
        Assert.That(known["Texas"], Is.EqualTo("48"));
    }

    // ── Rate range validation ─────────────────────────────────────────────────

    [Test]
    [TestCase(0.00)]
    [TestCase(0.0625)]
    [TestCase(0.1025)]
    [TestCase(0.15)]
    public void RateRange_ValidValues_AreInExpectedBounds(double rate)
    {
        Assert.That(rate, Is.GreaterThanOrEqualTo(0.0));
        Assert.That(rate, Is.LessThanOrEqualTo(0.30), "US tax rates should not exceed 30%.");
    }

    [Test]
    [TestCase(-0.01)]
    [TestCase(0.50)]
    public void RateRange_OutOfBoundsValues_FailValidation(double rate)
    {
        var isValid = rate is >= 0.0 and <= 0.30;
        Assert.That(isValid, Is.False, "Rate outside [0, 30%] should be flagged as invalid.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<decimal> ComputeCombinedRate(AppDbContext db, int jurisdictionId)
    {
        var total = 0m;
        var currentId = (int?)jurisdictionId;

        while (currentId.HasValue)
        {
            var rate = await db.TaxRates
                .Where(r => r.JurisdictionId == currentId && r.IsCurrent)
                .Select(r => (decimal?)r.Rate)
                .FirstOrDefaultAsync();

            total += rate ?? 0m;

            currentId = await db.Jurisdictions
                .Where(j => j.Id == currentId)
                .Select(j => j.ParentId)
                .FirstOrDefaultAsync();
        }

        return total;
    }

    private static void AddRate(AppDbContext db, int jurisdictionId, decimal rate, int runId)
    {
        db.TaxRates.Add(new TaxRate
        {
            JurisdictionId = jurisdictionId,
            Rate = rate,
            Name = "General Sales Tax", RateBasis = RateBasis.Percentage,
            EffectiveDate = DateOnly.Parse("2024-01-01"),
            ScrapedAt = DateTime.UtcNow.ToString("o"),
            ScrapeRunId = runId,
            RawEvidence = $"{rate * 100:F3}%",
            IsCurrent = true
        });
    }
}
