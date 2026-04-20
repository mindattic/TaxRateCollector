namespace TaxRateCollector.Core.Enums;

/// <summary>
/// Determines which jurisdiction's tax rate applies to an intrastate transaction:
/// the buyer's location (destination) or the seller's location (origin).
///
/// Applies only to intrastate sales. All interstate remote sales are destination-based
/// post-Wayfair (South Dakota v. Wayfair, 2018).
///
/// Origin-based states (12): Arizona, Illinois, Mississippi, Missouri, New Mexico,
/// Ohio, Pennsylvania, Tennessee, Texas, Utah, Virginia, and California (Modified).
/// All remaining states are destination-based.
/// </summary>
public enum SourcingRule
{
    /// <summary>
    /// Tax is based on the buyer's ship-to address (default for 38+ states and all
    /// interstate remote sales). Most SST member states require this.
    /// </summary>
    DestinationBased,

    /// <summary>
    /// Tax is based on the seller's business location for intrastate sales.
    /// The seller applies the tax rate for their own city/county, not the buyer's.
    /// States: AZ, IL, MS, MO, NM, OH, PA, TN, TX, UT, VA (intrastate only).
    /// </summary>
    OriginBased,

    /// <summary>
    /// Hybrid: state and county components are origin-sourced; city and special district
    /// components are destination-sourced. Primarily California.
    /// </summary>
    Modified,
}
