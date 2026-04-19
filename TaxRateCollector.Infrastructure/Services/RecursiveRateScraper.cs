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
/// discovers source URLs via the configured <see cref="IDiscoveryService"/>, fetches
/// raw content, calls the AI extractor (<see cref="IRateLawExtractor"/>) to produce
/// structured <see cref="ExtractedRateLaw"/> records, then persists each one as a
/// <see cref="TaxRate"/> row with a <see cref="SourceDocument"/> evidence record.
///
/// Idempotency: by default already-current rows (same name + jurisdiction) are skipped.
/// Set <see cref="RateScrapeOptions.OverwriteExisting"/> = true to refresh them.
/// </summary>
public sealed class RecursiveRateScraper(
    IDbContextFactory<AppDbContext> dbFactory,
    IDiscoveryService discoveryService,
    IRateLawExtractor extractor,
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

            int progressFlushCounter = 0;

            foreach (var jurisdiction in queue)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var (found, created, evidence) = await ProcessJurisdictionAsync(
                        db, scrapeRun.Id, jurisdiction, options, ct);

                    jurisdictionsProcessed++;
                    rateLawsFound += found;
                    rateLawsCreated += created;
                    evidenceCaptured += evidence;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    var msg = $"[{jurisdiction.JurisdictionType}] {jurisdiction.JurisdictionName}: {ex.Message}";
                    logger.LogWarning(ex, "Rate scrape failed for jurisdiction {Id}", jurisdiction.Id);
                    errors.Add(msg);
                }

                // Flush progress to DB every 10 jurisdictions so the UI can poll it
                scrapeRun.ProcessedCount = jurisdictionsProcessed;
                if (++progressFlushCounter % 10 == 0)
                    await db.SaveChangesAsync(ct);
            }

            scrapeRun.Status = ScrapeStatus.Completed;
            scrapeRun.CompletedAt = DateTime.UtcNow.ToString("o");
            scrapeRun.TotalScraped = jurisdictionsProcessed;
            scrapeRun.ChangesDetected = rateLawsCreated;
            scrapeRun.ErrorCount = errors.Count;
            scrapeRun.ProcessedCount = jurisdictionsProcessed;
            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException)
        {
            scrapeRun.Status = ScrapeStatus.Failed;
            scrapeRun.CompletedAt = DateTime.UtcNow.ToString("o");
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            scrapeRun.Status = ScrapeStatus.Failed;
            scrapeRun.CompletedAt = DateTime.UtcNow.ToString("o");
            scrapeRun.ErrorCount = errors.Count + 1;
            errors.Add($"Fatal: {ex.Message}");
            await db.SaveChangesAsync(CancellationToken.None);
        }

        return new RateScrapeReport(
            jurisdictionsProcessed,
            rateLawsFound,
            rateLawsCreated,
            evidenceCaptured,
            errors.AsReadOnly());
    }

    // ── Jurisdiction loading ──────────────────────────────────────────────────

    private static async Task<List<Jurisdiction>> LoadSubtreeAsync(
        AppDbContext db, int rootId, CancellationToken ct)
    {
        // Load the full subtree in one query by traversing parent→children
        // using a flat list + in-memory grouping (subtrees are small enough).
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

        // BFS — yields root first, then lower tiers according to options
        var queue = new Queue<Jurisdiction>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            yield return node;

            foreach (var child in all.Where(j => j.ParentId == node.Id))
            {
                if (child.JurisdictionType == JurisdictionType.County && !options.IncludeCounties) continue;
                if (child.JurisdictionType == JurisdictionType.City && !options.IncludeCities) continue;
                if (child.JurisdictionType == JurisdictionType.District && !options.IncludeDistricts) continue;
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
        // 1. Discover the source URL for this jurisdiction
        var discovery = await discoveryService.DiscoverAsync(jurisdiction, ct);
        if (discovery.Status == "Skipped" || string.IsNullOrWhiteSpace(discovery.SourceUsed))
        {
            logger.LogDebug("Skipping {Name} — no source URL", jurisdiction.JurisdictionName);
            return (0, 0, 0);
        }

        // 2. Fetch raw content
        var (rawContent, mimeType) = await FetchContentAsync(discovery.SourceUsed, ct);
        if (string.IsNullOrWhiteSpace(rawContent))
            return (0, 0, 0);

        // 3. Extract structured rate laws via AI extractor
        var extracted = await extractor.ExtractAsync(
            jurisdiction, rawContent, mimeType, discovery.SourceUsed, ct);

        if (extracted.Count == 0)
            return (0, 0, 0);

        // 4. Apply category filter if requested
        var applicable = options.TaxCategoryId.HasValue
            ? extracted.Where(e => e.TaxCategoryId == options.TaxCategoryId.Value
                                   || e.TaxCategoryId == null).ToList()
            : extracted.ToList();

        // 5. Filter by confidence
        applicable = applicable.Where(e => e.Confidence >= options.MinConfidence).ToList();

        int created = 0, evidenceCount = 0;
        var now = DateTime.UtcNow.ToString("o");
        var contentHash = ComputeHash(rawContent);

        foreach (var law in applicable)
        {
            var (wasCreated, docCreated) = await UpsertRateLawAsync(
                db, scrapeRunId, jurisdiction.Id, law,
                discovery.SourceUsed, rawContent, mimeType, contentHash, now,
                options.OverwriteExisting, ct);

            if (wasCreated) created++;
            if (docCreated) evidenceCount++;
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "{Name}: {Found} laws extracted, {Created} persisted",
            jurisdiction.JurisdictionName, applicable.Count, created);

        return (applicable.Count, created, evidenceCount);
    }

    // ── Upsert a single extracted rate law ───────────────────────────────────

    private static async Task<(bool rateCreated, bool docCreated)> UpsertRateLawAsync(
        AppDbContext db,
        int scrapeRunId,
        int jurisdictionId,
        ExtractedRateLaw law,
        string sourceUrl,
        string rawContent,
        string mimeType,
        string contentHash,
        string now,
        bool overwrite,
        CancellationToken ct)
    {
        // Skip if there's already a pending review for the same rate in this jurisdiction
        var alreadyPending = await db.TaxRates
            .AnyAsync(t =>
                t.JurisdictionId == jurisdictionId &&
                t.Name == law.Name &&
                t.NeedsReview, ct);

        if (alreadyPending)
            return (false, false);

        // If overwriting, check for an existing live rate (don't retire it yet — approval does that)
        if (!overwrite)
        {
            var liveExists = await db.TaxRates
                .AnyAsync(t =>
                    t.JurisdictionId == jurisdictionId &&
                    t.Name == law.Name &&
                    t.IsCurrent, ct);

            if (liveExists)
                return (false, false);
        }

        // Excise taxes remitted upstream (manufacturer/importer/distributor) are already
        // embedded in the wholesale cost — they must not be re-added at checkout.
        var isIncludedInPrice = law.TaxType == Core.Enums.TaxType.ExciseTax
            && law.RemittancePoint is Core.Enums.RemittancePoint.Manufacturer
                or Core.Enums.RemittancePoint.Importer
                or Core.Enums.RemittancePoint.Distributor;

        var rate = new TaxRate
        {
            JurisdictionId     = jurisdictionId,
            ScrapeRunId        = scrapeRunId,
            Name               = law.Name,
            Rate               = law.Rate,
            RateBasis          = law.Basis,
            Unit               = law.Unit,
            TaxType            = law.TaxType,
            IsIncludedInPrice  = isIncludedInPrice,
            IsCompound         = law.IsCompound,
            MaxTaxableAmount   = law.MaxTaxableAmount,
            IsTemporary        = law.IsTemporary,
            ProductCategory    = law.ProductCategory,
            SaleContext        = law.SaleContext,
            RemittancePoint    = law.RemittancePoint,
            MinAbv             = law.MinAbv,
            MaxAbv             = law.MaxAbv,
            Conditions         = law.Conditions,
            StatutoryReference = law.StatutoryReference,
            EffectiveDate      = ParseDate(law.EffectiveDate),
            ExpirationDate     = ParseDate(law.ExpirationDate),
            TaxCategoryId      = law.TaxCategoryId,
            RawEvidence        = law.RawEvidence,
            ScrapedAt          = now,
            IsCurrent          = false,   // promoted to true only after human approval
            NeedsReview        = true,
        };

        db.TaxRates.Add(rate);

        // Attach evidence document
        var doc = new SourceDocument
        {
            TaxRate       = rate,
            SourceType    = InferSourceType(mimeType),
            SourceUrl     = sourceUrl,
            MimeType      = mimeType,
            FetchedAt     = now,
            ContentHash   = contentHash,
            EvidenceType  = MimeToEvidenceType(mimeType),
            RawContent    = rawContent.Length <= 65536 ? rawContent : rawContent[..65536],
            IsActive      = true,
        };
        db.SourceDocuments.Add(doc);

        return (true, true);
    }

    // ── HTTP fetch ────────────────────────────────────────────────────────────

    private async Task<(string content, string mimeType)> FetchContentAsync(
        string url, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var mime = response.Content.Headers.ContentType?.MediaType ?? "text/html";
            var content = await response.Content.ReadAsStringAsync(ct);
            return (content, mime);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Failed to fetch {Url}: {Message}", url, ex.Message);
            return (string.Empty, string.Empty);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
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
        _                  => "html",
    };
}
