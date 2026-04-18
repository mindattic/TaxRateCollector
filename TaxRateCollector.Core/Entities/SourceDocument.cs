using TaxRateCollector.Core.Enums;

namespace TaxRateCollector.Core.Entities;

/// <summary>
/// Stores the raw source material that proves the veracity of a single TaxRate row.
/// One SourceDocument is captured per scrape that produces or refreshes a TaxRate.
/// The raw payload (JSON body or base64-encoded PDF) is stored inline so the rate
/// can be re-verified at any time without making a network call.
/// </summary>
public class SourceDocument
{
    public int Id { get; set; }
    public int TaxRateId { get; set; }

    public SourceType SourceType { get; set; }

    /// <summary>
    /// Full URI that was fetched (API endpoint, PDF URL, CSV download link, etc.).
    /// </summary>
    public string SourceUrl { get; set; } = string.Empty;

    /// <summary>
    /// MIME type of the captured content: "application/json", "application/pdf",
    /// "text/csv", etc.
    /// </summary>
    public string MimeType { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp when this document was fetched/captured, ISO-8601.
    /// </summary>
    public string FetchedAt { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 hex digest of RawContent. Used to detect tampering and to
    /// deduplicate unchanged source documents between scrape runs.
    /// </summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>
    /// For file-based evidence: the filename saved to the evidence folder
    /// (e.g. "01AN4Z07BY79Y3ZT1ZQR3KYF.pdf"). Empty for text/API evidence.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// The original filename as uploaded by the admin (e.g. "Alaska-Rate-2024.pdf").
    /// Used for display in the evidence reader breadcrumb.
    /// </summary>
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>
    /// Evidence type discriminator: "pdf", "txt", "csv", "xlsx", "html", "json",
    /// "url" (external link, no file on disk), or "raw" (inline text in RawContent).
    /// </summary>
    public string EvidenceType { get; set; } = string.Empty;

    /// <summary>
    /// For text/API evidence: the raw content or a short note.
    /// For file evidence: empty (the file lives on disk, identified by FileName).
    /// </summary>
    public string RawContent { get; set; } = string.Empty;

    /// <summary>
    /// Soft-delete flag. Set to false to disassociate evidence without removing
    /// the physical file or the database record — all history is preserved.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public TaxRate TaxRate { get; set; } = null!;
}
