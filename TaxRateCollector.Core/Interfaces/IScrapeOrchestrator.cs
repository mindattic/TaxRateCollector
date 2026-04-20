namespace TaxRateCollector.Core.Interfaces;

public interface IScrapeOrchestrator
{
    Task<int> RunFullScrapeAsync(CancellationToken ct = default);
    Task<int> ResumeAsync(int scrapeRunId, CancellationToken ct = default);
    Task<int?> GetPausedRunIdAsync(CancellationToken ct = default);
}
