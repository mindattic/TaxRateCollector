namespace TaxRateCollector.Core.Interfaces;

/// <summary>Progress snapshot during a Census jurisdiction import.</summary>
/// <param name="Stage">"Counties", "Cities", or "Re-linking ZIPs".</param>
/// <param name="Processed">Records examined so far this stage.</param>
/// <param name="Total">Total records expected this stage (0 = unknown).</param>
/// <param name="Created">New Jurisdiction rows inserted.</param>
/// <param name="Skipped">Records skipped (already exist by FipsCode).</param>
/// <param name="Errors">Records that caused exceptions.</param>
/// <param name="Current">Name of item currently being processed.</param>
public record CensusImportProgress(
    string Stage, int Processed, int Total,
    int Created, int Skipped, int Errors,
    string Current = "");

/// <summary>Final summary returned when import completes.</summary>
public record CensusImportResult(
    int CountiesCreated, int CitiesCreated, int ZipsRelinked,
    int CountiesSkipped, int CitiesSkipped,
    TimeSpan Elapsed);

/// <summary>
/// Imports all US counties (~3,143) and incorporated places / CDPs (~30,000+)
/// from the Census Bureau's public Gazetteer and relationship files.
///
/// After importing, re-links every ZipCodeRecord to the correct
/// CountyJurisdictionId and CityJurisdictionId so that ZIP-based tax
/// resolution works for all addresses in the US.
///
/// All data sources are free, require no authentication, and are cached
/// locally after the first download.
/// </summary>
public interface ICensusJurisdictionImportService
{
    /// <summary>
    /// Runs the full import pipeline:
    ///   1. Download + import all US counties → Jurisdiction rows (County tier)
    ///   2. Download + import all US cities/places → Jurisdiction rows (City tier)
    ///   3. Re-link all ZipCodeRecord rows to the new County/City jurisdiction IDs
    /// Idempotent — existing rows (matched by FipsCode) are skipped.
    /// </summary>
    Task<CensusImportResult> ImportAsync(
        IProgress<CensusImportProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>Returns county and city coverage counts for the dashboard.</summary>
    Task<(int SeededCounties, int SeededCities, int LinkedZips)> GetCoverageAsync(
        CancellationToken ct = default);

    /// <summary>Deletes cached Census download files so next run re-downloads.</summary>
    void ClearCache();
}
