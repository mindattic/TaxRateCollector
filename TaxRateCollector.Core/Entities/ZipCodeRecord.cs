namespace TaxRateCollector.Core.Entities;

/// <summary>
/// Lookup table: US ZIP code → primary State / County / City plus any overlapping
/// special taxing districts.
///
/// Rate resolution for a ZIP:
///   1. Resolve StateJurisdictionId + CountyJurisdictionId + CityJurisdictionId
///   2. Sum all IsCurrent TaxRate rows across those three jurisdictions
///   3. Check Districts collection for any additional special-district levies
///   4. Apply ProductCategory modifiers (exempt / reduced / full)
///
/// NOTE: A single ZIP can span multiple special taxing districts simultaneously.
/// The Districts junction collection captures this M:M relationship.
/// </summary>
public class ZipCodeRecord
{
    public string ZipCode { get; set; } = string.Empty;
    public string StateCode { get; set; } = string.Empty;
    public string StateFips { get; set; } = string.Empty;
    public string CountyFips { get; set; } = string.Empty;
    public string CountyName { get; set; } = string.Empty;
    public string PrimaryCity { get; set; } = string.Empty;

    public int? StateJurisdictionId { get; set; }
    public int? CountyJurisdictionId { get; set; }
    public int? CityJurisdictionId { get; set; }

    public string ImportedAt { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Special taxing districts that overlap this ZIP (e.g. RTA, MTA, MPEA, stadium districts).
    /// Many ZIPs are in multiple districts simultaneously — each adds its own levy.
    /// </summary>
    public ICollection<ZipCodeDistrict> Districts { get; set; } = [];
}
