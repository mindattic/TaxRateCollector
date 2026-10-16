using System.Net;
using TaxRateCollector.Infrastructure.Scrapers;
using TaxRateCollector.UnitTests.Helpers;

namespace TaxRateCollector.UnitTests.ScraperTests;

/// <summary>
/// Unit tests for ScraperHttpHelper, the fallback ladder every bulk state scraper depends
/// on: direct -> browser-UA -> browser-heavy -> Wayback -> archive.today -> Jina Reader.
/// Direct / browser-UA / browser-heavy all target the same URL, so tiers are told apart by
/// their headers (see Identify); Wayback / archive.today / Jina each hit a distinct URL.
/// </summary>
[TestFixture]
public class ScraperHttpHelperTests
{
    private const string TargetUrl = "https://revenue.example.gov/rates";
    private const string RequiredContent = "Sales Tax Rate";
    private const string WaybackSnapshotUrl =
        "https://web.archive.org/web/20240101000000/https://revenue.example.gov/rates";

    private const string WaybackAvailableJsonTemplate = """
        {"archived_snapshots":{"closest":{"available":true,"url":"__URL__","timestamp":"20240101000000","status":"200"}}}
        """;

    private const string WaybackNoSnapshotJson = """{"archived_snapshots":{}}""";

    // ── Routing helpers ──────────────────────────────────────────────────────────

