using TaxRateCollector.Core.Entities;

namespace TaxRateCollector.Core.Interfaces;

public interface IScrapeStrategy
{
    string StrategyKey { get; }
    bool CanHandle(Jurisdiction jurisdiction);
    Task<IReadOnlyList<RawScrapeResult>> ScrapeAsync(
        Jurisdiction jurisdiction,
        CancellationToken ct = default);
}

/// <param name="IsCombined">
/// True when <paramref name="ParsedRate"/> is the source's combined point-of-sale rate
/// (state + local in one figure) rather than the jurisdiction's own portion; stored as
/// <see cref="TaxRate.IsCombinedRate"/> so the TRC-LAW-6 chain sum doesn't double-count it.
/// </param>
public record RawScrapeResult(
    string RawValue,
    decimal? ParsedRate,
    string? RateType,
    string? JurisdictionHint,
    float Confidence,
    bool IsCombined = false);
