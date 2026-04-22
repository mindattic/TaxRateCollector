using System.Text.Json;

namespace TaxRateCollector.Infrastructure.Scrapers;

/// <summary>
/// HTTP helpers shared by bulk state scrapers.
///
/// Web-app / SPA / WAF-protected government sites are fragile — a single fetch path is
/// rarely enough. The helper runs through a sequence of strategies, returning as soon as
/// one yields content (and, if requested, content that contains the required substring):
///
///   1. Direct fetch with the caller's existing User-Agent (typically the bot identifier).
///   2. Direct fetch with a desktop browser User-Agent — many state revenue sites
///      (e.g. revenue.iowa.gov sitting behind WAFs / CDNs) return 403 or stripped
///      content for unfamiliar UAs.
///   3. Direct fetch with browser UA + browser-like headers (Referer to the site root,
///      sec-ch-ua, sec-fetch-* headers) — bypasses stricter Akamai / Cloudflare rules.
///   4. Wayback Machine snapshot (if waybackFallback is enabled).
///   5. archive.today / archive.ph snapshot (if waybackFallback is enabled).
///   6. Jina AI Reader proxy (r.jina.ai) — fetches and renders JS-heavy SPA pages
///      server-side and returns clean text. Last-resort because it depends on a
///      third-party service.
///
/// Each tier honors the optional requiredContent substring check; an HTTP 200 whose body
/// lacks the expected content is treated as a soft failure and the next tier is tried.
/// </summary>
internal static class ScraperHttpHelper
{
    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    /// <summary>
    /// Fetches a required URL using a sequence of strategies. Throws if every tier fails.
    /// Returns the HTML and the URL actually used (may be an archive or proxy URL).
    /// </summary>
    internal static async Task<(string Html, string UrlUsed)> GetRequiredStringAsync(
        HttpClient http, string url, bool waybackFallback, CancellationToken ct,
        string? requiredContent = null)
    {
        var attempts = new List<FetchOutcome>();

        var direct = await TryDirectAsync(http, url, requiredContent, ct);
        attempts.Add(direct);
        if (direct.Html is not null) return (direct.Html, url);

        var browser = await TryBrowserUaAsync(http, url, requiredContent, ct);
        attempts.Add(browser);
        if (browser.Html is not null) return (browser.Html, url);

        var browserHeavy = await TryBrowserHeavyAsync(http, url, requiredContent, ct);
        attempts.Add(browserHeavy);
        if (browserHeavy.Html is not null) return (browserHeavy.Html, url);

        if (waybackFallback)
        {
            var wayback = await TryWaybackAsync(http, url, requiredContent, ct);
            if (wayback is not null) return wayback.Value;

            var archiveToday = await TryArchiveTodayAsync(http, url, requiredContent, ct);
            if (archiveToday is not null) return archiveToday.Value;
        }

        var jina = await TryJinaReaderAsync(http, url, requiredContent, ct);
        if (jina is not null) return jina.Value;

        throw new InvalidOperationException(
            $"{BuildFailureReason(attempts, waybackFallback, requiredContent)}: {url}");
    }

    /// <summary>
    /// Fetches an optional URL using the same strategy sequence. Returns null if every
    /// tier fails. Never throws on network failure.
    /// </summary>
    internal static async Task<(string Html, string UrlUsed)?> GetOptionalStringAsync(
        HttpClient http, string url, bool waybackFallback, CancellationToken ct)
    {
        var direct = await TryDirectAsync(http, url, requiredContent: null, ct);
        if (direct.Html is not null) return (direct.Html, url);

        var browser = await TryBrowserUaAsync(http, url, requiredContent: null, ct);
        if (browser.Html is not null) return (browser.Html, url);

        var browserHeavy = await TryBrowserHeavyAsync(http, url, requiredContent: null, ct);
        if (browserHeavy.Html is not null) return (browserHeavy.Html, url);

        if (waybackFallback)
        {
            var wayback = await TryWaybackAsync(http, url, requiredContent: null, ct);
            if (wayback is not null) return wayback;

            var archiveToday = await TryArchiveTodayAsync(http, url, requiredContent: null, ct);
            if (archiveToday is not null) return archiveToday;
        }

        return await TryJinaReaderAsync(http, url, requiredContent: null, ct);
    }

    // ── Strategy implementations ─────────────────────────────────────────────────

    private record struct FetchOutcome(string StrategyName, string? Html, bool NetworkFailed, bool ContentMissing);

    private static async Task<FetchOutcome> TryDirectAsync(
        HttpClient http, string url, string? requiredContent, CancellationToken ct)
    {
        const string name = "direct";
        try
        {
            var html = await http.GetStringAsync(url, ct);
            if (Matches(html, requiredContent))
                return new(name, html, false, false);
            return new(name, null, false, true);
        }
        catch (HttpRequestException) { return new(name, null, true, false); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return new(name, null, true, false); }
    }

