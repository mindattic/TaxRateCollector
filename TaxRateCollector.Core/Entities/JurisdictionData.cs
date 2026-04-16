using System.Text.Json.Serialization;
using TaxRateCollector.Core.Interfaces;

namespace TaxRateCollector.Core.Entities;

/// <summary>
/// A single node in the jurisdiction hierarchy: Country → State → County → City.
/// Each jurisdiction carries its own tax rate sourced from an independent API or document.
/// Taxes are cumulative — a purchase location resolves the full chain and sums all tiers.
/// Every row stores a copy of the source record (API response or PDF) for audit.
/// </summary>
public class JurisdictionData : ICanonEntity
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.CreateVersion7().ToString("N");
    [JsonPropertyName("type")] public string Type { get; set; } = "jurisdiction";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("aliases")] public List<string> Aliases { get; set; } = [];
    [JsonPropertyName("description")] public string Description { get; set; } = "";

    /// <summary>Hierarchy tier: country, state, county, or city.</summary>
    [JsonPropertyName("tier")] public string Tier { get; set; } = "";

    /// <summary>Id of the parent jurisdiction (empty for top-level country).</summary>
    [JsonPropertyName("parent_id")] public string ParentId { get; set; } = "";

    /// <summary>FIPS code for US jurisdictions (state: 2-digit, county: 5-digit, city: 7-digit).</summary>
    [JsonPropertyName("fips_code")] public string FipsCode { get; set; } = "";

    /// <summary>ISO 3166 code for countries (2-letter) and subdivisions (e.g., US-IL).</summary>
    [JsonPropertyName("iso_code")] public string IsoCode { get; set; } = "";

    /// <summary>Population of this jurisdiction, for rate-tier lookups.</summary>
    [JsonPropertyName("population")] public long Population { get; set; }

    /// <summary>Primary tax rate for this jurisdiction tier.</summary>
    [JsonPropertyName("tax_rate")] public JurisdictionTaxRate TaxRate { get; set; } = new();

    /// <summary>Additional levies beyond the primary rate (special districts, surcharges, etc.).</summary>
    [JsonPropertyName("additional_rates")] public List<TaxRateEntry> AdditionalRates { get; set; } = [];

    /// <summary>Where this rate came from and the cached source document for audit.</summary>
    [JsonPropertyName("source")] public TaxSourceProvenance Source { get; set; } = new();

    /// <summary>Date this rate became effective (ISO 8601).</summary>
    [JsonPropertyName("effective_date")] public string EffectiveDate { get; set; } = "";

    /// <summary>Date this rate expires or must be re-verified (ISO 8601).</summary>
    [JsonPropertyName("expiration_date")] public string ExpirationDate { get; set; } = "";

    /// <summary>Timestamp of the last successful refresh from the source (ISO 8601).</summary>
    [JsonPropertyName("last_updated")] public string LastUpdated { get; set; } = "";

    /// <summary>How often (in days) this tier's source should be re-queried.</summary>
    [JsonPropertyName("update_frequency_days")] public int UpdateFrequencyDays { get; set; } = 90;

    /// <summary>Whether this jurisdiction is currently active for tax calculations.</summary>
    [JsonPropertyName("is_active")] public bool IsActive { get; set; } = true;

    [JsonPropertyName("tags")] public List<string> Tags { get; set; } = [];
}

/// <summary>Tax rate for a single jurisdiction tier.</summary>
public class JurisdictionTaxRate
{
    /// <summary>Rate value — percentage expressed as a decimal (e.g., 6.25 means 6.25%).</summary>
    [JsonPropertyName("rate")] public double Rate { get; set; }

    /// <summary>How the rate is applied: "percentage" or "flat".</summary>
    [JsonPropertyName("rate_type")] public string RateType { get; set; } = "percentage";

    /// <summary>Tax category: sales, use, excise, vat, gst, etc.</summary>
    [JsonPropertyName("category")] public string Category { get; set; } = "sales";

    /// <summary>Human-readable label (e.g., "Illinois State Sales Tax").</summary>
    [JsonPropertyName("label")] public string Label { get; set; } = "";
}

