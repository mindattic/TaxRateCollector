using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.UnitTests.ServiceTests;

[TestFixture]
public class EvidenceFileStoreTests
{
    private readonly List<string> createdFiles = [];

    [TearDown]
    public void Cleanup()
    {
        foreach (var f in createdFiles)
        {
            try { File.Delete(Path.Combine(SettingsService.EvidenceDirectory, f)); }
            catch { /* best-effort */ }
        }
        createdFiles.Clear();
    }

    private static EvidenceFileStore MakeStore() =>
        new(NullLogger<EvidenceFileStore>.Instance);

    private async Task<string> SaveAndTrack(EvidenceFileStore store, byte[] content, string mime, string url = "http://ex.gov/page")
    {
        var result = await store.SaveAsync(url, content, mime);
        createdFiles.Add(result.FileName);
        return result.FileName;
    }

    // ── ContentHash re-verification (re-hashing the on-disk file must match) ──

    [Test]
    public async Task ContentHash_MatchesOnDiskBytes_ForPdf()
    {
        var store = MakeStore();
        var pdfBytes = Encoding.UTF8.GetBytes("%PDF-1.4 sample bytes");
        var result = await store.SaveAsync("http://ex.gov/doc", pdfBytes, "application/pdf");
        createdFiles.Add(result.FileName);

        var path = Path.Combine(SettingsService.EvidenceDirectory, result.FileName);
        var rehash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

        Assert.That(rehash, Is.EqualTo(result.ContentHash));
        Assert.That(result.ContentHash, Has.Length.EqualTo(64));
    }

    [Test]
    public async Task ContentHash_MatchesOnDiskBytes_ForHtml()
    {
        // HTML is wrapped/stripped before saving — the hash must reflect the
        // wrapped bytes (what's on disk), not the raw fetched HTML.
        var store = MakeStore();
        var html = "<html><head><title>State Tax</title></head><body>Rate is 6.25%</body></html>";
        var result = await store.SaveAsync("http://ex.gov/page",
            Encoding.UTF8.GetBytes(html), "text/html");
        createdFiles.Add(result.FileName);

        var path = Path.Combine(SettingsService.EvidenceDirectory, result.FileName);
        var rehash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

        Assert.That(rehash, Is.EqualTo(result.ContentHash));
    }

    // ── MIME classification ───────────────────────────────────────────────────

    [Test]
    public async Task Pdf_ReturnsEvidenceType_Pdf()
    {
        var store = MakeStore();
        var result = await store.SaveAsync("http://ex.gov/doc", [1, 2, 3], "application/pdf");
        createdFiles.Add(result.FileName);
        Assert.That(result.EvidenceType, Is.EqualTo("pdf"));
    }

    [Test]
    public async Task TextCsv_ReturnsEvidenceType_Csv()
    {
        var store = MakeStore();
        var result = await store.SaveAsync("http://ex.gov/doc", Encoding.UTF8.GetBytes("a,b\n1,2"), "text/csv");
        createdFiles.Add(result.FileName);
        Assert.That(result.EvidenceType, Is.EqualTo("csv"));
    }

    [Test]
    public async Task OpenXml_ReturnsEvidenceType_Xlsx()
    {
        var store = MakeStore();
        var result = await store.SaveAsync("http://ex.gov/doc", [0x50, 0x4B, 0x03, 0x04],
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        createdFiles.Add(result.FileName);
        Assert.That(result.EvidenceType, Is.EqualTo("xlsx"));
    }

    [Test]
    public async Task Html_ReturnsEvidenceType_Html()
    {
        // HTML evidence is saved as a single stripped .html file (the legacy
        // zip-bundling scheme was removed), so the discriminator is "html".
        var store = MakeStore();
        var result = await store.SaveAsync("http://ex.gov/page",
            Encoding.UTF8.GetBytes("<html><body>Hello</body></html>"), "text/html");
        createdFiles.Add(result.FileName);
        Assert.That(result.EvidenceType, Is.EqualTo("html"));
    }

    [Test]
    public async Task UnknownMime_ReturnsEvidenceType_Txt()
    {
        var store = MakeStore();
        var result = await store.SaveAsync("http://ex.gov/doc",
            Encoding.UTF8.GetBytes("raw data"), "application/octet-stream");
        createdFiles.Add(result.FileName);
        Assert.That(result.EvidenceType, Is.EqualTo("txt"));
    }

    // ── Stripped-HTML evidence file ─────────────────────────────────────────────

    [Test]
    public async Task Html_SavedAsSingleHtmlFile()
    {
        var store = MakeStore();
        var html = Encoding.UTF8.GetBytes("<html><body>Tax page</body></html>");
        var result = await store.SaveAsync("http://ex.gov/page", html, "text/html");
        createdFiles.Add(result.FileName);

        Assert.That(result.FileName, Does.EndWith(".html"));
        Assert.That(File.Exists(Path.Combine(SettingsService.EvidenceDirectory, result.FileName)), Is.True);
    }

    [Test]
    public async Task Html_SavedFile_ContainsOriginalContent()
    {
        var store = MakeStore();
        var html = Encoding.UTF8.GetBytes("<html><body>Tax rate: 6.25%</body></html>");
        var result = await store.SaveAsync("http://ex.gov/page", html, "text/html");
        createdFiles.Add(result.FileName);

        var fullPath = Path.Combine(SettingsService.EvidenceDirectory, result.FileName);
        var content = await File.ReadAllTextAsync(fullPath);
        Assert.That(content, Does.Contain("6.25%"));
    }

    // ── File naming ───────────────────────────────────────────────────────────

    [Test]
    public async Task FileName_MatchesExpectedPattern()
    {
        var store = MakeStore();
        var result = await store.SaveAsync("http://ex.gov/doc", [1, 2, 3], "application/pdf");
        createdFiles.Add(result.FileName);
        // No title slug available → "scraped_<12 hex of content hash>.<ext>".
        Assert.That(result.FileName, Does.Match(@"^scraped_[0-9a-f]{12}\.pdf$"));
    }

    // ── SizeBytes ─────────────────────────────────────────────────────────────

    [Test]
    public async Task SizeBytes_ReflectsActualFileSize()
    {
        var store = MakeStore();
        var data = Encoding.UTF8.GetBytes("some,csv,data\n1,2,3");
        var result = await store.SaveAsync("http://ex.gov/file", data, "text/csv");
        createdFiles.Add(result.FileName);

        var fullPath = Path.Combine(SettingsService.EvidenceDirectory, result.FileName);
        Assert.That(result.SizeBytes, Is.EqualTo(new FileInfo(fullPath).Length));
    }

    // ── Asset stripping ─────────────────────────────────────────────────────────

    [Test]
    public async Task Html_StripsLinkedAssetReferences()
    {
        // The stripper removes <link>/<script>/<style> noise nodes, so external
        // asset references do not survive into the saved evidence file (the old
        // full-page asset-bundling behavior was removed).
        var store = MakeStore();
        var html = Encoding.UTF8.GetBytes(
            "<html><head><link href=\"http://ex.gov/style.css\" rel=\"stylesheet\"></head><body>Tax</body></html>");

        var result = await store.SaveAsync("http://ex.gov/page", html, "text/html");
        createdFiles.Add(result.FileName);

        var content = await File.ReadAllTextAsync(Path.Combine(SettingsService.EvidenceDirectory, result.FileName));
        Assert.That(content, Does.Not.Contain("style.css"));
    }
}
