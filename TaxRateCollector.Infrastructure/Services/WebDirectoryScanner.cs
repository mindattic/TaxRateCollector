using System.Text.RegularExpressions;
using TaxRateCollector.Core.Interfaces;

namespace TaxRateCollector.Infrastructure.Services;

/// <summary>
/// Discovers files on servers that publish HTTP directory listings by parsing
/// href links from the response HTML. Works with Apache, nginx, and IIS
/// auto-index pages, as well as any page whose links follow the same convention.
/// </summary>
public sealed class WebDirectoryScanner : IWebDirectoryScanner
{
    // Matches href="..." values — captures the raw href attribute value.
    private static readonly Regex HrefRegex = new(
        @"href=""([^""#?]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Matches the last-modified date column in typical Apache directory listings.
    // Apache format example:  2025-09-10 14:22
    private static readonly Regex LastModRegex = new(
        @"(\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2})",
        RegexOptions.Compiled);

    private readonly IHttpClientFactory httpFactory;

    public WebDirectoryScanner(IHttpClientFactory httpFactory)
    {
        this.httpFactory = httpFactory;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<WebDirectoryEntry>> ListAsync(
        string directoryUrl, CancellationToken ct = default)
    {
        var http    = httpFactory.CreateClient();
        var html    = await http.GetStringAsync(directoryUrl, ct);
        var baseUri = new Uri(directoryUrl.TrimEnd('/') + '/');
        var entries = new List<WebDirectoryEntry>();

        // Walk every <a href> in the page, resolving each relative to baseUri.
        var lastModMatches = LastModRegex.Matches(html);

        foreach (Match m in HrefRegex.Matches(html))
        {
            var href = m.Groups[1].Value;

            // Skip: parent dir, anchors, query strings, off-site absolutes.
            if (href == "../"
                || href.StartsWith("?")
                || (href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    && !href.StartsWith(baseUri.GetLeftPart(UriPartial.Authority),
                                        StringComparison.OrdinalIgnoreCase)))
                continue;

            Uri resolved;
            try { resolved = new Uri(baseUri, href); }
            catch { continue; }

            // Must stay within the same origin and not go up past the base path.
            if (!resolved.ToString().StartsWith(baseUri.ToString(), StringComparison.OrdinalIgnoreCase))
                continue;

            var name = Uri.UnescapeDataString(resolved.Segments.Last()).TrimEnd('/');
            if (string.IsNullOrWhiteSpace(name)) continue;

            var isDir = href.EndsWith('/');

            // Best-effort: grab last-modified from the same row. In a directory
            // listing the date follows its link, so the first date occurring after
            // this href's position is this row's date. Pairing by position (rather
            // than by surviving-entry index) stays correct even though some hrefs
            // above — parent dir, query strings, off-site links — were skipped.
            string? lastMod = lastModMatches
                .Cast<Match>()
                .FirstOrDefault(d => d.Index > m.Index)?.Value;

            entries.Add(new WebDirectoryEntry(resolved.ToString(), name, isDir, lastMod));
        }

        return entries;
    }

    public async Task<string?> FindLatestUrlAsync(
        string directoryUrl, string fileGlob, CancellationToken ct = default)
    {
        var entries = await ListAsync(directoryUrl, ct);
        var regex   = GlobToRegex(fileGlob);

        return entries
            .Where(e => regex.IsMatch(e.Name))
            .OrderByDescending(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?.Url;
    }

    public async Task<IReadOnlyList<string>> FindAllAsync(
        string rootUrl, string fileGlob, int maxDepth = 3, CancellationToken ct = default)
    {
        var results = new List<string>();
        await ScanAsync(rootUrl, GlobToRegex(fileGlob), maxDepth, 0, results, ct);
        return results;
    }

    // ── Internals ──────────────────────────────────────────────────────────────

    private async Task ScanAsync(
        string url, Regex pattern, int maxDepth, int depth,
        List<string> results, CancellationToken ct)
    {
        if (depth > maxDepth) return;

        IReadOnlyList<WebDirectoryEntry> entries;
        try   { entries = await ListAsync(url, ct); }
        catch { return; } // unreachable or non-listing page — skip silently

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            if (entry.IsDirectory)
                await ScanAsync(entry.Url, pattern, maxDepth, depth + 1, results, ct);
            else if (pattern.IsMatch(entry.Name))
                results.Add(entry.Url);
        }
    }

    /// <summary>
    /// Converts a glob pattern into a <see cref="Regex"/>.
    /// <list type="bullet">
    ///   <item><c>**</c> → matches anything including '/'</item>
    ///   <item><c>*</c>  → matches anything except '/'</item>
    ///   <item><c>?</c>  → matches exactly one character</item>
    /// </list>
    /// </summary>
    private static Regex GlobToRegex(string glob)
    {
        // Escape everything, then un-escape our wildcards.
        var pattern = "^" + Regex.Escape(glob)
            .Replace(@"\*\*", @"[\s\S]*")  // ** → any chars including /
            .Replace(@"\*",   "[^/]*")      // *  → any chars except /
            .Replace(@"\?",   ".")          // ?  → any single char
            + "$";

        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}
