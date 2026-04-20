using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Infrastructure.Data;
using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.UnitTests.ServiceTests;

[TestFixture]
public class DiffEngineTests
{
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

    private static async Task<(Jurisdiction j, ScrapeRun run)> SeedAsync(
        AppDbContext db, int runId = 1)
    {
        var j = new Jurisdiction { JurisdictionName = "Test", FipsCode = "99", StateCode = "TX", JurisdictionType = JurisdictionType.County, IsActive = true };
        db.Jurisdictions.Add(j);
        var run = new ScrapeRun { StartedAt = DateTime.UtcNow.ToString("o"), Status = ScrapeStatus.Running };
        db.ScrapeRuns.Add(run);
        await db.SaveChangesAsync();
        return (j, run);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Test]
    public async Task NewRate_NoHistory_CreatesNewJurisdictionEntry()
    {
        var factory = MakeFactory();
        await using var db = factory.CreateDbContext();
        var (j, run) = await SeedAsync(db);

        db.TaxRates.Add(new TaxRate
        {
            JurisdictionId = j.Id, ScrapeRunId = run.Id,
            Name = "Sales Tax", Rate = 0.0625m, IsCurrent = true,
            ScrapedAt = DateTime.UtcNow.ToString("o"),
        });
        await db.SaveChangesAsync();

        var engine = new DiffEngine(factory);
        var report = await engine.DetectChangesAsync(run.Id);

        await using var verify = factory.CreateDbContext();
        var entry = await verify.ChangeLog.SingleAsync();
        Assert.That(entry.ChangeType, Is.EqualTo(ChangeType.NewJurisdiction));
        Assert.That(entry.NewRate, Is.EqualTo(0.0625m));
        Assert.That(entry.OldRate, Is.Null);
    }

    [Test]
    public async Task RateChanged_CreatesRateChangedEntry()
    {
        var factory = MakeFactory();
        await using var db = factory.CreateDbContext();
        var (j, run1) = await SeedAsync(db);

        db.TaxRates.Add(new TaxRate
        {
            JurisdictionId = j.Id, ScrapeRunId = run1.Id,
            Name = "Sales Tax", Rate = 0.0625m, IsCurrent = false,
            ScrapedAt = DateTime.UtcNow.AddDays(-1).ToString("o"),
        });

        var run2 = new ScrapeRun { StartedAt = DateTime.UtcNow.ToString("o"), Status = ScrapeStatus.Running };
        db.ScrapeRuns.Add(run2);
        await db.SaveChangesAsync();

        db.TaxRates.Add(new TaxRate
        {
            JurisdictionId = j.Id, ScrapeRunId = run2.Id,
            Name = "Sales Tax", Rate = 0.07m, IsCurrent = true,
            ScrapedAt = DateTime.UtcNow.ToString("o"),
        });
        await db.SaveChangesAsync();

        var engine = new DiffEngine(factory);
        await engine.DetectChangesAsync(run2.Id);

        await using var verify = factory.CreateDbContext();
        var entry = await verify.ChangeLog.SingleAsync();
        Assert.That(entry.ChangeType, Is.EqualTo(ChangeType.RateChanged));
        Assert.That(entry.OldRate, Is.EqualTo(0.0625m));
        Assert.That(entry.NewRate, Is.EqualTo(0.07m));
    }

    [Test]
    public async Task UnchangedRate_CreatesNoEntry()
    {
        var factory = MakeFactory();
        await using var db = factory.CreateDbContext();
        var (j, run1) = await SeedAsync(db);

        db.TaxRates.Add(new TaxRate
        {
            JurisdictionId = j.Id, ScrapeRunId = run1.Id,
            Name = "Sales Tax", Rate = 0.0625m, IsCurrent = false,
            ScrapedAt = DateTime.UtcNow.AddDays(-1).ToString("o"),
        });
        var run2 = new ScrapeRun { StartedAt = DateTime.UtcNow.ToString("o"), Status = ScrapeStatus.Running };
        db.ScrapeRuns.Add(run2);
        await db.SaveChangesAsync();

        db.TaxRates.Add(new TaxRate
        {
            JurisdictionId = j.Id, ScrapeRunId = run2.Id,
            Name = "Sales Tax", Rate = 0.0625m, IsCurrent = true,
            ScrapedAt = DateTime.UtcNow.ToString("o"),
        });
        await db.SaveChangesAsync();

        var engine = new DiffEngine(factory);
        await engine.DetectChangesAsync(run2.Id);

        await using var verify = factory.CreateDbContext();
        Assert.That(await verify.ChangeLog.AnyAsync(), Is.False);
    }

