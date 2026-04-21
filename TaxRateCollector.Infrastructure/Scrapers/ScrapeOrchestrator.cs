using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Data;
using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.Infrastructure.Scrapers;

public class ScrapeOrchestrator(
    IDbContextFactory<AppDbContext> dbFactory,
    IEnumerable<IScrapeStrategy> strategies,
    IEnumerable<IStateBulkScraper> bulkScrapers,
    IEvidenceFileStore evidenceStore,
    IDiffEngine diffEngine,
    ILogger<ScrapeOrchestrator> logger) : IScrapeOrchestrator
{
    public async Task<int> RunFullScrapeAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var run = new ScrapeRun
        {
            StartedAt = DateTime.UtcNow.ToString("o"),
            Status    = ScrapeStatus.Running
        };
        db.ScrapeRuns.Add(run);
        await db.SaveChangesAsync(ct);

        var jurisdictions = await db.Jurisdictions
            .Where(j => j.IsActive)
            .OrderBy(j => j.Id)
            .ToListAsync(ct);

        run.TotalCount = jurisdictions.Count;
        await db.SaveChangesAsync(ct);

        return await RunQueueAsync(db, run, jurisdictions, ct);
    }

    public async Task<int> ResumeAsync(int scrapeRunId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var run = await db.ScrapeRuns.FindAsync([scrapeRunId], ct)
            ?? throw new InvalidOperationException($"ScrapeRun {scrapeRunId} not found.");

        run.Status = ScrapeStatus.Running;
        await db.SaveChangesAsync(ct);

        var remaining = await db.Jurisdictions
            .Where(j => j.IsActive && j.Id > (run.LastProcessedJurisdictionId ?? 0))
            .OrderBy(j => j.Id)
            .ToListAsync(ct);

        run.TotalCount = run.ProcessedCount + remaining.Count;
        await db.SaveChangesAsync(ct);

        return await RunQueueAsync(db, run, remaining, ct);
    }

    public async Task<int?> GetPausedRunIdAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ScrapeRuns
            .Where(r => r.Status == ScrapeStatus.Paused)
            .OrderByDescending(r => r.Id)
            .Select(r => (int?)r.Id)
            .FirstOrDefaultAsync(ct);
    }

    // ── Shared processing loop ────────────────────────────────────────────────

    private async Task<int> RunQueueAsync(
        AppDbContext db,
        ScrapeRun run,
        List<Jurisdiction> jurisdictions,
        CancellationToken ct)
    {
        // Index bulk scrapers by state code for O(1) lookup
        var bulkByState = bulkScrapers.ToDictionary(s => s.StateCode.ToUpperInvariant());
        // Track which states have been bulk-scraped this run (avoid re-fetching)
        var bulkDoneStates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int errors = 0, scraped = 0;

        try
        {
            foreach (var jurisdiction in jurisdictions)
            {
                ct.ThrowIfCancellationRequested();

                var stateCode = jurisdiction.StateCode?.ToUpperInvariant() ?? "";

                try
                {
                    // ── Try state bulk scraper first ──────────────────────────
                    if (!string.IsNullOrEmpty(stateCode)
                        && bulkByState.TryGetValue(stateCode, out var bulk)
                        && !bulkDoneStates.Contains(stateCode)
                        && jurisdiction.JurisdictionType == JurisdictionType.State)
                    {
                        await RunBulkScraperAsync(db, run, bulk, stateCode, ct);
                        bulkDoneStates.Add(stateCode);
                        scraped++;
                    }
                    // ── Skip non-state jurisdictions already handled by bulk ──
                    else if (!string.IsNullOrEmpty(stateCode) && bulkDoneStates.Contains(stateCode))
                    {
                        // Rates already written by bulk pass — count as processed
                    }
                    // ── Fall back to per-jurisdiction strategy ────────────────
                    else
                    {
                        var strategy = strategies.FirstOrDefault(s => s.CanHandle(jurisdiction));
                        if (strategy is null)
                        {
                            logger.LogDebug("No strategy for {Name}", jurisdiction.JurisdictionName);
                        }
                        else
                        {
                            await RunStrategyAsync(db, run, strategy, jurisdiction, ct);
                            scraped++;
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Scrape failed for {Name}", jurisdiction.JurisdictionName);
                    errors++;
                }

                run.LastProcessedJurisdictionId = jurisdiction.Id;
                run.ProcessedCount++;

                if (run.ProcessedCount % 10 == 0)
                    await db.SaveChangesAsync(ct);
            }

            await db.SaveChangesAsync(CancellationToken.None);

            var diff = await diffEngine.DetectChangesAsync(run.Id, ct);

            run.Status           = errors == jurisdictions.Count && jurisdictions.Count > 0
                                   ? ScrapeStatus.Failed : ScrapeStatus.Completed;
            run.CompletedAt      = DateTime.UtcNow.ToString("o");
            run.TotalScraped     = run.ProcessedCount;
            run.ChangesDetected += diff.Changes.Count;
            run.ErrorCount      += errors;
            await db.SaveChangesAsync(CancellationToken.None);

            return diff.Changes.Count;
        }
        catch (OperationCanceledException)
        {
            await db.SaveChangesAsync(CancellationToken.None);
            run.Status     = ScrapeStatus.Paused;
            run.ErrorCount += errors;
            await db.SaveChangesAsync(CancellationToken.None);
            return 0;
        }
    }

    // ── Bulk state scraper ────────────────────────────────────────────────────

    public async Task<int> RunBulkForStateAsync(
        string stateCode,
        int? taxCategoryId = null,
        bool needsReview = true,
        CancellationToken ct = default)
    {
        var bulk = bulkScrapers.FirstOrDefault(s =>
            s.StateCode.Equals(stateCode, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"No bulk scraper registered for state '{stateCode}'.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var run = new ScrapeRun { StartedAt = DateTime.UtcNow.ToString("o"), Status = ScrapeStatus.Running };
        db.ScrapeRuns.Add(run);
        await db.SaveChangesAsync(ct);

        await RunBulkScraperAsync(db, run, bulk, stateCode, ct,
            taxCategoryId: taxCategoryId, needsReview: needsReview, overwriteExisting: true);

        run.Status       = ScrapeStatus.Completed;
        run.CompletedAt  = DateTime.UtcNow.ToString("o");
        run.TotalScraped = run.ProcessedCount;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("RunBulkForStateAsync {State}: {Count} rates saved", stateCode, run.TotalScraped);
        return run.TotalScraped;
    }

    private async Task RunBulkScraperAsync(
        AppDbContext db,
        ScrapeRun run,
        IStateBulkScraper bulk,
        string stateCode,
        CancellationToken ct,
        int? taxCategoryId = null,
        bool needsReview = true,
        bool overwriteExisting = false)
    {
        logger.LogInformation("Running bulk scraper for {State}", stateCode);
        var results = await bulk.ScrapeAsync(ct);
        if (results.Count == 0) return;

        // Build FIPS → Jurisdiction lookup for the state
        var stateJurisdictions = await db.Jurisdictions
            .Where(j => j.StateCode == stateCode && j.IsActive)
            .ToListAsync(ct);
        var byFips = stateJurisdictions
            .Where(j => !string.IsNullOrEmpty(j.FipsCode))
            .ToDictionary(j => j.FipsCode, StringComparer.OrdinalIgnoreCase);
        var byName = stateJurisdictions
            .ToDictionary(j => j.JurisdictionName.ToUpperInvariant());

        var now = DateTime.UtcNow.ToString("o");

        foreach (var result in results)
        {
            ct.ThrowIfCancellationRequested();

            // Resolve jurisdiction
            Jurisdiction? jurisdiction = null;
            if (!string.IsNullOrEmpty(result.FipsCode))
                byFips.TryGetValue(result.FipsCode, out jurisdiction);
            if (jurisdiction is null)
                byName.TryGetValue(result.JurisdictionName.ToUpperInvariant(), out jurisdiction);

            if (jurisdiction is null)
            {
                logger.LogDebug("Bulk result '{Name}' (FIPS {Fips}) not matched to a jurisdiction",
                    result.JurisdictionName, result.FipsCode);
                continue;
            }

            if (overwriteExisting)
            {
                var stale = await db.TaxRates
                    .Where(t => t.JurisdictionId == jurisdiction.Id
                                && t.Name == result.RateName
                                && t.IsCurrent)
                    .ToListAsync(ct);
                foreach (var s in stale) s.IsCurrent = false;
            }
            else
            {
                var liveExists = await db.TaxRates
                    .AnyAsync(t => t.JurisdictionId == jurisdiction.Id
                                   && t.Name == result.RateName
                                   && t.IsCurrent, ct);
                if (liveExists) continue;
            }

            // Save evidence file
            StoredEvidenceFile? storedFile = null;
            try
            {
                storedFile = await evidenceStore.SaveAsync(
                    result.SourceUrl, result.EvidenceBytes, result.EvidenceMimeType, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Evidence save failed for bulk result {Name}", result.JurisdictionName);
            }

            var hash = Convert.ToHexString(SHA256.HashData(result.EvidenceBytes)).ToLowerInvariant();

            var rate = new TaxRate
            {
                JurisdictionId      = jurisdiction.Id,
                ScrapeRunId         = run.Id,
                TaxCategoryId       = taxCategoryId,
                Name                = result.RateName,
                Rate                = result.Rate,
                RateBasis           = RateBasis.Percentage,
                SaleContext         = SaleContext.Any,
                RemittancePoint     = RemittancePoint.Retailer,
                EffectiveDate       = ParseDate(result.EffectiveDate),
                ScrapedAt           = now,
                IsCurrent           = true,
                NeedsReview         = needsReview,
            };
            db.TaxRates.Add(rate);

            db.SourceDocuments.Add(new SourceDocument
            {
                TaxRate          = rate,
                SourceType       = SourceType.Website,
                SourceUrl        = result.SourceUrl,
                MimeType         = result.EvidenceMimeType,
                FetchedAt        = now,
                ContentHash      = hash,
                EvidenceType     = storedFile?.EvidenceType ?? "txt",
                FileName         = storedFile?.FileName ?? string.Empty,
                OriginalFileName = result.EvidenceOriginalFileName,
                RawContent       = string.Empty,
                IsActive         = true,
            });

            run.ProcessedCount++;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Bulk scraper {State}: {Count} results processed", stateCode, results.Count);
    }

    // ── Per-jurisdiction strategy ─────────────────────────────────────────────

    private async Task RunStrategyAsync(
        AppDbContext db,
        ScrapeRun run,
        IScrapeStrategy strategy,
        Jurisdiction jurisdiction,
        CancellationToken ct)
    {
        var rawResults = await strategy.ScrapeAsync(jurisdiction, ct);

        var existing = await db.TaxRates
            .Where(t => t.JurisdictionId == jurisdiction.Id && t.IsCurrent)
            .ToListAsync(ct);
        foreach (var r in existing) r.IsCurrent = false;

        foreach (var raw in rawResults)
        {
            if (raw.ParsedRate is null) continue;
            db.TaxRates.Add(new TaxRate
            {
                JurisdictionId  = jurisdiction.Id,
                Name            = string.IsNullOrEmpty(raw.RateType) ? "General Sales Tax" : raw.RateType,
                Rate            = raw.ParsedRate.Value,
                RateBasis       = RateBasis.Percentage,
                SaleContext     = SaleContext.Any,
                RemittancePoint = RemittancePoint.Retailer,
                EffectiveDate   = DateOnly.FromDateTime(DateTime.UtcNow),
                ScrapedAt       = DateTime.UtcNow.ToString("o"),
                ScrapeRunId     = run.Id,
                RawEvidence     = raw.RawValue,
                IsCurrent       = true,
                NeedsReview     = false,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateOnly.TryParse(value, out var d) ? d : null;
    }
}
