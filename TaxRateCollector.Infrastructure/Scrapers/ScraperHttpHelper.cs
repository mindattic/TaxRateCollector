using System.Text.Json;

namespace TaxRateCollector.Infrastructure.Scrapers;

/// <summary>
/// HTTP helpers shared by bulk state scrapers.
/// When a government URL returns a non-success status and waybackFallback is true,
/// the Wayback Machine availability API is queried for the most recent archived snapshot.
/// </summary>
internal static class ScraperHttpHelper
{
    /// <summary>
    /// Fetches a required URL. If unreachable and waybackFallback is enabled, tries the
    /// most recent Wayback Machine snapshot. Throws if all attempts fail.
    /// Returns the HTML and the URL actually used (may be an archive.org URL).
    /// If <paramref name="requiredContent"/> is non-null, a 200 response that does not contain
    /// that substring is treated as a content failure and Wayback fallback is attempted.
    /// </summary>
    internal static async Task<(string Html, string UrlUsed)> GetRequiredStringAsync(
        HttpClient http, string url, bool waybackFallback, CancellationToken ct,
        string? requiredContent = null)
    {
        try
        {
            var html = await http.GetStringAsync(url, ct);
            if (requiredContent == null || html.Contains(requiredContent, StringComparison.OrdinalIgnoreCase))
                return (html, url);
            // Content check failed — fall through to Wayback
        }
        catch (HttpRequestException) when (!waybackFallback)
        {
            throw;
        }
        catch (HttpRequestException) { }

        if (!waybackFallback)
            throw new InvalidOperationException(
                $"Required content ('{requiredContent}') missing and Wayback fallback disabled: {url}");

        var wayback = await TryWaybackAsync(http, url, ct);
        if (wayback is not null)
            return wayback.Value;

        throw new InvalidOperationException(
            $"URL unreachable and no Wayback Machine snapshot found: {url}");
    }

    /// <summary>
    /// Fetches an optional URL. Returns null if unreachable even after Wayback fallback.
    /// Never throws on network failure.
    /// </summary>
    internal static async Task<(string Html, string UrlUsed)?> GetOptionalStringAsync(
        HttpClient http, string url, bool waybackFallback, CancellationToken ct)
    {
        try
        {
            return (await http.GetStringAsync(url, ct), url);
        }
        catch (HttpRequestException) { }

        if (!waybackFallback)
            return null;

        return await TryWaybackAsync(http, url, ct);
    }

    private static async Task<(string Html, string UrlUsed)?> TryWaybackAsync(
        HttpClient http, string url, CancellationToken ct)
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
                return (await http.GetStringAsync(archiveUrl, ct), archiveUrl);
            }
        }
        catch { }
        return null;
    }
}
