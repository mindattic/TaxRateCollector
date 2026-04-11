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

public record RawScrapeResult(
    string RawValue,
    decimal? ParsedRate,
    string? RateType,
    string? JurisdictionHint,
    float Confidence);
