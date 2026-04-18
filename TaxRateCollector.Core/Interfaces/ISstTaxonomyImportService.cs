namespace TaxRateCollector.Core.Interfaces;

public record SstImportResult(int CategoriesCreated, bool Success, string? Error = null, TimeSpan Elapsed = default);

public interface ISstTaxonomyImportService
{
    /// <summary>
    /// Downloads the SSUTA PDF from the configured URL, extracts Appendix C
    /// defined terms, and populates the TaxCategories table.
    /// Idempotent — clears and rebuilds if called when the table is already populated.
    /// </summary>
    Task<SstImportResult> ImportAsync(IProgress<string>? progress = null, CancellationToken ct = default);

    Task<bool> IsPopulatedAsync();
}
