using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Data;
using TaxRateCollector.Infrastructure.Scrapers;

namespace TaxRateCollector.UnitTests.ServiceTests;

[TestFixture]
public class ScrapeOrchestratorTests
{
    // ── Factory ───────────────────────────────────────────────────────────────

    private sealed class Factory(DbContextOptions<AppDbContext> opts) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(opts);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(new AppDbContext(opts));
    }

    private static Factory MakeFactory()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new Factory(opts);
    }

    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class MatchingStrategy(string stateCode) : IScrapeStrategy
    {
        public string StrategyKey => $"{stateCode}-TEST";
        public bool CanHandle(Jurisdiction j) => j.StateCode == stateCode;
        public Task<IReadOnlyList<RawScrapeResult>> ScrapeAsync(Jurisdiction j, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RawScrapeResult>>([
                new RawScrapeResult("6.25%", 0.0625m, "General", j.JurisdictionName, 0.95f)]);
    }

    private sealed class NullStrategy : IScrapeStrategy
    {
        public string StrategyKey => "NULL";
        public bool CanHandle(Jurisdiction j) => false;
        public Task<IReadOnlyList<RawScrapeResult>> ScrapeAsync(Jurisdiction j, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RawScrapeResult>>([]);
    }

    private sealed class FakeBulkScraper(string stateCode, IReadOnlyList<BulkRateResult> results) : IStateBulkScraper
    {
        public string StateCode => stateCode;
        public Task<IReadOnlyList<BulkRateResult>> ScrapeAsync(CancellationToken ct = default)
            => Task.FromResult(results);
    }

    private sealed class NullEvidenceFileStore : IEvidenceFileStore
    {
        public Task<StoredEvidenceFile> SaveAsync(string sourceUrl, byte[] content, string mimeType, CancellationToken ct = default)
            => Task.FromResult(new StoredEvidenceFile("evidence.txt", "txt", content.Length,
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant()));
    }

    private sealed class NullDiffEngine : IDiffEngine
    {
        public Task<DiffReport> DetectChangesAsync(int scrapeRunId, CancellationToken ct = default)
            => Task.FromResult(new DiffReport(0, Array.Empty<RateChange>()));
    }

    private static ScrapeOrchestrator MakeOrchestrator(
        Factory factory,
        IScrapeStrategy[]? strategies = null,
        IStateBulkScraper[]? bulkScrapers = null)
        => new(
            factory,
            strategies ?? [new NullStrategy()],
            bulkScrapers ?? [],
            new NullEvidenceFileStore(),
            new NullDiffEngine(),
            NullLogger<ScrapeOrchestrator>.Instance);

    private static async Task<Jurisdiction> SeedJurisdiction(
        AppDbContext db,
        string stateCode,
        JurisdictionType type = JurisdictionType.County,
        bool active = true)
    {
        var j = new Jurisdiction
        {
            JurisdictionName = $"{stateCode} Test",
            FipsCode = "01001",
            StateCode = stateCode,
            JurisdictionType = type,
            IsActive = active,
            SourceUrl = $"http://tax.{stateCode.ToLower()}.gov/rates",
        };
        db.Jurisdictions.Add(j);
        await db.SaveChangesAsync();
        return j;
    }

    // ── Tests: RunFullScrapeAsync ─────────────────────────────────────────────

    [Test]
    public async Task RunFullScrapeAsync_CallsStrategy_ForMatchingJurisdiction()
    {
        var factory = MakeFactory();
        await using var db = factory.CreateDbContext();
        await SeedJurisdiction(db, "TX");

        var orchestrator = MakeOrchestrator(factory, strategies: [new MatchingStrategy("TX")]);
        await orchestrator.RunFullScrapeAsync();

        await using var verify = factory.CreateDbContext();
        Assert.That(await verify.TaxRates.AnyAsync(), Is.True);
    }

    [Test]
    public async Task RunFullScrapeAsync_IgnoresInactiveJurisdictions()
    {
        var factory = MakeFactory();
        await using var db = factory.CreateDbContext();
        await SeedJurisdiction(db, "TX", active: false);

        var orchestrator = MakeOrchestrator(factory, strategies: [new MatchingStrategy("TX")]);
        await orchestrator.RunFullScrapeAsync();

        await using var verify = factory.CreateDbContext();
        Assert.That(await verify.TaxRates.AnyAsync(), Is.False);
    }

    [Test]
    public async Task RunFullScrapeAsync_SetsScrapeRunStatus_Completed()
    {
        var factory = MakeFactory();
        await using var db = factory.CreateDbContext();
        await SeedJurisdiction(db, "TX");

        var orchestrator = MakeOrchestrator(factory, strategies: [new MatchingStrategy("TX")]);
        await orchestrator.RunFullScrapeAsync();

        await using var verify = factory.CreateDbContext();
        var run = await verify.ScrapeRuns.OrderBy(r => r.Id).LastAsync();
        Assert.That(run.Status, Is.EqualTo(ScrapeStatus.Completed));
    }

    [Test]
    public async Task RunFullScrapeAsync_SetsScrapeRunStatus_Paused_OnCancellation()
    {
        var factory = MakeFactory();
        await using var db = factory.CreateDbContext();
        db.Jurisdictions.Add(new Jurisdiction { JurisdictionName = "TX1", FipsCode = "00001", StateCode = "TX", JurisdictionType = JurisdictionType.County, IsActive = true });
        await db.SaveChangesAsync();

        // Strategy cancels the token when first called — simulates user hitting Pause mid-run
        using var cts = new CancellationTokenSource();
        var cancellingStrategy = new CancelOnScrapeStrategy("TX", cts);

        var orchestrator = MakeOrchestrator(factory, strategies: [cancellingStrategy]);
        await orchestrator.RunFullScrapeAsync(cts.Token);

        await using var verify = factory.CreateDbContext();
        var run = await verify.ScrapeRuns.OrderBy(r => r.Id).LastAsync();
        Assert.That(run.Status, Is.EqualTo(ScrapeStatus.Paused));
    }

    private sealed class CancelOnScrapeStrategy(string stateCode, CancellationTokenSource cts) : IScrapeStrategy
    {
        public string StrategyKey => $"{stateCode}-CANCEL";
        public bool CanHandle(Jurisdiction j) => j.StateCode == stateCode;
        public Task<IReadOnlyList<RawScrapeResult>> ScrapeAsync(Jurisdiction j, CancellationToken ct = default)
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<RawScrapeResult>>([]);
        }
    }

    [Test]
    public async Task RunFullScrapeAsync_SetsScrapeRunStatus_Failed_WhenAllJurisdictionsFail()
    {
        var factory = MakeFactory();
        await using var db = factory.CreateDbContext();
        await SeedJurisdiction(db, "TX");

        var orchestrator = MakeOrchestrator(factory, strategies: [new ThrowingStrategy("TX")]);
        await orchestrator.RunFullScrapeAsync();

        await using var verify = factory.CreateDbContext();
        var run = await verify.ScrapeRuns.OrderBy(r => r.Id).LastAsync();
        Assert.That(run.Status, Is.EqualTo(ScrapeStatus.Failed));
    }

    [Test]
    public async Task RunStrategyAsync_MarksPreviousRates_AsNotCurrent()
    {
        var factory = MakeFactory();
        await using var db = factory.CreateDbContext();
        var j = await SeedJurisdiction(db, "TX");

        var run0 = new ScrapeRun { StartedAt = DateTime.UtcNow.AddDays(-1).ToString("o"), Status = ScrapeStatus.Completed };
        db.ScrapeRuns.Add(run0);
        await db.SaveChangesAsync();
        db.TaxRates.Add(new TaxRate
        {
            JurisdictionId = j.Id, ScrapeRunId = run0.Id,
            Name = "General Sales Tax", Rate = 0.06m, IsCurrent = true,
            ScrapedAt = DateTime.UtcNow.AddDays(-1).ToString("o"),
        });
        await db.SaveChangesAsync();

        var orchestrator = MakeOrchestrator(factory, strategies: [new MatchingStrategy("TX")]);
        await orchestrator.RunFullScrapeAsync();

        await using var verify = factory.CreateDbContext();
        var rates = await verify.TaxRates.ToListAsync();
        Assert.That(rates.Where(r => r.Rate == 0.06m).All(r => !r.IsCurrent), Is.True,
            "Previous rate should be marked not current after a new scrape");
        Assert.That(rates.Any(r => r.Rate == 0.0625m && r.IsCurrent), Is.True,
            "New rate from strategy should be IsCurrent = true");
    }

    [Test]
    public async Task BulkScraper_SkipsRate_WhenLiveRateAlreadyExists()
    {
        var factory = MakeFactory();
        await using var db = factory.CreateDbContext();

        var stateJ = new Jurisdiction { JurisdictionName = "California", FipsCode = "06", StateCode = "CA", JurisdictionType = JurisdictionType.State, IsActive = true };
        db.Jurisdictions.Add(stateJ);
        var countyJ = new Jurisdiction { JurisdictionName = "Los Angeles", FipsCode = "06037", StateCode = "CA", JurisdictionType = JurisdictionType.County, IsActive = true };
        db.Jurisdictions.Add(countyJ);
        var run0 = new ScrapeRun { StartedAt = DateTime.UtcNow.AddDays(-1).ToString("o"), Status = ScrapeStatus.Completed };
        db.ScrapeRuns.Add(run0);
        await db.SaveChangesAsync();

        db.TaxRates.Add(new TaxRate
        {
            JurisdictionId = countyJ.Id, ScrapeRunId = run0.Id,
            Name = "Sales Tax", Rate = 0.09m, IsCurrent = true,
            ScrapedAt = DateTime.UtcNow.AddDays(-1).ToString("o"),
        });
        await db.SaveChangesAsync();

        var bulk = new FakeBulkScraper("CA", [
            new BulkRateResult("06037", "Los Angeles", "Sales Tax", 0.095m,
                "http://cdtfa.ca.gov/rates.csv", [0x01], "text/csv", "rates.csv")]);
        var orchestrator = MakeOrchestrator(factory, bulkScrapers: [bulk]);
        await orchestrator.RunFullScrapeAsync();

        await using var verify = factory.CreateDbContext();
        var rates = await verify.TaxRates.Where(r => r.JurisdictionId == countyJ.Id).ToListAsync();
        Assert.That(rates, Has.Count.EqualTo(1), "Bulk result should be skipped when live rate exists with same name");
        Assert.That(rates[0].Rate, Is.EqualTo(0.09m));
    }

    private sealed class ThrowingStrategy(string stateCode) : IScrapeStrategy
    {
        public string StrategyKey => $"{stateCode}-THROW";
        public bool CanHandle(Jurisdiction j) => j.StateCode == stateCode;
        public Task<IReadOnlyList<RawScrapeResult>> ScrapeAsync(Jurisdiction j, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated scrape failure");
    }

    // ── Tests: GetPausedRunIdAsync ────────────────────────────────────────────

    [Test]
    public async Task GetPausedRunIdAsync_ReturnsNull_WhenNoPausedRun()
    {
        var factory = MakeFactory();
        var orchestrator = MakeOrchestrator(factory);
        Assert.That(await orchestrator.GetPausedRunIdAsync(), Is.Null);
    }

    [Test]
    public async Task GetPausedRunIdAsync_ReturnsId_OfMostRecentPausedRun()
    {
        var factory = MakeFactory();
        await using var db = factory.CreateDbContext();

        db.ScrapeRuns.Add(new ScrapeRun { StartedAt = DateTime.UtcNow.AddHours(-2).ToString("o"), Status = ScrapeStatus.Paused });
        var newerRun = new ScrapeRun { StartedAt = DateTime.UtcNow.AddHours(-1).ToString("o"), Status = ScrapeStatus.Paused };
        db.ScrapeRuns.Add(newerRun);
        db.ScrapeRuns.Add(new ScrapeRun { StartedAt = DateTime.UtcNow.ToString("o"), Status = ScrapeStatus.Completed });
        await db.SaveChangesAsync();

        var orchestrator = MakeOrchestrator(factory);
        var id = await orchestrator.GetPausedRunIdAsync();

        Assert.That(id, Is.EqualTo(newerRun.Id));
    }

    // ── Tests: ResumeAsync ────────────────────────────────────────────────────

    [Test]
    public async Task ResumeAsync_PicksUpAfterLastProcessedJurisdiction()
    {
        var factory = MakeFactory();
        await using var db = factory.CreateDbContext();

        var j1 = new Jurisdiction { JurisdictionName = "J1", FipsCode = "0001", StateCode = "TX", JurisdictionType = JurisdictionType.County, IsActive = true };
        var j2 = new Jurisdiction { JurisdictionName = "J2", FipsCode = "0002", StateCode = "TX", JurisdictionType = JurisdictionType.County, IsActive = true };
        db.Jurisdictions.AddRange(j1, j2);
        await db.SaveChangesAsync();

        // Simulate a paused run that processed j1
        var run = new ScrapeRun
        {
            StartedAt = DateTime.UtcNow.AddHours(-1).ToString("o"),
            Status = ScrapeStatus.Paused,
            LastProcessedJurisdictionId = j1.Id,
            ProcessedCount = 1,
        };
        db.ScrapeRuns.Add(run);
        await db.SaveChangesAsync();

        var orchestrator = MakeOrchestrator(factory, strategies: [new MatchingStrategy("TX")]);
        await orchestrator.ResumeAsync(run.Id);

        await using var verify = factory.CreateDbContext();
        // Only j2 should have rates (j1 was already processed before pause)
        var rates = await verify.TaxRates.ToListAsync();
        Assert.That(rates.All(r => r.JurisdictionId == j2.Id), Is.True);
    }

    [Test]
    public async Task ResumeAsync_Throws_WhenRunNotFound()
    {
        var factory = MakeFactory();
        var orchestrator = MakeOrchestrator(factory);
        Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.ResumeAsync(99999));
    }

    // ── Tests: bulk scraper routing ───────────────────────────────────────────

    [Test]
    public async Task BulkScraper_IsUsed_ForStateJurisdiction_MatchingStateCode()
    {
        var factory = MakeFactory();
        await using var db = factory.CreateDbContext();

        var stateJ = new Jurisdiction
        {
            JurisdictionName = "California",
            FipsCode = "06",
            StateCode = "CA",
            JurisdictionType = JurisdictionType.State,
            IsActive = true,
        };
        db.Jurisdictions.Add(stateJ);
        // Add a matching county so the bulk result can be matched by name
        var countyJ = new Jurisdiction
        {
            JurisdictionName = "Los Angeles",
            FipsCode = "06037",
            StateCode = "CA",
            JurisdictionType = JurisdictionType.County,
            IsActive = true,
        };
        db.Jurisdictions.Add(countyJ);
        await db.SaveChangesAsync();

        var bulkResult = new BulkRateResult(
            FipsCode: "06037",
            JurisdictionName: "Los Angeles",
            RateName: "Sales Tax",
            Rate: 0.095m,
            SourceUrl: "http://cdtfa.ca.gov/rates.csv",
            EvidenceBytes: [0x01],
            EvidenceMimeType: "text/csv",
            EvidenceOriginalFileName: "rates.csv");

        var bulk = new FakeBulkScraper("CA", [bulkResult]);
        var orchestrator = MakeOrchestrator(factory, bulkScrapers: [bulk]);
        await orchestrator.RunFullScrapeAsync();

        await using var verify = factory.CreateDbContext();
        var rates = await verify.TaxRates.ToListAsync();
        Assert.That(rates, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(rates.Any(r => r.JurisdictionId == countyJ.Id && r.Rate == 0.095m), Is.True);
    }
}