    private static async Task<FetchOutcome> TryBrowserUaAsync(
        HttpClient http, string url, string? requiredContent, CancellationToken ct)
    {
        const string name = "browser-ua";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.Clear();
            req.Headers.UserAgent.ParseAdd(BrowserUserAgent);
            req.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            req.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

            using var res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return new(name, null, true, false);

            var html = await res.Content.ReadAsStringAsync(ct);
            if (Matches(html, requiredContent))
                return new(name, html, false, false);
            return new(name, null, false, true);
        }
        catch (HttpRequestException) { return new(name, null, true, false); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return new(name, null, true, false); }
    }

    private static async Task<FetchOutcome> TryBrowserHeavyAsync(
        HttpClient http, string url, string? requiredContent, CancellationToken ct)
    {
        const string name = "browser-heavy";
        try
        {
            var uri = new Uri(url);
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.Clear();
            req.Headers.UserAgent.ParseAdd(BrowserUserAgent);
            req.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
            req.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
            req.Headers.AcceptEncoding.ParseAdd("identity");
            req.Headers.Referrer = new Uri($"{uri.Scheme}://{uri.Host}/");
            req.Headers.TryAddWithoutValidation("Sec-Ch-Ua", "\"Chromium\";v=\"131\", \"Google Chrome\";v=\"131\", \"Not_A Brand\";v=\"24\"");
            req.Headers.TryAddWithoutValidation("Sec-Ch-Ua-Mobile", "?0");
            req.Headers.TryAddWithoutValidation("Sec-Ch-Ua-Platform", "\"Windows\"");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
            req.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");

            using var res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return new(name, null, true, false);

            var html = await res.Content.ReadAsStringAsync(ct);
            if (Matches(html, requiredContent))
                return new(name, html, false, false);
            return new(name, null, false, true);
        }
        catch (HttpRequestException) { return new(name, null, true, false); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return new(name, null, true, false); }
    }

    private static async Task<(string Html, string UrlUsed)?> TryWaybackAsync(
        HttpClient http, string url, string? requiredContent, CancellationToken ct)
    {
        try
        {
            var encoded   = Uri.EscapeDataString(url);
            var apiUrl    = $"https://archive.org/wayback/available?url={encoded}";
            var json      = await http.GetStringAsync(apiUrl, ct);
            using var doc = JsonDocument.Parse(json);
            var snapshots = doc.RootElement.GetProperty("archived_snapshots");
            if (snapshots.TryGetProperty("closest", out var closest) &&
                closest.TryGetProperty("available", out var avail) && avail.GetBoolean())
            {
                var archiveUrl = closest.GetProperty("url").GetString()!;
                var html = await http.GetStringAsync(archiveUrl, ct);
                if (Matches(html, requiredContent))
                    return (html, archiveUrl);
            }
        }
        catch { }
        return null;
    }

    private static async Task<(string Html, string UrlUsed)?> TryArchiveTodayAsync(
        HttpClient http, string url, string? requiredContent, CancellationToken ct)
    {
        try
        {
            // archive.ph/newest/<url> redirects to the most recent snapshot, if any.
            var probe = $"https://archive.ph/newest/{url}";
            using var req = new HttpRequestMessage(HttpMethod.Get, probe);
            req.Headers.UserAgent.Clear();
            req.Headers.UserAgent.ParseAdd(BrowserUserAgent);

            using var res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return null;

            var finalUrl = res.RequestMessage?.RequestUri?.ToString() ?? probe;
            var html     = await res.Content.ReadAsStringAsync(ct);
            if (Matches(html, requiredContent))
                return (html, finalUrl);
        }
        catch { }
        return null;
    }

    private static async Task<(string Html, string UrlUsed)?> TryJinaReaderAsync(
        HttpClient http, string url, string? requiredContent, CancellationToken ct)
    {
        try
        {
            // r.jina.ai renders JS-heavy SPA pages server-side and returns clean text.
            // Last-resort because it depends on an external service.
            var proxyUrl = $"https://r.jina.ai/{url}";
            using var req = new HttpRequestMessage(HttpMethod.Get, proxyUrl);
            req.Headers.UserAgent.Clear();
            req.Headers.UserAgent.ParseAdd("TaxRateCollector/1.0");
            req.Headers.Accept.ParseAdd("text/plain, text/html;q=0.9");

            using var res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return null;

            var text = await res.Content.ReadAsStringAsync(ct);
            if (Matches(text, requiredContent))
                return (text, proxyUrl);
        }
        catch { }
        return null;
    }

    private static bool Matches(string body, string? requiredContent) =>
        requiredContent is null ||
        body.Contains(requiredContent, StringComparison.OrdinalIgnoreCase);

    private static string BuildFailureReason(
        IReadOnlyList<FetchOutcome> attempts, bool waybackFallback, string? requiredContent)
    {
        var tried            = string.Join(" + ", attempts.Select(a => a.StrategyName));
        var allNetworkFailed = attempts.All(a => a.NetworkFailed);
        var anyContentMissed = attempts.Any(a => a.ContentMissing);
        var archivePart      = waybackFallback ? " + Wayback + archive.today" : "";
        var jinaPart         = " + Jina Reader";

        if (allNetworkFailed)
            return $"URL unreachable across all strategies ({tried}{archivePart}{jinaPart})";
        if (anyContentMissed && requiredContent != null)
            return $"Required content ('{requiredContent}') missing from every strategy's response ({tried}{archivePart}{jinaPart})";
        return $"All fetch strategies failed ({tried}{archivePart}{jinaPart})";
    }
}
