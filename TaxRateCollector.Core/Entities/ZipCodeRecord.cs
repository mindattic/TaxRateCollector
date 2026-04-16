namespace TaxRateCollector.Core.Entities;

/// <summary>
/// Lookup table: US ZIP code → primary State / County / City.
///
/// Used as the first step in sales-tax rate resolution:
///   ZIP → StateJurisdictionId + CountyJurisdictionId + CityJurisdictionId
///       → sum of their current TaxRate rows
///       → apply ProductCategory modifier (exempt / reduced / full)
///
/// County assignment = primary county by land-area intersection (Census crosswalk).
/// City assignment   = USPS preferred city name (CityStateLookup API) or Census place.
///
/// NOTE: ZIP codes are NOT stored in the Jurisdictions table — they are a separate
/// lookup layer that points into the existing State → County → City hierarchy.
/// </summary>
public class ZipCodeRecord
{
    /// <summary>5-digit ZIP code — primary key.</summary>
    public string ZipCode { get; set; } = string.Empty;

    /// <summary>2-letter state abbreviation, e.g. "CA".</summary>
    public string StateCode { get; set; } = string.Empty;

    /// <summary>2-digit state FIPS, e.g. "06".</summary>
    public string StateFips { get; set; } = string.Empty;

    /// <summary>5-digit county FIPS, e.g. "06037" (primary county by land area).</summary>
    public string CountyFips { get; set; } = string.Empty;

    /// <summary>Primary county name, e.g. "Los Angeles County".</summary>
    public string CountyName { get; set; } = string.Empty;

    /// <summary>Primary city/place name per USPS preferred city or Census place, e.g. "BEVERLY HILLS".</summary>
    public string PrimaryCity { get; set; } = string.Empty;

    /// <summary>Id of the matching Jurisdiction row for the state (null if not seeded).</summary>
    public int? StateJurisdictionId { get; set; }

    /// <summary>Id of the matching Jurisdiction row for the county (null if not seeded).</summary>
    public int? CountyJurisdictionId { get; set; }

    /// <summary>Id of the matching Jurisdiction row for the city (null — best-effort name match).</summary>
    public int? CityJurisdictionId { get; set; }

    /// <summary>ISO 8601 UTC timestamp of when this record was imported.</summary>
    public string ImportedAt { get; set; } = string.Empty;

    /// <summary>"Census", "USPS+Census", or "Manual".</summary>
    public string Source { get; set; } = string.Empty;
}
