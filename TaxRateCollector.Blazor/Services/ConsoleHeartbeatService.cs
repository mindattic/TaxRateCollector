using Microsoft.Extensions.Hosting;

namespace TaxRateCollector.Blazor.Services;

/// <summary>
/// Writes a numbered "[tick]" line to stdout every fifteen seconds so a human
/// watching the console knows the host is alive. Carries no application logic —
/// purely a liveness signal for development sessions.
/// </summary>
public sealed class ConsoleHeartbeatService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tick = 1;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(15_000, stoppingToken); }
            catch (OperationCanceledException) { break; }

            Console.WriteLine($"[tick] {tick++}");
        }
    }
}
