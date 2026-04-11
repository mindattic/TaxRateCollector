using TaxRateCollector.Core.Enums;

namespace TaxRateCollector.Core.Interfaces;

public interface IDiffEngine
{
    Task<DiffReport> DetectChangesAsync(int scrapeRunId, CancellationToken ct = default);
}

public record DiffReport(
    int TotalCompared,
    IReadOnlyList<RateChange> Changes);

public record RateChange(
    int JurisdictionId,
    ChangeType Type,
    decimal? OldRate,
    decimal? NewRate);
