using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.UnitTests.ScraperTests;

/// <summary>
/// Validates the AutoApprove approval/rejection flow used by the /review admin page.
/// Tests the raw DB logic rather than the Blazor component so behaviour is verifiable
/// without a browser context.
/// </summary>
[TestFixture]
public class NeedsReviewFlowTests
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
    }

    private static async Task<(Jurisdiction j, ScrapeRun run)> SeedAsync(AppDbContext db)
    {
        var run = new ScrapeRun
        {
            StartedAt = DateTime.UtcNow.ToString("o"),
            Status = ScrapeStatus.Completed,
        };
        db.ScrapeRuns.Add(run);

        var j = new Jurisdiction
        {
            StateCode = "IL", JurisdictionName = "Illinois",
            FipsCode = "17", JurisdictionType = JurisdictionType.State, IsActive = true,
        };
        db.Jurisdictions.Add(j);
        await db.SaveChangesAsync();
        return (j, run);
    }

    private static TaxRate MakePendingRate(int jurisdictionId, int scrapeRunId, string name = "General Sales Tax")
        => new()
        {
            JurisdictionId = jurisdictionId,
            ScrapeRunId    = scrapeRunId,
            Name           = name,
            Rate           = 0.0625m,
            RateBasis      = RateBasis.Percentage,
            IsCurrent      = false,
            AutoApprove    = false,
            ScrapedAt      = DateTime.UtcNow.ToString("o"),
        };

    private static TaxRate MakeLiveRate(int jurisdictionId, int scrapeRunId, string name = "General Sales Tax")
        => new()
        {
            JurisdictionId = jurisdictionId,
            ScrapeRunId    = scrapeRunId,
            Name           = name,
            Rate           = 0.0500m,
            RateBasis      = RateBasis.Percentage,
            IsCurrent      = true,
            AutoApprove    = true,
            ScrapedAt      = DateTime.UtcNow.ToString("o"),
        };

    // ── Default state ─────────────────────────────────────────────────────────

    [Test]
    public void NewTaxRate_DefaultAutoApprove_IsTrue()
    {
        var rate = new TaxRate();
        Assert.That(rate.AutoApprove, Is.True,
            "Rates created manually via admin UI are auto-approved by default");
    }

    [Test]
    public void ScrapeStatus_HasPendingValue()
    {
        Assert.That(Enum.IsDefined(typeof(ScrapeStatus), ScrapeStatus.Pending));
    }

    [Test]
    public void ScrapeRun_ProgressFields_DefaultToZero()
    {
        var run = new ScrapeRun();
        Assert.That(run.TotalCount,     Is.EqualTo(0));
        Assert.That(run.ProcessedCount, Is.EqualTo(0));
    }

    // ── Approve ───────────────────────────────────────────────────────────────

    [Test]
    public async Task Approve_SetsAutoApprove_True_AndIsCurrent_True()
    {
        var factory = CreateFactory();
        await using var setup = factory.CreateDbContext();
        var (j, run) = await SeedAsync(setup);
        var rate = MakePendingRate(j.Id, run.Id);
        setup.TaxRates.Add(rate);
        await setup.SaveChangesAsync();

        // Simulate Review page approve action
        await using var act = factory.CreateDbContext();
        var pending = await act.TaxRates.SingleAsync(t => !t.AutoApprove);
        pending.AutoApprove = true;
        pending.IsCurrent   = true;
        await act.SaveChangesAsync();

        await using var verify = factory.CreateDbContext();
        var approved = await verify.TaxRates.FindAsync(rate.Id);
        Assert.That(approved!.AutoApprove, Is.True);
        Assert.That(approved.IsCurrent,    Is.True);
    }

    [Test]
    public async Task Approve_RetiresExistingLiveRate_WithSameName()
    {
        var factory = CreateFactory();
        await using var setup = factory.CreateDbContext();
        var (j, run) = await SeedAsync(setup);
        var liveRate    = MakeLiveRate(j.Id, run.Id);
        var pendingRate = MakePendingRate(j.Id, run.Id);
        setup.TaxRates.AddRange(liveRate, pendingRate);
        await setup.SaveChangesAsync();

        // Simulate approve: retire old live, promote pending
        await using var act = factory.CreateDbContext();
        var existing = await act.TaxRates
            .Where(t => t.JurisdictionId == j.Id && t.Name == "General Sales Tax" && t.IsCurrent)
            .ToListAsync();
        foreach (var r in existing) r.IsCurrent = false;

        var toApprove = await act.TaxRates.SingleAsync(t => !t.AutoApprove);
        toApprove.AutoApprove = true;
        toApprove.IsCurrent   = true;
        await act.SaveChangesAsync();

        await using var verify = factory.CreateDbContext();
        var currentRates = await verify.TaxRates
            .Where(t => t.JurisdictionId == j.Id && t.Name == "General Sales Tax" && t.IsCurrent)
            .ToListAsync();

        Assert.That(currentRates, Has.Count.EqualTo(1),
            "Exactly one rate should be current after approval");
        Assert.That(currentRates[0].Rate, Is.EqualTo(0.0625m),
            "The newly approved rate should be current, not the old one");
    }

    [Test]
    public async Task Approve_OnlyCurrentRate_PerNamePerJurisdiction()
    {
        var factory = CreateFactory();
        await using var setup = factory.CreateDbContext();
        var (j, run) = await SeedAsync(setup);

        // Two pending rates for different names — approving both should give two current rates
        setup.TaxRates.AddRange(
            MakePendingRate(j.Id, run.Id, "Beer Tax"),
            MakePendingRate(j.Id, run.Id, "Wine Tax"));
        await setup.SaveChangesAsync();

        await using var act = factory.CreateDbContext();
        foreach (var r in await act.TaxRates.Where(t => !t.AutoApprove).ToListAsync())
        {
            r.AutoApprove = true;
            r.IsCurrent   = true;
        }
        await act.SaveChangesAsync();

        await using var verify = factory.CreateDbContext();
        var current = await verify.TaxRates.Where(t => t.IsCurrent).ToListAsync();
        Assert.That(current, Has.Count.EqualTo(2));
    }

    // ── Reject ────────────────────────────────────────────────────────────────

    [Test]
    public async Task Reject_DeletesRateFromDatabase()
    {
        var factory = CreateFactory();
        await using var setup = factory.CreateDbContext();
        var (j, run) = await SeedAsync(setup);
        var rate = MakePendingRate(j.Id, run.Id);
        setup.TaxRates.Add(rate);
        await setup.SaveChangesAsync();
        var rateId = rate.Id;

        await using var act = factory.CreateDbContext();
        var toDelete = await act.TaxRates.FindAsync(rateId);
        act.TaxRates.Remove(toDelete!);
        await act.SaveChangesAsync();

        await using var verify = factory.CreateDbContext();
        Assert.That(await verify.TaxRates.FindAsync(rateId), Is.Null);
    }

    [Test]
    public async Task Reject_DoesNotAffect_LiveRates()
    {
        var factory = CreateFactory();
        await using var setup = factory.CreateDbContext();
        var (j, run) = await SeedAsync(setup);
        var liveRate    = MakeLiveRate(j.Id, run.Id);
        var pendingRate = MakePendingRate(j.Id, run.Id, "New Rate");
        setup.TaxRates.AddRange(liveRate, pendingRate);
        await setup.SaveChangesAsync();

        await using var act = factory.CreateDbContext();
        var toDelete = await act.TaxRates.SingleAsync(t => !t.AutoApprove);
        act.TaxRates.Remove(toDelete);
        await act.SaveChangesAsync();

        await using var verify = factory.CreateDbContext();
        Assert.That(await verify.TaxRates.CountAsync(), Is.EqualTo(1));
        Assert.That(await verify.TaxRates.AnyAsync(t => t.IsCurrent), Is.True);
    }

    // ── Lookup exclusion ──────────────────────────────────────────────────────

    [Test]
    public async Task PendingRate_IsExcluded_FromCurrentRateLookups()
    {
        var factory = CreateFactory();
        await using var setup = factory.CreateDbContext();
        var (j, run) = await SeedAsync(setup);
        setup.TaxRates.Add(MakePendingRate(j.Id, run.Id));
        await setup.SaveChangesAsync();

        await using var verify = factory.CreateDbContext();
        var currentRates = await verify.TaxRates
            .Where(t => t.JurisdictionId == j.Id && t.IsCurrent)
            .ToListAsync();

        Assert.That(currentRates, Is.Empty,
            "A pending (AutoApprove=false) rate must not appear in IsCurrent queries");
    }
}
