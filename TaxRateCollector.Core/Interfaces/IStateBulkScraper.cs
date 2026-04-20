namespace TaxRateCollector.Core.Interfaces;

/// <summary>
/// Scraper that downloads a single state-published file and returns rates for
/// all counties and cities in that state in one pass.
/// </summary>
public interface IStateBulkScraper
{
    /// <summary>Two-letter USPS state code this scraper handles (e.g. "TX").</summary>
    string StateCode { get; }

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

    string EffectiveDate = "");
