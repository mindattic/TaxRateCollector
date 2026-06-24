namespace TaxRateCollector.Core.Interfaces;

public interface IEvidenceFileStore
{
    /// <summary>
    /// Persists fetched content to the evidence directory and returns file metadata.
    /// HTML  → plain-text file: "Source: &lt;url&gt;\n\n&lt;extracted text&gt;".
    /// PDF / CSV / XLSX → saved directly with the correct extension.
    /// Anything else   → saved as .txt.
    /// </summary>
    Task<StoredEvidenceFile> SaveAsync(
        string sourceUrl,
        byte[] content,
        string mimeType,
        CancellationToken ct = default);
}

public record StoredEvidenceFile(
    string FileName,      // stored filename on disk, e.g. "scraped_ab12cd34ef56.pdf" or "<slug>_ab12cd34ef56.html"
    string EvidenceType,  // "pdf" | "csv" | "xlsx" | "html" | "txt"
    long SizeBytes,
    string ContentHash);  // SHA-256 hex of the bytes actually written to disk (re-hashable)