    private static string Identify(HttpRequestMessage req)
    {
        var url = req.RequestUri!.ToString();
        if (url == TargetUrl)
        {
            if (req.Headers.Contains("Sec-Ch-Ua")) return "browser-heavy";
            if (req.Headers.UserAgent.Count > 0) return "browser-ua";
            return "direct";
        }
        if (url.StartsWith("https://archive.org/wayback/available")) return "wayback-api";
        if (url.StartsWith("https://archive.ph/newest/")) return "archive-today";
        if (url.StartsWith("https://r.jina.ai/")) return "jina";
        return "wayback-snapshot";
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static HttpResponseMessage Fail() => new(HttpStatusCode.ServiceUnavailable);

    private static ScriptedHttpMessageHandler BuildHandler(
        Dictionary<string, Func<HttpResponseMessage>> tiers) =>
        new(req =>
        {
            var tag = Identify(req);
            var response = tiers.TryGetValue(tag, out var factory) ? factory() : Fail();
            return (tag, response);
        });

    // ── Fallback ladder order ────────────────────────────────────────────────────

    [Test]
    public async Task FallbackLadder_TriesEveryTierInOrder_WhenEachPriorTierFails()
    {
        var handler = BuildHandler(new()
        {
            ["direct"]        = () => Ok("<html>no match here</html>"),   // soft failure
            ["browser-ua"]    = Fail,
            ["browser-heavy"] = Fail,
            ["wayback-api"]   = () => Ok(WaybackNoSnapshotJson),
            ["archive-today"] = Fail,
            ["jina"]          = () => Ok($"<html>{RequiredContent}</html>"),
        });
        using var client = new HttpClient(handler);

        var (html, urlUsed) = await ScraperHttpHelper.GetRequiredStringAsync(
            client, TargetUrl, waybackFallback: true, CancellationToken.None, RequiredContent);

        Assert.That(html, Does.Contain(RequiredContent));
        Assert.That(urlUsed, Is.EqualTo($"https://r.jina.ai/{TargetUrl}"));
        Assert.That(handler.CallLog, Is.EqualTo(new[]
        {
            "direct", "browser-ua", "browser-heavy", "wayback-api", "archive-today", "jina",
        }));
    }

    [Test]
    public async Task WaybackAndArchiveToday_AreSkipped_WhenWaybackFallbackIsFalse()
    {
        var handler = BuildHandler(new()
        {
            ["direct"]        = Fail,
            ["browser-ua"]    = Fail,
            ["browser-heavy"] = Fail,
            ["jina"]          = () => Ok($"<html>{RequiredContent}</html>"),
        });
        using var client = new HttpClient(handler);

        var (_, urlUsed) = await ScraperHttpHelper.GetRequiredStringAsync(
            client, TargetUrl, waybackFallback: false, CancellationToken.None, RequiredContent);

        Assert.That(urlUsed, Is.EqualTo($"https://r.jina.ai/{TargetUrl}"));
        Assert.That(handler.CallLog, Is.EqualTo(new[] { "direct", "browser-ua", "browser-heavy", "jina" }));
        Assert.That(handler.CallLog, Does.Not.Contain("wayback-api"));
        Assert.That(handler.CallLog, Does.Not.Contain("archive-today"));
    }

    // ── Soft failure (200 but missing required content) ─────────────────────────

    [Test]
    public async Task SoftFailure_200WithoutRequiredContent_MovesToNextTier()
    {
        var handler = BuildHandler(new()
        {
            ["direct"]     = () => Ok("<html>Nothing relevant here</html>"),
            ["browser-ua"] = () => Ok($"<html>{RequiredContent}</html>"),
        });
        using var client = new HttpClient(handler);

        var (html, urlUsed) = await ScraperHttpHelper.GetRequiredStringAsync(
            client, TargetUrl, waybackFallback: false, CancellationToken.None, RequiredContent);

        Assert.That(html, Does.Contain(RequiredContent));
        Assert.That(urlUsed, Is.EqualTo(TargetUrl));
        Assert.That(handler.CallLog, Is.EqualTo(new[] { "direct", "browser-ua" }));
    }

    [Test]
    public async Task RequiredContentMatch_IsCaseInsensitive()
    {
        var handler = BuildHandler(new()
        {
            ["direct"] = () => Ok("<html>the sales tax rate is 5%</html>"),
        });
        using var client = new HttpClient(handler);

        var (_, urlUsed) = await ScraperHttpHelper.GetRequiredStringAsync(
            client, TargetUrl, waybackFallback: false, CancellationToken.None, RequiredContent);

        Assert.That(urlUsed, Is.EqualTo(TargetUrl));
        Assert.That(handler.CallLog, Is.EqualTo(new[] { "direct" }));
    }

    // ── UrlUsed per tier ─────────────────────────────────────────────────────────

    [Test]
    public async Task BrowserHeavy_Succeeds_Returns_OriginalUrlAsUrlUsed()
    {
        var handler = BuildHandler(new()
        {
            ["direct"]        = Fail,
            ["browser-ua"]    = Fail,
            ["browser-heavy"] = () => Ok($"<html>{RequiredContent}</html>"),
        });
        using var client = new HttpClient(handler);

        var (_, urlUsed) = await ScraperHttpHelper.GetRequiredStringAsync(
            client, TargetUrl, waybackFallback: false, CancellationToken.None, RequiredContent);

        Assert.That(urlUsed, Is.EqualTo(TargetUrl));
        Assert.That(handler.CallLog, Is.EqualTo(new[] { "direct", "browser-ua", "browser-heavy" }));
    }

    [Test]
    public async Task Wayback_Succeeds_Returns_SnapshotUrlAsUrlUsed()
    {
        var handler = BuildHandler(new()
        {
            ["direct"]           = Fail,
            ["browser-ua"]       = Fail,
            ["browser-heavy"]    = Fail,
            ["wayback-api"]      = () => Ok(WaybackAvailableJsonTemplate.Replace("__URL__", WaybackSnapshotUrl)),
            ["wayback-snapshot"] = () => Ok($"<html>{RequiredContent}</html>"),
        });
        using var client = new HttpClient(handler);

        var (html, urlUsed) = await ScraperHttpHelper.GetRequiredStringAsync(
            client, TargetUrl, waybackFallback: true, CancellationToken.None, RequiredContent);

        Assert.That(html, Does.Contain(RequiredContent));
        Assert.That(urlUsed, Is.EqualTo(WaybackSnapshotUrl));
        Assert.That(handler.CallLog, Is.EqualTo(new[]
        {
            "direct", "browser-ua", "browser-heavy", "wayback-api", "wayback-snapshot",
        }));
    }

    [Test]
    public async Task ArchiveToday_Succeeds_Returns_FinalRedirectedUrlAsUrlUsed()
    {
        const string finalUrl = "https://archive.ph/20240101000000/https://revenue.example.gov/rates";
        var handler = BuildHandler(new()
        {
            ["direct"]        = Fail,
            ["browser-ua"]    = Fail,
            ["browser-heavy"] = Fail,
            ["wayback-api"]   = Fail,
            ["archive-today"] = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"<html>{RequiredContent}</html>"),
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, finalUrl),
            },
        });
        using var client = new HttpClient(handler);

        var (_, urlUsed) = await ScraperHttpHelper.GetRequiredStringAsync(
            client, TargetUrl, waybackFallback: true, CancellationToken.None, RequiredContent);

        Assert.That(urlUsed, Is.EqualTo(finalUrl));
    }

    [Test]
    public async Task Jina_Succeeds_Returns_ProxyUrlAsUrlUsed()
    {
        var handler = BuildHandler(new()
        {
            ["direct"]        = Fail,
            ["browser-ua"]    = Fail,
            ["browser-heavy"] = Fail,
            ["wayback-api"]   = Fail,
            ["archive-today"] = Fail,
            ["jina"]          = () => Ok($"<html>{RequiredContent}</html>"),
        });
        using var client = new HttpClient(handler);

        var (_, urlUsed) = await ScraperHttpHelper.GetRequiredStringAsync(
            client, TargetUrl, waybackFallback: true, CancellationToken.None, RequiredContent);

        Assert.That(urlUsed, Is.EqualTo($"https://r.jina.ai/{TargetUrl}"));
    }

    // ── Wayback JSON edge cases ──────────────────────────────────────────────────

    [Test]
    public async Task Wayback_NoSnapshotAvailable_FallsThroughToArchiveToday()
    {
        var handler = BuildHandler(new()
        {
            ["direct"]        = Fail,
            ["browser-ua"]    = Fail,
            ["browser-heavy"] = Fail,
            ["wayback-api"]   = () => Ok(WaybackNoSnapshotJson),
            ["archive-today"] = () => Ok($"<html>{RequiredContent}</html>"),
        });
        using var client = new HttpClient(handler);

        var (html, urlUsed) = await ScraperHttpHelper.GetRequiredStringAsync(
            client, TargetUrl, waybackFallback: true, CancellationToken.None, RequiredContent);

        Assert.That(html, Does.Contain(RequiredContent));
        Assert.That(urlUsed, Is.EqualTo($"https://archive.ph/newest/{TargetUrl}"));
        Assert.That(handler.CallLog, Is.EqualTo(new[]
        {
            "direct", "browser-ua", "browser-heavy", "wayback-api", "archive-today",
        }));
        Assert.That(handler.CallLog, Does.Not.Contain("wayback-snapshot"));
    }

    [Test]
    public async Task Wayback_MalformedJson_FallsThroughToArchiveToday()
    {
        var handler = BuildHandler(new()
        {
            ["direct"]        = Fail,
            ["browser-ua"]    = Fail,
            ["browser-heavy"] = Fail,
            ["wayback-api"]   = () => Ok("{not valid json"),
            ["archive-today"] = () => Ok($"<html>{RequiredContent}</html>"),
        });
        using var client = new HttpClient(handler);

        var (html, urlUsed) = await ScraperHttpHelper.GetRequiredStringAsync(
            client, TargetUrl, waybackFallback: true, CancellationToken.None, RequiredContent);

        Assert.That(html, Does.Contain(RequiredContent));
        Assert.That(urlUsed, Is.EqualTo($"https://archive.ph/newest/{TargetUrl}"));
        Assert.That(handler.CallLog, Is.EqualTo(new[]
        {
            "direct", "browser-ua", "browser-heavy", "wayback-api", "archive-today",
        }));
    }

    // ── InvalidOperationException messages ──────────────────────────────────────

    [Test]
    public void AllTiersNetworkFail_Throws_UnreachableMessage()
    {
        var handler = BuildHandler(new()
        {
            ["direct"]        = Fail,
            ["browser-ua"]    = Fail,
            ["browser-heavy"] = Fail,
            ["wayback-api"]   = Fail,
            ["archive-today"] = Fail,
            ["jina"]          = Fail,
        });
        using var client = new HttpClient(handler);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(() =>
            ScraperHttpHelper.GetRequiredStringAsync(
                client, TargetUrl, waybackFallback: true, CancellationToken.None, RequiredContent));

        Assert.That(ex!.Message, Does.StartWith("URL unreachable across all strategies"));
        Assert.That(ex.Message, Does.Contain("direct + browser-ua + browser-heavy"));
        Assert.That(ex.Message, Does.Contain("Wayback + archive.today"));
        Assert.That(ex.Message, Does.Contain("Jina Reader"));
        Assert.That(ex.Message, Does.EndWith($": {TargetUrl}"));
    }

    [Test]
    public void AllTiersReturnWrongContent_Throws_RequiredContentMissingMessage()
    {
        var handler = BuildHandler(new()
        {
            ["direct"]        = () => Ok("<html>unrelated</html>"),
            ["browser-ua"]    = () => Ok("<html>unrelated</html>"),
            ["browser-heavy"] = () => Ok("<html>unrelated</html>"),
            ["wayback-api"]   = Fail,
            ["archive-today"] = Fail,
            ["jina"]          = Fail,
        });
        using var client = new HttpClient(handler);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(() =>
            ScraperHttpHelper.GetRequiredStringAsync(
                client, TargetUrl, waybackFallback: true, CancellationToken.None, RequiredContent));

        Assert.That(ex!.Message, Does.StartWith($"Required content ('{RequiredContent}') missing from every strategy's response"));
        Assert.That(ex.Message, Does.EndWith($": {TargetUrl}"));
    }

    // ── GetOptionalStringAsync ───────────────────────────────────────────────────

    [Test]
    public async Task GetOptionalStringAsync_ReturnsNull_WhenEveryTierFails_InsteadOfThrowing()
    {
        var handler = BuildHandler(new()
        {
            ["direct"]        = Fail,
            ["browser-ua"]    = Fail,
            ["browser-heavy"] = Fail,
            ["wayback-api"]   = Fail,
            ["archive-today"] = Fail,
            ["jina"]          = Fail,
        });
        using var client = new HttpClient(handler);

        var result = await ScraperHttpHelper.GetOptionalStringAsync(
            client, TargetUrl, waybackFallback: true, CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetOptionalStringAsync_ReturnsContent_WhenDirectSucceeds()
    {
        var handler = BuildHandler(new()
        {
            ["direct"] = () => Ok("<html>anything at all</html>"),
        });
        using var client = new HttpClient(handler);

        var result = await ScraperHttpHelper.GetOptionalStringAsync(
            client, TargetUrl, waybackFallback: false, CancellationToken.None);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Html, Does.Contain("anything at all"));
        Assert.That(result.Value.UrlUsed, Is.EqualTo(TargetUrl));
    }
}
