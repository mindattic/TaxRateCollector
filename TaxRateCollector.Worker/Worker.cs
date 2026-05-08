namespace TaxRateCollector.Worker;

/// <summary>
/// Default scaffolded heartbeat <see cref="BackgroundService"/> — emits an Information
/// log entry every second so the host stays observable. Real scrape work runs on
/// <see cref="ScrapeJobWorker"/>.
/// </summary>
public class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            }
            await Task.Delay(1000, stoppingToken);
        }
    }
}
