using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.UnitTests.EvidenceTests;

/// <summary>
/// Tests for SourceDocument database transactions:
/// attach, disassociate (soft-delete), IsActive filtering,
/// and file association with TaxRate rows.
/// </summary>
[TestFixture]
public class EvidenceTransactionTests
{
    private static AppDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(opts);
    }

    private static async Task<(ScrapeRun run, Jurisdiction state, TaxRate rate)> SeedBasicAsync(AppDbContext db)
    {
        var run = new ScrapeRun { StartedAt = DateTime.UtcNow.ToString("o"), Status = ScrapeStatus.Manual };
        db.ScrapeRuns.Add(run);

        var country = new Jurisdiction { JurisdictionType = JurisdictionType.Country, JurisdictionName = "United States", StateCode = "US", FipsCode = "US-test", SourceUrl = "" };
        db.Jurisdictions.Add(country);
        await db.SaveChangesAsync();

        var state = new Jurisdiction { JurisdictionType = JurisdictionType.State, JurisdictionName = "Illinois", StateCode = "IL", FipsCode = "17-test", SourceUrl = "", ParentId = country.Id };
        db.Jurisdictions.Add(state);
        await db.SaveChangesAsync();

        var rate = new TaxRate { JurisdictionId = state.Id, Rate = 0.0625m, RateType = "General", EffectiveDate = "2024-01-01", ScrapedAt = DateTime.UtcNow.ToString("o"), ScrapeRunId = run.Id, RawValue = "6.250%", IsCurrent = true };
        db.TaxRates.Add(rate);
        await db.SaveChangesAsync();

        return (run, state, rate);
    }

    // ── Attach evidence ───────────────────────────────────────────────────────

    [Test]
    public async Task AttachEvidence_InsertsSourceDocument_WithCorrectFields()
    {
        await using var db = CreateDb();
        var (_, _, rate) = await SeedBasicAsync(db);

        var doc = new SourceDocument
        {
            TaxRateId = rate.Id,
            SourceType = SourceType.Pdf,
            FileName = "01AN4Z07BY79Y3ZT1ZQR3KYF.pdf",
            MimeType = "application/pdf",
            FetchedAt = DateTime.UtcNow.ToString("o"),
            ContentHash = "abc123",
            RawContent = string.Empty,
            IsActive = true
        };
        db.SourceDocuments.Add(doc);
        await db.SaveChangesAsync();

        var stored = await db.SourceDocuments.FindAsync(doc.Id);
        Assert.That(stored, Is.Not.Null);
        Assert.That(stored!.FileName, Is.EqualTo("01AN4Z07BY79Y3ZT1ZQR3KYF.pdf"));
        Assert.That(stored.IsActive, Is.True);
        Assert.That(stored.TaxRateId, Is.EqualTo(rate.Id));
    }

    [Test]
    public async Task AttachMultipleEvidence_AllLinkedToSameRate()
    {
        await using var db = CreateDb();
        var (_, _, rate) = await SeedBasicAsync(db);

        db.SourceDocuments.AddRange(
            new SourceDocument { TaxRateId = rate.Id, SourceType = SourceType.Pdf, FileName = "file1.pdf", MimeType = "application/pdf", FetchedAt = DateTime.UtcNow.ToString("o"), ContentHash = "h1", IsActive = true },
            new SourceDocument { TaxRateId = rate.Id, SourceType = SourceType.Csv, FileName = "file2.csv", MimeType = "text/csv", FetchedAt = DateTime.UtcNow.ToString("o"), ContentHash = "h2", IsActive = true },
            new SourceDocument { TaxRateId = rate.Id, SourceType = SourceType.Manual, FileName = "file3.txt", MimeType = "text/plain", FetchedAt = DateTime.UtcNow.ToString("o"), ContentHash = "h3", IsActive = true }
        );
        await db.SaveChangesAsync();

        var docs = await db.SourceDocuments.Where(d => d.TaxRateId == rate.Id && d.IsActive).ToListAsync();
        Assert.That(docs, Has.Count.EqualTo(3));
    }

    // ── Disassociate (soft delete) ────────────────────────────────────────────

    [Test]
    public async Task Disassociate_SetsIsActiveFalse_RecordPreserved()
    {
        await using var db = CreateDb();
        var (_, _, rate) = await SeedBasicAsync(db);

        var doc = new SourceDocument { TaxRateId = rate.Id, SourceType = SourceType.Pdf, FileName = "evidence.pdf", MimeType = "application/pdf", FetchedAt = DateTime.UtcNow.ToString("o"), ContentHash = "abc", IsActive = true };
        db.SourceDocuments.Add(doc);
        await db.SaveChangesAsync();
        var docId = doc.Id;

        // Disassociate
        doc.IsActive = false;
        await db.SaveChangesAsync();

        // Record still exists in DB
        var preserved = await db.SourceDocuments.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Id == docId);
        Assert.That(preserved, Is.Not.Null, "Row must be preserved after disassociation");
        Assert.That(preserved!.IsActive, Is.False);

        // Active query no longer returns it
        var active = await db.SourceDocuments.Where(d => d.TaxRateId == rate.Id && d.IsActive).ToListAsync();
        Assert.That(active, Is.Empty, "Disassociated doc should not appear in active query");
    }

    [Test]
    public async Task Disassociate_OneOfMany_OthersRemainActive()
    {
        await using var db = CreateDb();
        var (_, _, rate) = await SeedBasicAsync(db);

        var doc1 = new SourceDocument { TaxRateId = rate.Id, SourceType = SourceType.Pdf, FileName = "a.pdf", MimeType = "application/pdf", FetchedAt = DateTime.UtcNow.ToString("o"), ContentHash = "h1", IsActive = true };
        var doc2 = new SourceDocument { TaxRateId = rate.Id, SourceType = SourceType.Manual, FileName = "b.txt", MimeType = "text/plain", FetchedAt = DateTime.UtcNow.ToString("o"), ContentHash = "h2", IsActive = true };
        db.SourceDocuments.AddRange(doc1, doc2);
        await db.SaveChangesAsync();

        doc1.IsActive = false;
        await db.SaveChangesAsync();

        var active = await db.SourceDocuments.Where(d => d.TaxRateId == rate.Id && d.IsActive).ToListAsync();
        Assert.That(active, Has.Count.EqualTo(1));
        Assert.That(active[0].FileName, Is.EqualTo("b.txt"));
    }

    // ── TaxRate cascade ───────────────────────────────────────────────────────

    [Test]
    public async Task RateRetirement_NewRateHasNoEvidence_OldEvidenceLinkedToOldRate()
    {
        await using var db = CreateDb();
        var (run, state, rate) = await SeedBasicAsync(db);

        // Attach evidence to the original rate
        var doc = new SourceDocument { TaxRateId = rate.Id, SourceType = SourceType.Pdf, FileName = "original.pdf", MimeType = "application/pdf", FetchedAt = DateTime.UtcNow.ToString("o"), ContentHash = "h", IsActive = true };
        db.SourceDocuments.Add(doc);
        await db.SaveChangesAsync();

        // Retire old rate, create new rate
        rate.IsCurrent = false;
        var newRate = new TaxRate { JurisdictionId = state.Id, Rate = 0.065m, RateType = "General", EffectiveDate = "2025-01-01", ScrapedAt = DateTime.UtcNow.ToString("o"), ScrapeRunId = run.Id, RawValue = "6.500%", IsCurrent = true };
        db.TaxRates.Add(newRate);
        await db.SaveChangesAsync();

        // Old rate still has its evidence
        var oldEvidence = await db.SourceDocuments.Where(d => d.TaxRateId == rate.Id && d.IsActive).ToListAsync();
        Assert.That(oldEvidence, Has.Count.EqualTo(1));

        // New rate has no evidence yet
        var newEvidence = await db.SourceDocuments.Where(d => d.TaxRateId == newRate.Id && d.IsActive).ToListAsync();
        Assert.That(newEvidence, Is.Empty);
    }

    // ── FileName uniqueness ───────────────────────────────────────────────────

    [Test]
    public void FileName_GuidV7Format_IsUnique()
    {
        var names = Enumerable.Range(0, 100)
            .Select(_ => $"{Guid.CreateVersion7():N}.pdf")
            .ToHashSet();

        Assert.That(names, Has.Count.EqualTo(100), "All generated filenames should be unique");
    }

    [Test]
    public void FileName_GuidV7_HasExpectedLength()
    {
        var name = $"{Guid.CreateVersion7():N}.pdf";
        // 32 hex chars + ".pdf" = 36
        Assert.That(name, Has.Length.EqualTo(36));
        Assert.That(name, Does.EndWith(".pdf"));
        Assert.That(name[..32], Does.Match("^[0-9a-f]{32}$"));
    }

    // ── IsActive default ──────────────────────────────────────────────────────

    [Test]
    public void SourceDocument_IsActiveDefaultsTrue()
    {
        var doc = new SourceDocument();
        Assert.That(doc.IsActive, Is.True, "New SourceDocument should default to IsActive=true");
    }
}
