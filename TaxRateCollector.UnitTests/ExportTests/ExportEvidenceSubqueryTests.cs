using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.UnitTests.ExportTests;

/// <summary>
/// Tests for the evidence detection query in BuildExportData (Jurisdictions.razor).
///
/// The query was rewritten from:
///   allRateIds.Contains(d.TaxRateId) — hits SQLite's 999-variable limit at scale
/// to a correlated subquery:
///   db.TaxRates.Any(r => r.Id == d.TaxRateId &amp;&amp; r.IsCurrent)
///
/// These tests verify the new query produces correct results across all edge cases.
/// </summary>
[TestFixture]
public class ExportEvidenceSubqueryTests
{
    private static AppDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(opts);
    }

    /// <summary>
    /// The fixed query from BuildExportData.
    /// Mirrors the exact LINQ expression so tests stay in sync with production code.
    /// </summary>
    private static async Task<HashSet<int>> GetEvidencedRateIdsAsync(AppDbContext db)
        => await db.SourceDocuments
            .Where(d => d.IsActive && db.TaxRates.Any(r => r.Id == d.TaxRateId && r.IsCurrent))
            .Select(d => d.TaxRateId)
            .Distinct()
            .ToHashSetAsync();

    private static async Task<ScrapeRun> AddRunAsync(AppDbContext db)
    {
        var run = new ScrapeRun { StartedAt = DateTime.UtcNow.ToString("o"), Status = ScrapeStatus.Manual };
        db.ScrapeRuns.Add(run);
        await db.SaveChangesAsync();
        return run;
    }

    private static async Task<Jurisdiction> AddStateAsync(AppDbContext db, string stateCode = "")
    {
        var j = new Jurisdiction
        {
            JurisdictionType = JurisdictionType.State,
            JurisdictionName = "TestState_" + Guid.NewGuid().ToString("N")[..6],
            StateCode = string.IsNullOrEmpty(stateCode) ? Guid.NewGuid().ToString("N")[..2].ToUpper() : stateCode,
            FipsCode = Guid.NewGuid().ToString("N")[..8],
            IsActive = true,
        };
        db.Jurisdictions.Add(j);
        await db.SaveChangesAsync();
        return j;
    }

    private static async Task<TaxRate> AddRateAsync(AppDbContext db, int jurisdictionId, int runId,
        decimal rate = 0.06m, bool isCurrent = true)
    {
        var r = new TaxRate
        {
            JurisdictionId = jurisdictionId,
            Rate = rate,
            RateType = "General",
            EffectiveDate = "2024-01-01",
            ScrapedAt = DateTime.UtcNow.ToString("o"),
            ScrapeRunId = runId,
            IsCurrent = isCurrent
        };
        db.TaxRates.Add(r);
        await db.SaveChangesAsync();
        return r;
    }

    private static async Task AddDocAsync(AppDbContext db, int rateId, bool isActive = true)
    {
        db.SourceDocuments.Add(new SourceDocument
        {
            TaxRateId   = rateId,
            SourceType  = SourceType.Pdf,
            FileName    = $"{Guid.NewGuid():N}.pdf",
            FetchedAt   = DateTime.UtcNow.ToString("o"),
            ContentHash = Guid.NewGuid().ToString("N"),
            IsActive    = isActive
        });
        await db.SaveChangesAsync();
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Test]
    public async Task Query_CurrentRateWithActiveDoc_IsIncluded()
    {
        await using var db = CreateDb();
        var run  = await AddRunAsync(db);
        var j    = await AddStateAsync(db);
        var rate = await AddRateAsync(db, j.Id, run.Id, isCurrent: true);
        await AddDocAsync(db, rate.Id, isActive: true);

        var result = await GetEvidencedRateIdsAsync(db);

        Assert.That(result, Contains.Item(rate.Id));
    }

    [Test]
    public async Task Query_NoDocuments_ReturnsEmpty()
    {
        await using var db = CreateDb();
        var run  = await AddRunAsync(db);
        var j    = await AddStateAsync(db);
        await AddRateAsync(db, j.Id, run.Id, isCurrent: true);

        var result = await GetEvidencedRateIdsAsync(db);

        Assert.That(result, Is.Empty);
    }

    // ── Key correctness guarantee: the subquery filters on IsCurrent ──────────

    [Test]
    public async Task Query_RetiredRate_ExcludedEvenWithActiveDoc()
    {
        // This is the primary contract of the fix: evidence on a retired rate
        // must NOT surface in the export's "validated" column.
        await using var db = CreateDb();
        var run     = await AddRunAsync(db);
        var j       = await AddStateAsync(db);
        var retired = await AddRateAsync(db, j.Id, run.Id, isCurrent: false);
        await AddDocAsync(db, retired.Id, isActive: true);

        var result = await GetEvidencedRateIdsAsync(db);

        Assert.That(result, Does.Not.Contain(retired.Id),
            "Evidence on a non-current (retired) rate must not appear in the export.");
    }

    [Test]
    public async Task Query_RetiredAndCurrentBothHaveDocs_OnlyCurrentIncluded()
    {
        await using var db = CreateDb();
        var run     = await AddRunAsync(db);
        var j       = await AddStateAsync(db);
        var retired = await AddRateAsync(db, j.Id, run.Id, rate: 0.05m, isCurrent: false);
        var current = await AddRateAsync(db, j.Id, run.Id, rate: 0.06m, isCurrent: true);
        await AddDocAsync(db, retired.Id, isActive: true);
        await AddDocAsync(db, current.Id, isActive: true);

        var result = await GetEvidencedRateIdsAsync(db);

        Assert.That(result, Contains.Item(current.Id));
        Assert.That(result, Does.Not.Contain(retired.Id));
    }

    // ── IsActive filter on SourceDocument ─────────────────────────────────────

    [Test]
    public async Task Query_SoftDeletedDoc_IsExcluded()
    {
        await using var db = CreateDb();
        var run  = await AddRunAsync(db);
        var j    = await AddStateAsync(db);
        var rate = await AddRateAsync(db, j.Id, run.Id, isCurrent: true);
        await AddDocAsync(db, rate.Id, isActive: false);  // soft-deleted

        var result = await GetEvidencedRateIdsAsync(db);

        Assert.That(result, Does.Not.Contain(rate.Id),
            "Disassociated (IsActive=false) documents must not count as evidence.");
    }

    [Test]
    public async Task Query_MixedActiveAndInactiveDocs_OnlyActiveCountsAsEvidence()
    {
        await using var db = CreateDb();
        var run   = await AddRunAsync(db);
        var j1    = await AddStateAsync(db);
        var j2    = await AddStateAsync(db);
        var rate1 = await AddRateAsync(db, j1.Id, run.Id, isCurrent: true);
        var rate2 = await AddRateAsync(db, j2.Id, run.Id, isCurrent: true);

        await AddDocAsync(db, rate1.Id, isActive: true);   // j1 has valid evidence
        await AddDocAsync(db, rate2.Id, isActive: false);  // j2's doc was disassociated

        var result = await GetEvidencedRateIdsAsync(db);

        Assert.That(result, Contains.Item(rate1.Id));
        Assert.That(result, Does.Not.Contain(rate2.Id));
    }

    // ── Multi-jurisdiction scenarios ──────────────────────────────────────────

    [Test]
    public async Task Query_MultipleJurisdictions_OnlyEvidencedFlagged()
    {
        await using var db = CreateDb();
        var run   = await AddRunAsync(db);
        var j1    = await AddStateAsync(db);
        var j2    = await AddStateAsync(db);
        var j3    = await AddStateAsync(db);
        var rate1 = await AddRateAsync(db, j1.Id, run.Id, isCurrent: true);
        var rate2 = await AddRateAsync(db, j2.Id, run.Id, isCurrent: true);
        var rate3 = await AddRateAsync(db, j3.Id, run.Id, isCurrent: true);

        await AddDocAsync(db, rate1.Id);   // evidenced
        await AddDocAsync(db, rate3.Id);   // evidenced
        // rate2 has no doc

        var result = await GetEvidencedRateIdsAsync(db);

        Assert.That(result, Contains.Item(rate1.Id));
        Assert.That(result, Does.Not.Contain(rate2.Id));
        Assert.That(result, Contains.Item(rate3.Id));
        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Query_MultipleDocsForOneRate_DeduplicatesRateId()
    {
        await using var db = CreateDb();
        var run  = await AddRunAsync(db);
        var j    = await AddStateAsync(db);
        var rate = await AddRateAsync(db, j.Id, run.Id, isCurrent: true);

        // Two different documents for the same rate
        await AddDocAsync(db, rate.Id);
        await AddDocAsync(db, rate.Id);

        var result = await GetEvidencedRateIdsAsync(db);

        Assert.That(result, Contains.Item(rate.Id));
        Assert.That(result, Has.Count.EqualTo(1), "Distinct() must deduplicate when multiple docs share a rate.");
    }

    [Test]
    public async Task Query_EmptyDatabase_ReturnsEmpty()
    {
        await using var db = CreateDb();

        var result = await GetEvidencedRateIdsAsync(db);

        Assert.That(result, Is.Empty);
    }
}
