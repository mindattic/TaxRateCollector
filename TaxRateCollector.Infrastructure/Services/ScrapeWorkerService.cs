using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.Infrastructure.Services;

public sealed class ScrapeWorkerService(
    ScrapeJobCoordinator coordinator,
    IServiceScopeFactory scopeFactory,
    ILogger<ScrapeWorkerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverOrphanedRunsAsync(stoppingToken);

        await foreach (var job in coordinator.Reader.ReadAllAsync(stoppingToken))
        {
            using var scope       = scopeFactory.CreateScope();
            var orchestrator      = scope.ServiceProvider.GetRequiredService<IScrapeOrchestrator>();
            using var pauseCts    = new CancellationTokenSource();
            using var linkedCts   = CancellationTokenSource.CreateLinkedTokenSource(pauseCts.Token, stoppingToken);

            coordinator.SetRunning(true, pauseCts);
            try
            {
                if (job.Type == ScrapeJobType.StartFull)
                    await orchestrator.RunFullScrapeAsync(linkedCts.Token);
                else if (job.RunId.HasValue)
                    await orchestrator.ResumeAsync(job.RunId.Value, linkedCts.Token);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Scrape paused by user request.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scrape worker encountered an unhandled error.");
            }
            finally
            {
                coordinator.SetRunning(false, null);
            }
        }
    }

    // Any run left in Running state from a previous app session gets moved to Paused
    // so it shows up as resumable in the UI.
    private async Task RecoverOrphanedRunsAsync(CancellationToken ct)
    {
        try
        {
            using var scope  = scopeFactory.CreateScope();
            var dbFactory    = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var orphaned = await db.ScrapeRuns
                .Where(r => r.Status == ScrapeStatus.Running)
                .ToListAsync(ct);

            if (orphaned.Count > 0)
            {
                foreach (var run in orphaned)
                    run.Status = ScrapeStatus.Paused;
                await db.SaveChangesAsync(ct);
                logger.LogWarning("Recovered {Count} orphaned scrape run(s) to Paused.", orphaned.Count);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to recover orphaned scrape runs.");
        }
    }
}
