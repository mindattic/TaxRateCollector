namespace TaxRateCollector.Core.Interfaces;

public interface IScrapeOrchestrator
{
    Task<int> RunFullScrapeAsync(CancellationToken ct = default);
}