/// <summary>
/// An additional named tax levy within a jurisdiction (special district,
/// transit surcharge, stadium tax, etc.).
/// </summary>
public class TaxRateEntry
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("rate")] public double Rate { get; set; }
    [JsonPropertyName("rate_type")] public string RateType { get; set; } = "percentage";
    [JsonPropertyName("category")] public string Category { get; set; } = "sales";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
}

/// <summary>
/// Provenance metadata and cached source document for a jurisdiction's tax rate.
/// Each tier's rate comes from a different API or document. The raw source is stored
/// on every row so the rate can be independently verified at any time without a
/// network call. When the source is a website, a PDF print or full-page screenshot
/// is captured and stored as the evidence artifact.
/// </summary>
public class TaxSourceProvenance
{
    /// <summary>Source type: api, pdf, csv, website, manual.</summary>
    [JsonPropertyName("source_type")] public string SourceType { get; set; } = "";

    /// <summary>The API endpoint URL, document download URL, or authoritative web page URL.</summary>
    [JsonPropertyName("source_uri")] public string SourceUri { get; set; } = "";

    /// <summary>Timestamp when the source was last fetched (ISO 8601).</summary>
    [JsonPropertyName("retrieved_at")] public string RetrievedAt { get; set; } = "";

    /// <summary>SHA-256 hash of the raw response for integrity verification.</summary>
    [JsonPropertyName("document_hash")] public string DocumentHash { get; set; } = "";

    /// <summary>MIME type of the cached document (application/json, application/pdf, text/csv, text/html, etc.).</summary>
    [JsonPropertyName("content_type")] public string ContentType { get; set; } = "";

    /// <summary>
    /// The full cached source record. For API responses, this is the raw JSON body.
    /// For PDF documents or page captures, this is the base64-encoded binary content.
    /// For CSV sources, this is the raw CSV text.
    /// </summary>
    [JsonPropertyName("raw_response")] public string RawResponse { get; set; } = "";

    /// <summary>Human-readable notes (e.g., "IRS Publication 1234, Table 3; page captured 2026-04-15").</summary>
    [JsonPropertyName("notes")] public string Notes { get; set; } = "";
}

/// <summary>
/// Cumulative tax assessment for a specific point-of-sale location.
/// Walks the jurisdiction chain from the most specific tier (city) up to
/// the country and sums all applicable rates into a combined total.
/// </summary>
public class TaxAssessment
{
    /// <summary>Human-readable name of the assessed location (e.g., "Chicago, Cook County, IL, US").</summary>
    [JsonPropertyName("location_name")] public string LocationName { get; set; } = "";

    /// <summary>Timestamp when this assessment was computed (ISO 8601).</summary>
    [JsonPropertyName("assessed_at")] public string AssessedAt { get; set; } = "";

    /// <summary>Ordered breakdown of each tier's contribution, from most specific to broadest.</summary>
    [JsonPropertyName("line_items")] public List<TaxLineItem> LineItems { get; set; } = [];

    /// <summary>Sum of all tier rates — the total tax percentage at this location.</summary>
    [JsonPropertyName("combined_rate")] public double CombinedRate { get; set; }

    /// <summary>Jurisdiction IDs from the most specific tier up to the country.</summary>
    [JsonPropertyName("jurisdiction_chain")] public List<string> JurisdictionChain { get; set; } = [];
}

/// <summary>
/// One line in a cumulative tax breakdown, representing a single
/// jurisdiction tier's contribution to the total.
/// </summary>
public class TaxLineItem
{
    [JsonPropertyName("jurisdiction_id")] public string JurisdictionId { get; set; } = "";
    [JsonPropertyName("jurisdiction_name")] public string JurisdictionName { get; set; } = "";
    [JsonPropertyName("tier")] public string Tier { get; set; } = "";
    [JsonPropertyName("rate")] public double Rate { get; set; }
    [JsonPropertyName("rate_type")] public string RateType { get; set; } = "percentage";
    [JsonPropertyName("category")] public string Category { get; set; } = "sales";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
}
