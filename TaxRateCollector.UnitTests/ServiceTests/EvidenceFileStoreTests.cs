using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TaxRateCollector.Infrastructure.Services;
using TaxRateCollector.UnitTests.Helpers;

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

    private EvidenceFileStore MakeStore(FakeHttpMessageHandler? handler = null)
    {
        var http = new HttpClient(handler ?? new FakeHttpMessageHandler());
        return new EvidenceFileStore(new SettingsService(), http, NullLogger<EvidenceFileStore>.Instance);
    }

    private async Task<string> SaveAndTrack(EvidenceFileStore store, byte[] content, string mime, string url = "http://ex.gov/page")
    {
        var result = await store.SaveAsync(url, content, mime);
        createdFiles.Add(result.FileName);
        return result.FileName;
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
    public async Task Html_ReturnsEvidenceType_Zip()
    {
        var store = MakeStore();
        var result = await store.SaveAsync("http://ex.gov/page",
            Encoding.UTF8.GetBytes("<html><body>Hello</body></html>"), "text/html");
        createdFiles.Add(result.FileName);
        Assert.That(result.EvidenceType, Is.EqualTo("zip"));
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

    // ── Simple zip contents ───────────────────────────────────────────────────

    [Test]
    public async Task SimpleHtmlZip_ContainsIndexHtml()
    {
        var store = MakeStore();
        var html = Encoding.UTF8.GetBytes("<html><body>Tax page</body></html>");
        var result = await store.SaveAsync("http://ex.gov/page", html, "text/html");
        createdFiles.Add(result.FileName);

        var fullPath = Path.Combine(SettingsService.EvidenceDirectory, result.FileName);
        using var zip = ZipFile.OpenRead(fullPath);
        Assert.That(zip.GetEntry("index.html"), Is.Not.Null);
    }

    [Test]
    public async Task SimpleHtmlZip_IndexHtml_ContainsOriginalContent()
    {
        var store = MakeStore();
        var html = Encoding.UTF8.GetBytes("<html><body>Tax rate: 6.25%</body></html>");
        var result = await store.SaveAsync("http://ex.gov/page", html, "text/html");
        createdFiles.Add(result.FileName);

        var fullPath = Path.Combine(SettingsService.EvidenceDirectory, result.FileName);
        using var zip = ZipFile.OpenRead(fullPath);
        using var sr = new StreamReader(zip.GetEntry("index.html")!.Open());
        var content = await sr.ReadToEndAsync();
        Assert.That(content, Does.Contain("6.25%"));
    }

    // ── File naming ───────────────────────────────────────────────────────────

    [Test]
    public async Task FileName_MatchesExpectedPattern()
    {
        var store = MakeStore();
        var result = await store.SaveAsync("http://ex.gov/doc", [1, 2, 3], "application/pdf");
        createdFiles.Add(result.FileName);
        Assert.That(result.FileName, Does.Match(@"^scraped_\d{14}_[0-9a-f]{8}\.pdf$"));
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

    // ── Full-page zip ─────────────────────────────────────────────────────────

    [Test]
    public async Task FullPageZip_BundlesLinkedAssets()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register("http://ex.gov/style.css", Encoding.UTF8.GetBytes("body{}"), "text/css");

        var settings = new SettingsService();
        settings.Current.FullPageCapture = true;

        var store = new EvidenceFileStore(settings,
            new HttpClient(handler),
            NullLogger<EvidenceFileStore>.Instance);

        var html = Encoding.UTF8.GetBytes(
            "<html><head><link href=\"http://ex.gov/style.css\" rel=\"stylesheet\"></head><body>Tax</body></html>");

        var result = await store.SaveAsync("http://ex.gov/page", html, "text/html");
        createdFiles.Add(result.FileName);

        var fullPath = Path.Combine(SettingsService.EvidenceDirectory, result.FileName);
        using var zip = ZipFile.OpenRead(fullPath);
        var hasAsset = zip.Entries.Any(e => e.FullName.Contains("style.css"));
        Assert.That(hasAsset, Is.True);
    }
}
