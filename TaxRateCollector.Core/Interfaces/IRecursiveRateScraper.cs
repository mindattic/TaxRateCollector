using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;

namespace TaxRateCollector.Core.Interfaces;

/// <summary>
/// Recursively traverses a jurisdiction hierarchy (State → County → City → District)
/// and discovers, extracts, and persists all applicable tax rate laws with evidence.
/// </summary>
public interface IRecursiveRateScraper
{
    Task<RateScrapeReport> ScrapeAsync(
        int rootJurisdictionId,
        RateScrapeOptions options,
        CancellationToken ct = default);
}

public record RateScrapeOptions(
    bool IncludeCounties = true,
    bool IncludeCities = true,
    bool IncludeDistricts = true,
    int? TaxCategoryId = null,
    float MinConfidence = 0.70f,
    bool OverwriteExisting = false);

public record RateScrapeReport(
    int JurisdictionsProcessed,
    int RateLawsFound,
    int RateLawsCreated,
    int EvidenceDocumentsCaptured,
    IReadOnlyList<string> Errors);

/// <summary>
/// AI-powered extractor: given raw content from a government source URL, produces
/// a structured list of tax rate laws with confidence scores.
/// </summary>
public interface IRateLawExtractor
{
    Task<IReadOnlyList<ExtractedRateLaw>> ExtractAsync(
        Jurisdiction jurisdiction,
        string content,
        string mimeType,
        string sourceUrl,
        CancellationToken ct = default);
}

/// <summary>
/// Structured tax rate law extracted from a source document.
/// All fields map 1:1 to TaxRate entity columns.
/// </summary>
public record ExtractedRateLaw(
    string Name,
    decimal Rate,
    RateBasis Basis,
    string Unit,
    SaleContext SaleContext,
    RemittancePoint RemittancePoint,
    decimal? MinAbv,
    decimal? MaxAbv,
    string Conditions,
    string StatutoryReference,
    string EffectiveDate,
    string ExpirationDate,
    int? TaxCategoryId,
    float Confidence,
    string RawEvidence,
    TaxType TaxType = TaxType.SalesTax,
    ProductCategory? ProductCategory = null,
    bool IsCompound = false,
    decimal? MaxTaxableAmount = null,
    bool IsTemporary = false);
