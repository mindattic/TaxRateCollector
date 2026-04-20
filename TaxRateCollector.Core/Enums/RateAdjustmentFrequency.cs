namespace TaxRateCollector.Core.Enums;

/// <summary>
/// How frequently this tax rate automatically adjusts without requiring new legislation.
/// Indexed/variable rates need more frequent re-scraping than static rates.
///
/// Used primarily for fuel/carbon/environmental excise taxes where the rate is tied to
/// a commodity price index, CPI, or legislative formula rather than a fixed value.
/// </summary>
public enum RateAdjustmentFrequency
{
    /// <summary>Rate is fixed until explicitly changed by legislation. Default for most taxes.</summary>
    Static,

    /// <summary>
    /// Rate adjusts once per year on a fixed date (often Jan 1 or Jul 1).
    /// Examples: New Jersey fuel tax (Jan 1), Illinois fuel tax CPI index (Jul 1).
    /// </summary>
    Annual,

    /// <summary>
    /// Rate recalculates every quarter based on a price index (e.g., average wholesale price).
    /// Examples: Virginia motor fuel (quarterly average wholesale), some state beer excise tiers.
    /// </summary>
    Quarterly,

    /// <summary>
    /// Rate changes monthly. Rare; used by some carbon allowance systems and cap-and-trade programs.
    /// </summary>
    Monthly,
}
