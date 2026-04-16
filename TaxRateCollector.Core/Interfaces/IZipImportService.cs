namespace TaxRateCollector.Core.Interfaces;

/// <summary>Progress snapshot reported during a ZIP import run.</summary>
/// <param name="Processed">Records examined so far (new + skipped).</param>
/// <param name="Total">Total new records to process this run.</param>
/// <param name="Imported">Records successfully written to the database.</param>
/// <param name="Skipped">Records skipped (already exist or missing county data).</param>
/// <param name="Errors">Records that caused exceptions.</param>
/// <param name="CurrentZip">ZIP code currently being processed (for display).</param>
public record ZipImportProgress(int Processed, int Total, int Imported, int Skipped, int Errors, string CurrentZip = "");

/// <summary>Final summary returned when an import run completes.</summary>
/// <param name="Total">Total new records processed.</param>
/// <param name="Imported">Records written to the database.</param>
/// <param name="Skipped">Records skipped.</param>
/// <param name="Errors">Records that caused exceptions.</param>
/// <param name="Elapsed">Wall-clock duration of the import.</param>
public record ZipImportResult(int Total, int Imported, int Skipped, int Errors, TimeSpan Elapsed);

public interface IZipImportService
{
    /// <summary>
    /// Imports all US ZIP codes from the Census Bureau ZCTA crosswalk files.
    /// If a USPS API key is configured, city names are enriched via the USPS
    /// CityStateLookup API. Each imported record is linked to its matching
    /// State, County, and City Jurisdiction rows (where seeded).
    ///
    /// The Census crosswalk files are cached locally after first download
    /// to avoid re-downloading on subsequent runs.
    ///
    /// Already-imported ZIP codes are skipped (idempotent).
    /// </summary>
    Task<ZipImportResult> ImportAsync(IProgress<ZipImportProgress>? progress = null, CancellationToken ct = default);

    /// <summary>Returns the number of ZIP code records currently in the database.</summary>
    Task<int> GetImportedCountAsync(CancellationToken ct = default);

    /// <summary>Clears all cached Census download files so next run re-downloads.</summary>
    void ClearCache();
}
