using TaxRateCollector.Core.Enums;

namespace TaxRateCollector.Core.Interfaces;

/// <summary>
/// Scraper that downloads a single state-published file and returns rates for
/// all counties and cities in that state in one pass.
/// </summary>
public interface IStateBulkScraper
{
    /// <summary>Two-letter USPS state code this scraper handles (e.g. "TX").</summary>
    string StateCode { get; }

    /// <summary>
    /// SST taxonomy category name (e.g. "Alcoholic Beverages") that general (non-excise)
    /// results from this scraper belong to. Null means the rates are fully general and apply
    /// to all categories. When set, the orchestrator resolves this to a TaxCategoryId and
    /// tags all ProductCategory-null results with it so they only appear under the correct category.
    /// </summary>
    string? SstCategoryName => null;

    Task<IReadOnlyList<BulkRateResult>> ScrapeAsync(CancellationToken ct = default);
}

/// <summary>
/// A single rate row returned by a state bulk scraper.
/// </summary>
public record BulkRateResult(
    /// <summary>
    /// 5-digit county FIPS or 7-digit place FIPS used to match to a Jurisdiction row.
    /// May be empty when only a name match is available.
    /// </summary>
    string FipsCode,

    /// <summary>Jurisdiction name used as a fallback if FIPS doesn't match.</summary>
    string JurisdictionName,

    /// <summary>Display label for the rate (e.g. "State Rate", "County Rate").</summary>
    string RateName,

    decimal Rate,

    /// <summary>URL of the source file that was downloaded.</summary>
    string SourceUrl,

    /// <summary>Raw bytes of the source file (PDF, Excel, CSV, etc.).</summary>
    byte[] EvidenceBytes,

    string EvidenceMimeType,

    /// <summary>Original filename for display (e.g. "tx_sales_tax_rates_q2_2026.xlsx").</summary>
    string EvidenceOriginalFileName,

    string EffectiveDate = "",

    /// <summary>How the Rate value is interpreted (percentage of sale price, per-gallon, etc.).</summary>
    RateBasis RateBasis = RateBasis.Percentage,

    /// <summary>Kind of tax (sales, excise, etc.).</summary>
    TaxType TaxType = TaxType.SalesTax,

    /// <summary>Where in the supply chain this tax is collected and remitted.</summary>
    RemittancePoint RemittancePoint = RemittancePoint.Retailer,

    /// <summary>True when the tax is remitted upstream and already embedded in the wholesale cost.</summary>
    bool IsIncludedInPrice = false,

    /// <summary>On-premise, off-premise, or any context for this rate.</summary>
    SaleContext SaleContext = SaleContext.Any,

    /// <summary>Minimum ABV (decimal fraction, e.g. 0.20 = 20%) required for this rate. Null = no lower bound.</summary>
    decimal? MinAbv = null,

    /// <summary>Maximum ABV (decimal fraction) above which this rate does not apply. Null = no upper bound.</summary>
    decimal? MaxAbv = null,

    /// <summary>How confident we are in the data source. Official = live government URL; Archive = Wayback Machine snapshot.</summary>
    SourceConfidence SourceConfidence = SourceConfidence.Official,

    /// <summary>
    /// Excise product category (Beer, Wine, Spirits, etc.). Null for general sales-tax rates.
    /// When set, the rate appears in the excise-rates panel rather than the main rates panel.
    /// </summary>
    ProductCategory? ProductCategory = null,

    /// <summary>Free-text conditions note (e.g. control-state explanation, exemption reason).</summary>
    string Conditions = "");
