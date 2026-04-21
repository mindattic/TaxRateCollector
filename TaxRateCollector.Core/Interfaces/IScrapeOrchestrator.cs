namespace TaxRateCollector.Core.Interfaces;

public interface IScrapeOrchestrator
{
    Task<int> RunFullScrapeAsync(CancellationToken ct = default);
    Task<int> ResumeAsync(int scrapeRunId, CancellationToken ct = default);
    Task<int?> GetPausedRunIdAsync(CancellationToken ct = default);
    Task<int> RunBulkForStateAsync(string stateCode, int? taxCategoryId = null, bool needsReview = false, CancellationToken ct = default);
}
