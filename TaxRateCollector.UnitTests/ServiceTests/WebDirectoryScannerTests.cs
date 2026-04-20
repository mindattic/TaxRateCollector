using TaxRateCollector.Infrastructure.Services;
using TaxRateCollector.UnitTests.Helpers;

namespace TaxRateCollector.UnitTests.ServiceTests;

[TestFixture]
public class WebDirectoryScannerTests
{
    private const string BaseUrl = "http://files.example.gov/taxdata/";

    private static WebDirectoryScanner MakeScanner(FakeHttpMessageHandler handler)
        => new(new FakeHttpClientFactory(handler));

    private static string ApacheListing(params string[] hrefs)
    {
        var links = string.Join("\n", hrefs.Select(h =>
            $"<a href=\"{h}\">{h}</a>  2025-01-15 10:00"));
        return $"<html><body>{links}</body></html>";
    }

    // ── ListAsync ─────────────────────────────────────────────────────────────

    [Test]
    public async Task ListAsync_ParsesFileEntries()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(BaseUrl, ApacheListing("rates_2025.csv", "rates_2024.csv"));

        var scanner = MakeScanner(handler);
        var entries = await scanner.ListAsync(BaseUrl);

        Assert.That(entries, Has.Count.EqualTo(2));
        Assert.That(entries[0].Name, Is.EqualTo("rates_2025.csv"));
        Assert.That(entries[0].IsDirectory, Is.False);
    }

    [Test]
    public async Task ListAsync_SkipsParentDirLink()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(BaseUrl, ApacheListing("../", "rates.csv"));

        var scanner = MakeScanner(handler);
        var entries = await scanner.ListAsync(BaseUrl);

        Assert.That(entries.All(e => e.Name != ".."), Is.True);
        Assert.That(entries, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ListAsync_SkipsOffSiteLinks()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(BaseUrl,
            "<html><body><a href=\"http://evil.com/steal.csv\">steal</a><a href=\"rates.csv\">r</a></body></html>");

        var scanner = MakeScanner(handler);
        var entries = await scanner.ListAsync(BaseUrl);

        Assert.That(entries.All(e => !e.Url.Contains("evil.com")), Is.True);
        Assert.That(entries, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ListAsync_MarksDirEntries_WhenHrefEndsWithSlash()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(BaseUrl, ApacheListing("archive/", "rates.csv"));

        var scanner = MakeScanner(handler);
        var entries = await scanner.ListAsync(BaseUrl);

        var dir = entries.Single(e => e.Name == "archive");
        var file = entries.Single(e => e.Name == "rates.csv");
        Assert.That(dir.IsDirectory, Is.True);
        Assert.That(file.IsDirectory, Is.False);
    }

    [Test]
    public async Task ListAsync_IncludesLastModified_WhenPresent()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(BaseUrl,
            "<html><body><a href=\"rates.csv\">rates.csv</a>  2025-09-10 14:22</body></html>");

        var scanner = MakeScanner(handler);
        var entries = await scanner.ListAsync(BaseUrl);

        Assert.That(entries[0].LastModified, Is.EqualTo("2025-09-10 14:22"));
    }

    // ── FindLatestUrlAsync ────────────────────────────────────────────────────

    [Test]
    public async Task FindLatestUrlAsync_ReturnsHighestSortedMatch()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(BaseUrl, ApacheListing("rates_2023.csv", "rates_2025.csv", "rates_2024.csv"));

        var scanner = MakeScanner(handler);
        var url = await scanner.FindLatestUrlAsync(BaseUrl, "rates_*.csv");

        Assert.That(url, Does.Contain("rates_2025.csv"));
    }

    [Test]
    public async Task FindLatestUrlAsync_ReturnsNull_WhenNoMatch()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(BaseUrl, ApacheListing("rates.xlsx", "report.pdf"));

        var scanner = MakeScanner(handler);
        var url = await scanner.FindLatestUrlAsync(BaseUrl, "*.csv");

        Assert.That(url, Is.Null);
    }

    [Test]
    public async Task FindLatestUrlAsync_GlobPattern_MatchesCsvNotOthers()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(BaseUrl, ApacheListing("data.csv", "data.xlsx", "readme.txt"));

        var scanner = MakeScanner(handler);
        var url = await scanner.FindLatestUrlAsync(BaseUrl, "*.csv");

        Assert.That(url, Does.Contain("data.csv"));
    }

    // ── FindAllAsync ──────────────────────────────────────────────────────────

    [Test]
    public async Task FindAllAsync_RecursesIntoSubdirectory()
    {
        var subUrl = BaseUrl + "archive/";
        var handler = new FakeHttpMessageHandler();
        handler.Register(BaseUrl, ApacheListing("archive/", "top.csv"));
        handler.Register(subUrl, ApacheListing("old.csv"));

        var scanner = MakeScanner(handler);
        var results = await scanner.FindAllAsync(BaseUrl, "*.csv", maxDepth: 2);

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results, Does.Contain(BaseUrl + "top.csv"));
        Assert.That(results, Does.Contain(subUrl + "old.csv"));
    }

    [Test]
    public async Task FindAllAsync_DoesNotExceedMaxDepth()
    {
        var depth1 = BaseUrl + "a/";
        var depth2 = depth1 + "b/";
        var handler = new FakeHttpMessageHandler();
        handler.Register(BaseUrl, ApacheListing("a/"));
        handler.Register(depth1, ApacheListing("b/", "level1.csv"));
        handler.Register(depth2, ApacheListing("level2.csv"));

        var scanner = MakeScanner(handler);
        // maxDepth:1 — should find level1.csv but NOT recurse into b/
        var results = await scanner.FindAllAsync(BaseUrl, "*.csv", maxDepth: 1);

        Assert.That(results, Does.Contain(depth1 + "level1.csv"));
        Assert.That(results, Does.Not.Contain(depth2 + "level2.csv"));
    }
}
