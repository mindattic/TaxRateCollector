using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.Worker;

/// <summary>
/// Runs once per hour. On the 1st of each month, if no scrape has been queued or run
/// for the current month, inserts a Pending ScrapeRun so the ScrapeJobWorker picks it up.
/// </summary>
public sealed class MonthlySchedulerService(
    IServiceScopeFactory scopeFactory,
    ILogger<MonthlySchedulerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TryQueueMonthlyRunAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "MonthlySchedulerService tick failed");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task TryQueueMonthlyRunAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        if (now.Day != 1) return;

        using var scope = scopeFactory.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).ToString("o");
        // Only real scrape runs count toward "already queued this month". Manual rows
        // (the seeder's bootstrap run, UI-entered rates) must not suppress the scheduled job.
        var alreadyQueued = await db.ScrapeRuns
            .AnyAsync(r => r.Status != ScrapeStatus.Manual
                        && string.Compare(r.StartedAt, monthStart, StringComparison.Ordinal) >= 0, ct);

        if (alreadyQueued)
            return;

        db.ScrapeRuns.Add(new ScrapeRun
        {
            StartedAt = now.ToString("o"),
            Status    = ScrapeStatus.Pending,
        });
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Monthly scrape job queued for {Month:yyyy-MM}", now);
    }
}
