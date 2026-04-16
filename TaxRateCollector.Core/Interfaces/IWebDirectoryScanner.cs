namespace TaxRateCollector.Core.Interfaces;

/// <summary>A file or subdirectory discovered in an HTTP directory listing.</summary>
/// <param name="Url">Absolute URL of the entry.</param>
/// <param name="Name">Bare filename or directory name (no path).</param>
/// <param name="IsDirectory">True when the entry is a subdirectory.</param>
/// <param name="LastModified">Raw last-modified string from the listing, if present.</param>
public sealed record WebDirectoryEntry(
    string Url,
    string Name,
    bool IsDirectory,
    string? LastModified = null);

/// <summary>
/// Discovers files on servers that publish Apache-style (or similar) HTTP directory
/// listings — pages that render links to files and subdirectories.
///
/// Typical use: find the latest Census Gazetteer ZIP, locate a state tax-rate CSV
/// that moves around year to year, or recursively index a documentation site.
///
/// All methods accept a glob pattern where:
///   *  matches any characters except '/'
///   ** matches any characters including '/'
///   ?  matches exactly one character
/// Patterns are matched against the bare filename only (not the full URL path).
/// </summary>
public interface IWebDirectoryScanner
{
    /// <summary>
    /// Fetches <paramref name="directoryUrl"/> and returns every link that points
    /// to a child file or subdirectory (parent links and external URLs are excluded).
    /// </summary>
    Task<IReadOnlyList<WebDirectoryEntry>> ListAsync(
        string directoryUrl,
        CancellationToken ct = default);

    /// <summary>
    /// Lists <paramref name="directoryUrl"/>, filters entries whose names match
    /// <paramref name="fileGlob"/>, sorts the matches by name descending, and
    /// returns the URL of the top result — useful for "find the latest year" patterns.
    /// Returns <c>null</c> if nothing matches.
    /// </summary>
    Task<string?> FindLatestUrlAsync(
        string directoryUrl,
        string fileGlob,
        CancellationToken ct = default);

    /// <summary>
    /// Recursively walks the directory tree rooted at <paramref name="rootUrl"/>
    /// (up to <paramref name="maxDepth"/> levels deep) and returns all file URLs
    /// whose names match <paramref name="fileGlob"/>.
    /// Directories that return errors are silently skipped.
    /// </summary>
    Task<IReadOnlyList<string>> FindAllAsync(
        string rootUrl,
        string fileGlob,
        int maxDepth = 3,
        CancellationToken ct = default);
}
