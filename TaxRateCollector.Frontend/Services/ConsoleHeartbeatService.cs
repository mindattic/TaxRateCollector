using Microsoft.Extensions.Hosting;

namespace TaxRateCollector.Frontend.Services;

public sealed class ConsoleHeartbeatService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tick = 1;
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(15_000, stoppingToken).ContinueWith(_ => { });
            if (!stoppingToken.IsCancellationRequested)
                Console.WriteLine($"[tick] {tick++}");
        }
    }
}
