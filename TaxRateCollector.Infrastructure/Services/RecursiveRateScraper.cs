using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.Infrastructure.Services;

/// <summary>
/// Recursively traverses a jurisdiction hierarchy (State → County → City → District),
/// discovers source URLs, fetches raw content, calls the AI extractor to produce
/// structured <see cref="ExtractedRateLaw"/> records, then persists each one as a
/// <see cref="TaxRate"/> with a <see cref="SourceDocument"/> evidence file on disk.
/// </summary>
public sealed class RecursiveRateScraper(
    IDbContextFactory<AppDbContext> dbFactory,
    IDiscoveryService discoveryService,
    IRateLawExtractor extractor,
    IEvidenceFileStore evidenceStore,
    HttpClient http,
    ILogger<RecursiveRateScraper> logger) : IRecursiveRateScraper
{
    public async Task<RateScrapeReport> ScrapeAsync(
        int rootJurisdictionId,
        RateScrapeOptions options,
        CancellationToken ct = default)
    {
        var errors = new List<string>();
        int jurisdictionsProcessed = 0, rateLawsFound = 0, rateLawsCreated = 0, evidenceCaptured = 0;

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var scrapeRun = new ScrapeRun
        {
            StartedAt = DateTime.UtcNow.ToString("o"),
            Status = ScrapeStatus.Running,
        };
        db.ScrapeRuns.Add(scrapeRun);
        await db.SaveChangesAsync(ct);

        try
        {
            var allJurisdictions = await LoadSubtreeAsync(db, rootJurisdictionId, ct);
            var queue = BuildQueue(allJurisdictions, rootJurisdictionId, options).ToList();

            scrapeRun.TotalCount = queue.Count;
            await db.SaveChangesAsync(ct);

            int flushCounter = 0;

            foreach (var jurisdiction in queue)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var (found, created, evidence) = await ProcessJurisdictionAsync(
                        db, scrapeRun.Id, jurisdiction, options, ct);

                    jurisdictionsProcessed++;
                    rateLawsFound     += found;
                    rateLawsCreated   += created;
                    evidenceCaptured  += evidence;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    errors.Add($"[{jurisdiction.JurisdictionType}] {jurisdiction.JurisdictionName}: {ex.Message}");
                    logger.LogWarning(ex, "Rate scrape failed for jurisdiction {Id}", jurisdiction.Id);
                }

                scrapeRun.ProcessedCount = jurisdictionsProcessed;
                scrapeRun.LastProcessedJurisdictionId = jurisdiction.Id;
                if (++flushCounter % 10 == 0)
                    await db.SaveChangesAsync(ct);
            }

            scrapeRun.Status          = ScrapeStatus.Completed;
            scrapeRun.CompletedAt     = DateTime.UtcNow.ToString("o");
            scrapeRun.TotalScraped    = jurisdictionsProcessed;
            scrapeRun.ChangesDetected = rateLawsCreated;
            scrapeRun.ErrorCount      = errors.Count;
            scrapeRun.ProcessedCount  = jurisdictionsProcessed;
            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException)
        {
            scrapeRun.Status      = ScrapeStatus.Paused;
            scrapeRun.CompletedAt = DateTime.UtcNow.ToString("o");
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            scrapeRun.Status      = ScrapeStatus.Failed;
            scrapeRun.CompletedAt = DateTime.UtcNow.ToString("o");
            scrapeRun.ErrorCount  = errors.Count + 1;
            errors.Add($"Fatal: {ex.Message}");
            await db.SaveChangesAsync(CancellationToken.None);
        }

        return new RateScrapeReport(
            jurisdictionsProcessed, rateLawsFound, rateLawsCreated, evidenceCaptured,
            errors.AsReadOnly());
    }

    // ── Jurisdiction loading ──────────────────────────────────────────────────

    private static async Task<List<Jurisdiction>> LoadSubtreeAsync(
        AppDbContext db, int rootId, CancellationToken ct)
    {
        var root = await db.Jurisdictions
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == rootId, ct)
            ?? throw new InvalidOperationException($"Jurisdiction {rootId} not found.");

        var stateCode = root.StateCode.Length > 0 ? root.StateCode : root.FipsCode[..2];

        return await db.Jurisdictions
            .AsNoTracking()
            .Where(j => j.StateCode == stateCode && j.IsActive)
            .ToListAsync(ct);
    }

    private static IEnumerable<Jurisdiction> BuildQueue(
        List<Jurisdiction> all, int rootId, RateScrapeOptions options)
    {
        var byId = all.ToDictionary(j => j.Id);
        if (!byId.TryGetValue(rootId, out var root)) yield break;

        // Pre-build children-by-parent index so traversal is O(n) instead of O(n²).
        var byParent = all
            .Where(j => j.ParentId.HasValue)
            .GroupBy(j => j.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var queue = new Queue<Jurisdiction>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            yield return node;

            if (!byParent.TryGetValue(node.Id, out var children)) continue;
            foreach (var child in children)
            {
                if (child.JurisdictionType == JurisdictionType.County   && !options.IncludeCounties)  continue;
                if (child.JurisdictionType == JurisdictionType.City      && !options.IncludeCities)    continue;
                if (child.JurisdictionType == JurisdictionType.District  && !options.IncludeDistricts) continue;
                queue.Enqueue(child);
            }
        }
    }

    // ── Per-jurisdiction processing ───────────────────────────────────────────

    private async Task<(int found, int created, int evidence)> ProcessJurisdictionAsync(
        AppDbContext db,
        int scrapeRunId,
        Jurisdiction jurisdiction,
        RateScrapeOptions options,
        CancellationToken ct)
    {
        var discovery = await discoveryService.DiscoverAsync(jurisdiction, ct);
        if (discovery.Status == "Skipped" || string.IsNullOrWhiteSpace(discovery.SourceUsed))
        {
            logger.LogDebug("Skipping {Name} — no source URL", jurisdiction.JurisdictionName);
            return (0, 0, 0);
        }

        // Primary URL drives extraction; additional URLs (newline-separated in SourceUrl)
        // are fetched and attached as supplementary corroborating evidence.
        var primaryUrl = discovery.SourceUsed;
        var allUrls = jurisdiction.SourceUrls();
        var supplementaryUrls = allUrls.Where(u => !string.Equals(u, primaryUrl, StringComparison.OrdinalIgnoreCase));

        var (rawBytes, mimeType) = await FetchBytesAsync(primaryUrl, ct);
        if (rawBytes.Length == 0) return (0, 0, 0);

        // Convert to text for the AI extractor (binary types get a placeholder)
        var rawText = IsTextMime(mimeType)
            ? Encoding.UTF8.GetString(rawBytes)
            : $"[Binary: {mimeType}, {rawBytes.Length:N0} bytes from {primaryUrl}]";

        var extracted = await extractor.ExtractAsync(
            jurisdiction, rawText, mimeType, primaryUrl, ct);

        if (extracted.Count == 0) return (0, 0, 0);

        var applicable = (options.TaxCategoryId.HasValue
            ? extracted.Where(e => e.TaxCategoryId == options.TaxCategoryId || e.TaxCategoryId == null)
            : extracted)
            .Where(e => e.Confidence >= options.MinConfidence)
            .ToList();

        if (applicable.Count == 0) return (0, 0, 0);

        // Save primary evidence
        StoredEvidenceFile? primaryStored = null;
        try
        {
            primaryStored = await evidenceStore.SaveAsync(primaryUrl, rawBytes, mimeType, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Evidence save failed for {Url}", primaryUrl);
        }

        var contentHash = primaryStored?.ContentHash ?? ComputeHash(rawBytes);
        var now         = DateTime.UtcNow.ToString("o");

        // Pre-fetch supplementary sources once and reuse across all extracted laws
        var supplements = new List<SupplementaryEvidence>();
        foreach (var url in supplementaryUrls)
        {
            try
            {
                var (suppBytes, suppMime) = await FetchBytesAsync(url, ct);
                if (suppBytes.Length == 0) continue;
                var stored = await evidenceStore.SaveAsync(url, suppBytes, suppMime, ct);
                supplements.Add(new SupplementaryEvidence(url, suppMime, stored,
                    stored.ContentHash, ExcerptText(suppBytes, suppMime)));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Supplementary evidence fetch failed for {Url}", url);
            }
        }

        int created = 0, evidenceCount = 0;

        foreach (var law in applicable)
        {
            var (wasCreated, docsCreated) = await UpsertRateLawAsync(
                db, scrapeRunId, jurisdiction.Id, law,
                primaryUrl, rawText, mimeType, contentHash,
                primaryStored, supplements, now, options.OverwriteExisting, ct);

            if (wasCreated) created++;
            evidenceCount += docsCreated;
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "{Name}: {Found} laws extracted, {Created} persisted, {Evidence} evidence docs (primary + {Supps} supplementary)",
            jurisdiction.JurisdictionName, applicable.Count, created, evidenceCount, supplements.Count);

        return (applicable.Count, created, evidenceCount);
    }

    private sealed record SupplementaryEvidence(
        string SourceUrl, string MimeType, StoredEvidenceFile Stored, string ContentHash, string Excerpt);

    private static string ExcerptText(byte[] bytes, string mimeType) =>
        IsTextMime(mimeType) && bytes.Length > 0
            ? Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 8192))
            : string.Empty;

    // ── Upsert a single extracted rate law ───────────────────────────────────

    private static async Task<(bool rateCreated, int docsCreated)> UpsertRateLawAsync(
        AppDbContext db,
        int scrapeRunId,
        int jurisdictionId,
        ExtractedRateLaw law,
        string sourceUrl,
        string rawText,
        string mimeType,
        string contentHash,
        StoredEvidenceFile? storedFile,
        IReadOnlyList<SupplementaryEvidence> supplements,
        string now,
        bool overwrite,
        CancellationToken ct)
    {
        var alreadyPending = await db.TaxRates
            .AnyAsync(t => t.JurisdictionId == jurisdictionId && t.Name == law.Name && !t.AutoApprove, ct);
        if (alreadyPending) return (false, 0);

        if (!overwrite)
        {
            var liveExists = await db.TaxRates
                .AnyAsync(t => t.JurisdictionId == jurisdictionId && t.Name == law.Name && t.IsCurrent, ct);
            if (liveExists) return (false, 0);
        }

        var isIncludedInPrice = law.TaxType == Core.Enums.TaxType.ExciseTax
            && law.RemittancePoint is Core.Enums.RemittancePoint.Manufacturer
                or Core.Enums.RemittancePoint.Importer
                or Core.Enums.RemittancePoint.Distributor;

        var rate = new TaxRate
        {
            JurisdictionId      = jurisdictionId,
            ScrapeRunId         = scrapeRunId,
            Name                = law.Name,
            Rate                = law.Rate,
            RateBasis           = law.Basis,
            Unit                = law.Unit,
            TaxType             = law.TaxType,
            IsIncludedInPrice   = isIncludedInPrice,
            IsCompound          = law.IsCompound,
            MinTaxableAmount    = law.MinTaxableAmount,
            MaxTaxableAmount    = law.MaxTaxableAmount,
            FlatCapPerUnit      = law.FlatCapPerUnit,
            IsTemporary         = law.IsTemporary,
            IsRecurring         = law.IsRecurring,
            AdjustmentFrequency = law.AdjustmentFrequency,
            AdjustmentMechanism = law.AdjustmentMechanism,
            ProductCategory     = law.ProductCategory,
            SaleContext         = law.SaleContext,
            RemittancePoint     = law.RemittancePoint,
            MinAbv              = law.MinAbv,
            MaxAbv              = law.MaxAbv,
            Conditions          = law.Conditions,
            StatutoryReference  = law.StatutoryReference,
            EffectiveDate       = ParseDate(law.EffectiveDate),
            ExpirationDate      = ParseDate(law.ExpirationDate),
            TaxCategoryId       = law.TaxCategoryId,
            RawEvidence         = law.RawEvidence,
            ScrapedAt           = now,
            IsCurrent           = false,
            AutoApprove         = false,
        };

        db.TaxRates.Add(rate);

        var primaryDoc = new SourceDocument
        {
            TaxRate          = rate,
            SourceType       = InferSourceType(mimeType),
            SourceUrl        = sourceUrl,
            MimeType         = mimeType,
            FetchedAt        = now,
            ContentHash      = contentHash,
            EvidenceType     = storedFile?.EvidenceType ?? MimeToEvidenceType(mimeType),
            FileName         = storedFile?.FileName ?? string.Empty,
            OriginalFileName = Path.GetFileName(new Uri(sourceUrl).AbsolutePath),
            // Keep a readable snippet inline even when file is on disk
            RawContent       = rawText.Length <= 8192 ? rawText : rawText[..8192],
            IsActive         = true,
        };
        db.SourceDocuments.Add(primaryDoc);

        foreach (var supp in supplements)
        {
            db.SourceDocuments.Add(new SourceDocument
            {
                TaxRate          = rate,
                SourceType       = InferSourceType(supp.MimeType),
                SourceUrl        = supp.SourceUrl,
                MimeType         = supp.MimeType,
                FetchedAt        = now,
                ContentHash      = supp.ContentHash,
                EvidenceType     = supp.Stored.EvidenceType,
                FileName         = supp.Stored.FileName,
                OriginalFileName = Path.GetFileName(new Uri(supp.SourceUrl).AbsolutePath),
                RawContent       = supp.Excerpt,
                IsActive         = true,
            });
        }

        return (true, 1 + supplements.Count);
    }

    // ── HTTP fetch (bytes, preserves binary) ──────────────────────────────────

    private async Task<(byte[] bytes, string mimeType)> FetchBytesAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var mime  = response.Content.Headers.ContentType?.MediaType ?? "text/html";
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            return (bytes, mime);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Failed to fetch {Url}: {Message}", url, ex.Message);
            return ([], string.Empty);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsTextMime(string mimeType) =>
        mimeType.StartsWith("text/") || mimeType == "application/json" || mimeType == "application/xml";

    private static string ComputeHash(byte[] content)
    {
        var bytes = SHA256.HashData(content);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static SourceType InferSourceType(string mimeType) => mimeType switch
    {
        "application/pdf"  => SourceType.Pdf,
        "text/csv"         => SourceType.Csv,
        "application/json" => SourceType.Api,
        _                  => SourceType.Website,
    };

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateOnly.TryParse(value, out var d) ? d : null;
    }

    private static string MimeToEvidenceType(string mimeType) => mimeType switch
    {
        "application/pdf"  => "pdf",
        "text/csv"         => "csv",
        "application/json" => "json",
        var m when m.StartsWith("application/vnd.openxmlformats") => "xlsx",
        var m when m.StartsWith("text/html")                      => "zip",
        _                                                          => "txt",
    };
}
