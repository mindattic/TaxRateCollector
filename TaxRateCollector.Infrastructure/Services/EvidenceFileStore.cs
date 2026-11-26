using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using TaxRateCollector.Core.Interfaces;

namespace TaxRateCollector.Infrastructure.Services;

public sealed class EvidenceFileStore(
    ILogger<EvidenceFileStore> logger,
    SettingsService? settings = null,
    IHttpClientFactory? httpFactory = null) : IEvidenceFileStore
{
    private const int MaxAssets = 100;
    private const long MaxAssetBytes = 5 * 1024 * 1024;
    private const long MaxTotalAssetBytes = 25 * 1024 * 1024;

    public async Task<StoredEvidenceFile> SaveAsync(
        string sourceUrl,
        byte[] content,
        string mimeType,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(SettingsService.EvidenceDirectory);

        var (evidenceType, extension) = ClassifyMime(mimeType);

        string? slug = null;
        byte[] toWrite;

        if (IsHtml(mimeType) && settings?.Current.FullPageCapture == true)
        {
            (toWrite, slug) = await BundleFullPageAsync(sourceUrl, content, ct);
            (evidenceType, extension) = ("zip", ".zip");
        }
        else if (IsHtml(mimeType))
        {
            (toWrite, slug) = StripHtmlBytes(sourceUrl, content);
        }
        else if (IsTextual(mimeType))
        {
            toWrite = WrapTextBytes(sourceUrl, content);
        }
        else
        {
            toWrite = content;
        }

        // Hash the bytes that actually go to disk so the DB ContentHash can be
        // verified later by re-reading the file and re-hashing it.
        var fullHash = Convert.ToHexString(SHA256.HashData(toWrite)).ToLowerInvariant();
        var shortHash = fullHash[..12];
        var fileName = GenerateFileName(extension, slug, shortHash);
        var fullPath = Path.Combine(SettingsService.EvidenceDirectory, fileName);

        if (File.Exists(fullPath))
        {
            logger.LogDebug("Evidence deduped: {File}", fileName);
        }
        else
        {
            await File.WriteAllBytesAsync(fullPath, toWrite, ct);
            logger.LogDebug("Evidence saved: {File} ({Bytes} bytes)", fileName, toWrite.Length);
        }

        var size = new FileInfo(fullPath).Length;
        return new StoredEvidenceFile(fileName, evidenceType, size, fullHash);
    }

    /// <summary>
    /// Zips the page as fetched (index.html) with its linked stylesheets, scripts and images under assets/,
    /// rewriting those references to the bundled copies. Assets that fail to download stay as absolute links.
    /// </summary>
    private async Task<(byte[] Bytes, string? Slug)> BundleFullPageAsync(string sourceUrl, byte[] htmlBytes, CancellationToken ct)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(Encoding.UTF8.GetString(htmlBytes));

        var titleNode = doc.DocumentNode.SelectSingleNode("//title");
        var title = System.Net.WebUtility.HtmlDecode(titleNode?.InnerText.Trim() ?? "");
        var slug = string.IsNullOrEmpty(title) ? null : Slugify(title);

        var assets = new List<(string Name, byte[] Bytes)>();
        var seen = new Dictionary<string, string>();
        long total = 0;

        if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out var baseUri) && httpFactory is not null)
        {
            var http = httpFactory.CreateClient();
            var refs = doc.DocumentNode.SelectNodes("//link[@href][translate(@rel,'STYLEHT','styleht')='stylesheet']|//script[@src]|//img[@src]")
                       ?? Enumerable.Empty<HtmlNode>();
            foreach (var node in refs)
            {
                var attr = node.Name == "link" ? "href" : "src";
                var raw = node.GetAttributeValue(attr, "");
                if (!Uri.TryCreate(baseUri, raw, out var abs) || abs.Scheme is not ("http" or "https")) continue;

                var key = abs.GetLeftPart(UriPartial.Query);
                if (!seen.TryGetValue(key, out var name))
                {
                    if (assets.Count >= MaxAssets || total >= MaxTotalAssetBytes) continue;
                    var bytes = await TryDownloadAsync(http, abs, ct);
                    if (bytes is null) continue;
                    total += bytes.Length;
                    var ext = Path.GetExtension(abs.AbsolutePath);
                    if (ext.Length > 8 || ext.Any(c => !char.IsLetterOrDigit(c) && c != '.')) ext = "";
                    name = $"assets/{assets.Count:000}{ext}";
                    assets.Add((name, bytes));
                    seen[key] = name;
                }
                node.SetAttributeValue(attr, name);
                node.Attributes.Remove("integrity");
                node.Attributes.Remove("srcset");
            }
        }

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string entryName, byte[] data)
            {
                var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var s = entry.Open();
                s.Write(data);
            }
            Add("index.html", Encoding.UTF8.GetBytes(doc.DocumentNode.OuterHtml));
            Add("source.txt", Encoding.UTF8.GetBytes($"Source: {sourceUrl}\n"));
            foreach (var (name, bytes) in assets) Add(name, bytes);
        }
        logger.LogDebug("Full-page capture of {Url}: {Count} assets, {Bytes} bytes", sourceUrl, assets.Count, total);
        return (ms.ToArray(), slug);
    }

    private async Task<byte[]?> TryDownloadAsync(HttpClient http, Uri url, CancellationToken ct)
    {
        try
        {
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode || resp.Content.Headers.ContentLength > MaxAssetBytes) return null;
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var buf = new MemoryStream();
            var chunk = new byte[81920];
            int n;
            while ((n = await stream.ReadAsync(chunk, ct)) > 0)
            {
                buf.Write(chunk, 0, n);
                if (buf.Length > MaxAssetBytes) return null;
            }
            return buf.ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug("Asset skipped {Url}: {Message}", url, ex.Message);
            return null;
        }
    }

    private static (byte[] Bytes, string? Slug) StripHtmlBytes(string sourceUrl, byte[] htmlBytes)
    {
        var html = Encoding.UTF8.GetString(htmlBytes);
        var doc  = new HtmlDocument();
        doc.LoadHtml(html);

        // Extract title before stripping
        var titleNode = doc.DocumentNode.SelectSingleNode("//title");
        var title     = System.Net.WebUtility.HtmlDecode(titleNode?.InnerText.Trim() ?? "");

        // Remove noise nodes
        foreach (var tag in new[] { "script", "style", "link", "meta", "noscript", "iframe", "nav", "header", "footer" })
            foreach (var node in doc.DocumentNode.SelectNodes($"//{tag}") ?? Enumerable.Empty<HtmlNode>())
                node.Remove();

        // Extract body content (fall back to full document if no body)
        var bodyNode  = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;
        var bodyHtml  = HighlightRates(LinkifyUrls(bodyNode.InnerHtml.Trim()));

        var titleAttr = System.Net.WebUtility.HtmlEncode(title);
        var urlAttr   = System.Net.WebUtility.HtmlEncode(sourceUrl);

        var stripped = $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <title>{{titleAttr}}</title>
              <style>
                body { font-family: sans-serif; font-size: 14px; line-height: 1.5; padding: 1rem; color: #222; }
                .ev-banner { background: #f0f4f8; border-bottom: 1px solid #d0d8e0; padding: .4rem .8rem; margin: -1rem -1rem 1rem; font-size: .8em; word-break: break-all; }
                .ev-banner a { color: #0055aa; }
              </style>
            </head>
            <body>
              <div class="ev-banner">
                <strong>{{titleAttr}}</strong><br>
                <a href="{{urlAttr}}" target="_blank" rel="noopener noreferrer">{{urlAttr}}</a>
              </div>
              {{bodyHtml}}
            </body>
            </html>
            """;

        var slug = string.IsNullOrEmpty(title) ? null : Slugify(title);
        return (Encoding.UTF8.GetBytes(stripped), slug);
    }

    private static byte[] WrapTextBytes(string sourceUrl, byte[] content)
    {
        var body = Encoding.UTF8.GetString(content).Trim();
        return Encoding.UTF8.GetBytes($"URL: {sourceUrl}\n\nBODY:\n{body}");
    }

    private static string GenerateFileName(string extension, string? slug, string hash) =>
        string.IsNullOrEmpty(slug)
            ? $"scraped_{hash}{extension}"
            : $"{slug}_{hash}{extension}";

    private static readonly Regex BareUrlPattern =
        new(@"(?<!href=[""']|src=[""']|action=[""'])https?://[^\s<>""']+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RatePattern =
        new(@"\b\d+(?:\.\d+)?\s*%", RegexOptions.Compiled);

    private static string HighlightRates(string html) =>
        RatePattern.Replace(html, m =>
            $"<mark style=\"background:#ffe066;padding:0 2px;border-radius:2px\">{m.Value}</mark>");

    private static string LinkifyUrls(string html) =>
        BareUrlPattern.Replace(html, m =>
        {
            var url     = m.Value.TrimEnd('.', ',', ')', ']', ';');
            var encoded = System.Net.WebUtility.HtmlEncode(url);
            return $"<a href=\"{encoded}\" target=\"_blank\" rel=\"noopener noreferrer\">{encoded}</a>";
        });

    private static string Slugify(string title)
    {
        var s = title.ToLowerInvariant();
        s = Regex.Replace(s, @"[^a-z0-9\s-]", "");
        s = Regex.Replace(s, @"\s+", "-");
        s = Regex.Replace(s, @"-{2,}", "-");
        s = s.Trim('-');
        if (s.Length > 60) s = s[..60].TrimEnd('-');
        return s;
    }

    private static bool IsHtml(string mimeType) =>
        mimeType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) ||
        mimeType.StartsWith("text/xhtml", StringComparison.OrdinalIgnoreCase);

    private static bool IsTextual(string mimeType) =>
        mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
        mimeType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ||
        mimeType.StartsWith("application/xml", StringComparison.OrdinalIgnoreCase);

    private static (string evidenceType, string extension) ClassifyMime(string mimeType) =>
        mimeType switch
        {
            "application/pdf"                                          => ("pdf",  ".pdf"),
            var m when m.StartsWith("application/vnd.openxmlformats") => ("xlsx", ".xlsx"),
            var m when m.StartsWith("application/vnd.ms-excel")       => ("xlsx", ".xlsx"),
            var m when m.StartsWith("text/csv", StringComparison.OrdinalIgnoreCase) => ("csv", ".csv"),
            var m when IsHtml(m)                                       => ("html", ".html"),
            _                                                          => ("txt",  ".txt"),
        };
}
