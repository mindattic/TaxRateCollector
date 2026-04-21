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
    string FileName,      // stored filename on disk, e.g. "scraped_20260419_ab12cd34.pdf"
    string EvidenceType,  // "pdf" | "csv" | "xlsx" | "zip" | "txt"
    long SizeBytes);
