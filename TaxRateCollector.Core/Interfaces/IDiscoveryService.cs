using TaxRateCollector.Core.Entities;

namespace TaxRateCollector.Core.Interfaces;

public interface IDiscoveryService
{
    /// <summary>
    /// Attempt to discover the current tax rate for a single jurisdiction.
    /// Phase 2 will try web scraping, PDF extraction, API calls, and news articles.
    /// Currently returns a stub result.
    /// </summary>
    Task<DiscoveryResult> DiscoverAsync(Jurisdiction jurisdiction, CancellationToken ct = default);
}