    [Test]
    public async Task AbsentJurisdiction_InCurrentRun_CreatesRemovedEntry()
    {
        var factory = MakeFactory();
        await using var db = factory.CreateDbContext();
        var (j, run1) = await SeedAsync(db);

        db.TaxRates.Add(new TaxRate
        {
            JurisdictionId = j.Id, ScrapeRunId = run1.Id,
            Name = "Sales Tax", Rate = 0.0625m, IsCurrent = false,
            ScrapedAt = DateTime.UtcNow.AddDays(-1).ToString("o"),
        });

        var j2 = new Jurisdiction { JurisdictionName = "Other", FipsCode = "98", StateCode = "TX", JurisdictionType = JurisdictionType.County, IsActive = true };
        db.Jurisdictions.Add(j2);
        var run2 = new ScrapeRun { StartedAt = DateTime.UtcNow.ToString("o"), Status = ScrapeStatus.Running };
        db.ScrapeRuns.Add(run2);
        await db.SaveChangesAsync();

        // Only j2 has a rate in run2; j is absent → Removed
        db.TaxRates.Add(new TaxRate
        {
            JurisdictionId = j2.Id, ScrapeRunId = run2.Id,
            Name = "Sales Tax", Rate = 0.05m, IsCurrent = true,
            ScrapedAt = DateTime.UtcNow.ToString("o"),
        });
        await db.SaveChangesAsync();

        var engine = new DiffEngine(factory);
        await engine.DetectChangesAsync(run2.Id);

        await using var verify = factory.CreateDbContext();
        var removed = await verify.ChangeLog
            .Where(c => c.ChangeType == ChangeType.Removed)
            .ToListAsync();
        Assert.That(removed, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(removed.All(r => r.JurisdictionId == j.Id), Is.True);
    }

    [Test]
    public async Task Report_TotalCompared_EqualsCurrentIsCurrent()
    {
        var factory = MakeFactory();
        await using var db = factory.CreateDbContext();
        var (j, run) = await SeedAsync(db);

        db.TaxRates.Add(new TaxRate { JurisdictionId = j.Id, ScrapeRunId = run.Id, Name = "A", Rate = 0.06m, IsCurrent = true, ScrapedAt = DateTime.UtcNow.ToString("o") });
        db.TaxRates.Add(new TaxRate { JurisdictionId = j.Id, ScrapeRunId = run.Id, Name = "B", Rate = 0.01m, IsCurrent = true, ScrapedAt = DateTime.UtcNow.ToString("o") });
        await db.SaveChangesAsync();

        var engine = new DiffEngine(factory);
        var report = await engine.DetectChangesAsync(run.Id);

        Assert.That(report.TotalCompared, Is.EqualTo(2));
    }

    [Test]
    public async Task Report_Changes_Count_MatchesLogEntries()
    {
        var factory = MakeFactory();
        await using var db = factory.CreateDbContext();
        var (j, run) = await SeedAsync(db);

        db.TaxRates.Add(new TaxRate { JurisdictionId = j.Id, ScrapeRunId = run.Id, Name = "Sales Tax", Rate = 0.06m, IsCurrent = true, ScrapedAt = DateTime.UtcNow.ToString("o") });
        await db.SaveChangesAsync();

        var engine = new DiffEngine(factory);
        var report = await engine.DetectChangesAsync(run.Id);

        Assert.That(report.Changes.Count, Is.EqualTo(report.Changes.Count));
        Assert.That(report.Changes, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Removed_JurisdictionInMultiplePreviousRuns_SingleRemovedEntry()
    {
        // Regression: Distinct() included t.Id so the same removed jurisdiction produced
        // one ChangeLog row per historical scrape run rather than exactly one.
        var factory = MakeFactory();
        await using var db = factory.CreateDbContext();
        var (j, run1) = await SeedAsync(db);

        // First previous run
        db.TaxRates.Add(new TaxRate
        {
            JurisdictionId = j.Id, ScrapeRunId = run1.Id,
            Name = "Sales Tax", Rate = 0.06m, IsCurrent = false,
            ScrapedAt = DateTime.UtcNow.AddDays(-2).ToString("o"),
        });

        // Second previous run — same jurisdiction, different scrape
        var run2 = new ScrapeRun { StartedAt = DateTime.UtcNow.AddDays(-1).ToString("o"), Status = ScrapeStatus.Completed };
        db.ScrapeRuns.Add(run2);
        await db.SaveChangesAsync();

        db.TaxRates.Add(new TaxRate
        {
            JurisdictionId = j.Id, ScrapeRunId = run2.Id,
            Name = "Sales Tax", Rate = 0.065m, IsCurrent = false,
            ScrapedAt = DateTime.UtcNow.AddDays(-1).ToString("o"),
        });

        // Current run — jurisdiction is absent
        var run3 = new ScrapeRun { StartedAt = DateTime.UtcNow.ToString("o"), Status = ScrapeStatus.Running };
        db.ScrapeRuns.Add(run3);
        var j2 = new Jurisdiction { JurisdictionName = "Other", FipsCode = "98", StateCode = "TX", JurisdictionType = JurisdictionType.County, IsActive = true };
        db.Jurisdictions.Add(j2);
        await db.SaveChangesAsync();

        db.TaxRates.Add(new TaxRate
        {
            JurisdictionId = j2.Id, ScrapeRunId = run3.Id,
            Name = "Sales Tax", Rate = 0.05m, IsCurrent = true,
            ScrapedAt = DateTime.UtcNow.ToString("o"),
        });
        await db.SaveChangesAsync();

        var engine = new DiffEngine(factory);
        await engine.DetectChangesAsync(run3.Id);

        await using var verify = factory.CreateDbContext();
        var removed = await verify.ChangeLog
            .Where(c => c.ChangeType == ChangeType.Removed && c.JurisdictionId == j.Id)
            .ToListAsync();

        Assert.That(removed, Has.Count.EqualTo(1),
            "A jurisdiction absent from the current run should produce exactly one Removed entry, " +
            "regardless of how many prior scrape runs it appeared in.");
    }
}
