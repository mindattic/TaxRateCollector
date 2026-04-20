using System.IO.Compression;
using System.Text;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using TaxRateCollector.Core.Interfaces;

namespace TaxRateCollector.Infrastructure.Services;

public sealed class EvidenceFileStore(
    SettingsService settings,
    HttpClient http,
    ILogger<EvidenceFileStore> logger) : IEvidenceFileStore
{
    public async Task<StoredEvidenceFile> SaveAsync(
        string sourceUrl,
        byte[] content,
        string mimeType,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(SettingsService.EvidenceDirectory);

        var (evidenceType, extension) = ClassifyMime(mimeType);
        var fileName = GenerateFileName(extension);
        var fullPath = Path.Combine(SettingsService.EvidenceDirectory, fileName);

        if (evidenceType == "zip")
        {
            if (settings.Current.FullPageCapture)
                await SaveFullPageZipAsync(sourceUrl, content, fullPath, ct);
            else
                await SaveSimpleZipAsync(content, fullPath, ct);
        }
        else
        {
            await File.WriteAllBytesAsync(fullPath, content, ct);
        }

        var size = new FileInfo(fullPath).Length;
        return new StoredEvidenceFile(fileName, evidenceType, size);
    }

    // ── Simple zip: just the HTML as index.html ───────────────────────────────

    private static async Task SaveSimpleZipAsync(byte[] htmlBytes, string destPath, CancellationToken ct)
    {
        await using var fs  = new FileStream(destPath, FileMode.Create, FileAccess.Write);
        using var zip       = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);
        var entry           = zip.CreateEntry("index.html", CompressionLevel.Optimal);
        await using var es  = entry.Open();
        await es.WriteAsync(htmlBytes, ct);
    }

    // ── Full page zip: HTML + linked CSS / JS / images ────────────────────────

    private async Task SaveFullPageZipAsync(
        string pageUrl,
        byte[] htmlBytes,
        string destPath,
        CancellationToken ct)
    {
        var baseUri      = new Uri(pageUrl);
        var htmlText     = Encoding.UTF8.GetString(htmlBytes);
        var assetEntries = new Dictionary<string, byte[]>();   // zip path → bytes

        // Parse asset URLs via HtmlAgilityPack
        var doc = new HtmlDocument();
        doc.LoadHtml(htmlText);

        var assetUrls = new List<(string attr, HtmlNode node)>();
        foreach (var node in doc.DocumentNode.SelectNodes("//link[@href]") ?? Enumerable.Empty<HtmlNode>())
            assetUrls.Add(("href", node));
        foreach (var node in doc.DocumentNode.SelectNodes("//script[@src]") ?? Enumerable.Empty<HtmlNode>())
            assetUrls.Add(("src", node));
        foreach (var node in doc.DocumentNode.SelectNodes("//img[@src]") ?? Enumerable.Empty<HtmlNode>())
            assetUrls.Add(("src", node));
        foreach (var node in doc.DocumentNode.SelectNodes("//source[@src]") ?? Enumerable.Empty<HtmlNode>())
            assetUrls.Add(("src", node));

        foreach (var (attr, node) in assetUrls)
        {
            var rawHref = node.GetAttributeValue(attr, "");
            if (string.IsNullOrWhiteSpace(rawHref) || rawHref.StartsWith("data:")) continue;

            if (!Uri.TryCreate(baseUri, rawHref, out var assetUri)) continue;

            var zipPath = AssetZipPath(assetUri);
            if (assetEntries.ContainsKey(zipPath)) continue;

            try
            {
                var assetBytes = await http.GetByteArrayAsync(assetUri, ct);
                assetEntries[zipPath] = assetBytes;
                // Rewrite the href/src to the relative zip path
                node.SetAttributeValue(attr, zipPath);
            }
            catch (Exception ex)
            {
                logger.LogDebug("Could not fetch asset {Url}: {Msg}", assetUri, ex.Message);
            }
        }

        // Re-serialise the modified HTML
        var modifiedHtml = Encoding.UTF8.GetBytes(doc.DocumentNode.OuterHtml);

        await using var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write);
        using var zip      = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);

        var htmlEntry      = zip.CreateEntry("index.html", CompressionLevel.Optimal);
        await using (var es = htmlEntry.Open())
            await es.WriteAsync(modifiedHtml, ct);

        foreach (var (zipPath, bytes) in assetEntries)
        {
            var entry = zip.CreateEntry(zipPath, CompressionLevel.Optimal);
            await using var es = entry.Open();
            await es.WriteAsync(bytes, ct);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string GenerateFileName(string extension)
    {
        var ts   = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var rand = Guid.NewGuid().ToString("N")[..8];
        return $"scraped_{ts}_{rand}{extension}";
    }

    private static string AssetZipPath(Uri uri)
    {
        // Turn https://example.gov/css/main.css → assets/css/main.css
        var segments = uri.AbsolutePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine("assets", segments).Replace('\\', '/');
    }

    private static (string evidenceType, string extension) ClassifyMime(string mimeType) =>
        mimeType switch
        {
            "application/pdf"                                                    => ("pdf",  ".pdf"),
            "text/csv"                                                           => ("csv",  ".csv"),
            var m when m.StartsWith("application/vnd.openxmlformats")           => ("xlsx", ".xlsx"),
            var m when m.StartsWith("application/vnd.ms-excel")                 => ("xlsx", ".xlsx"),
            var m when m.StartsWith("text/html") || m.StartsWith("text/xhtml")  => ("zip",  ".zip"),
            _                                                                    => ("txt",  ".txt"),
        };
}
