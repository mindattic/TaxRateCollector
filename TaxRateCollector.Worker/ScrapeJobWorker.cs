using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.Worker;

/// <summary>
/// Polls the ScrapeRuns table for rows with Status = Pending, claims each one,
/// then runs <see cref="IRecursiveRateScraper"/> for every active state in parallel.
///
/// The UI queues a job by inserting a ScrapeRun with Status = Pending.
/// Progress is visible to the UI via the ProcessedCount / TotalCount columns updated
/// by the scraper every 10 jurisdictions.
/// </summary>
public sealed class ScrapeJobWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ScrapeJobWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ScrapeJobWorker started — polling every {Interval}s", PollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingRunsAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "ScrapeJobWorker poll cycle failed");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }

        logger.LogInformation("ScrapeJobWorker stopped");
    }

    private async Task ProcessPendingRunsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        await using var ctx = await db.CreateDbContextAsync(ct);

        // Find the oldest pending run.
        var run = await ctx.ScrapeRuns
            .Where(r => r.Status == ScrapeStatus.Pending)
            .OrderBy(r => r.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (run is null) return;

        // Claim it atomically: only the worker that flips Pending → Running proceeds.
        // A conditional UPDATE on Status means a second worker (or instance) that read
        // the same row affects 0 rows and bails, so a run is never processed twice.
        var claimed = await ctx.ScrapeRuns
            .Where(r => r.Id == run.Id && r.Status == ScrapeStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ScrapeStatus.Running), ct);

        if (claimed == 0)
        {
            logger.LogInformation("ScrapeRun #{Id} already claimed by another worker — skipping", run.Id);
            return;
        }

        logger.LogInformation("Claimed ScrapeRun #{Id}", run.Id);

        // Load all active states to scrape
        var states = await ctx.Jurisdictions
            .Where(j => j.JurisdictionType == TaxRateCollector.Core.Enums.JurisdictionType.State && j.IsActive)
            .AsNoTracking()
            .ToListAsync(ct);

        logger.LogInformation("ScrapeRun #{Id}: scraping {Count} states", run.Id, states.Count);

        // Run one state at a time to keep DB contention low.
        // Increase parallelism here once the extractor is live and throughput needs scaling.
        int totalFound = 0, totalCreated = 0, totalEvidence = 0;
        var errors = new List<string>();

        var scraper = scope.ServiceProvider.GetRequiredService<IRecursiveRateScraper>();

        foreach (var state in states)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var report = await scraper.ScrapeAsync(
                    state.Id,
                    new RateScrapeOptions(),
                    ct);

                totalFound    += report.RateLawsFound;
                totalCreated  += report.RateLawsCreated;
                totalEvidence += report.EvidenceDocumentsCaptured;
                errors.AddRange(report.Errors);

                logger.LogInformation(
                    "  {State}: {Found} laws found, {Created} created",
                    state.StateCode, report.RateLawsFound, report.RateLawsCreated);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                var msg = $"{state.StateCode}: {ex.Message}";
                logger.LogWarning(ex, "Scrape failed for state {State}", state.StateCode);
                errors.Add(msg);
            }
        }

        // Final status update — re-fetch the run in a fresh context to avoid stale tracking
        await using var finalCtx = await db.CreateDbContextAsync(CancellationToken.None);
        var finalRun = await finalCtx.ScrapeRuns.FindAsync([run.Id], CancellationToken.None);
        if (finalRun is not null)
        {
            finalRun.Status = errors.Count > 0 && totalCreated == 0
                ? ScrapeStatus.Failed
                : ScrapeStatus.Completed;
            finalRun.CompletedAt = DateTime.UtcNow.ToString("o");
            finalRun.ChangesDetected = totalCreated;
            finalRun.ErrorCount = errors.Count;
            await finalCtx.SaveChangesAsync(CancellationToken.None);
        }

        logger.LogInformation(
            "ScrapeRun #{Id} complete — {Created} laws created, {Errors} errors",
            run.Id, totalCreated, errors.Count);
    }
}
