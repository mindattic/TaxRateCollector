using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxRateCollector.Core.Interfaces;

namespace TaxRateCollector.Infrastructure.Services;

public class ScrapeSchedulerService(
    IServiceScopeFactory scopeFactory,
    ILogger<ScrapeSchedulerService> logger) : BackgroundService
{
    private readonly TimeSpan interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small delay so the app fully starts before first scrape
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IScrapeOrchestrator>();

            try
            {
                logger.LogInformation("Scheduled scrape starting at {Time}", DateTime.UtcNow);
                await orchestrator.RunFullScrapeAsync(stoppingToken);
                logger.LogInformation("Scheduled scrape completed at {Time}", DateTime.UtcNow);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled scrape cycle failed");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
