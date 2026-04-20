namespace TaxRateCollector.Core.Enums;

public enum RateBasis
{
    /// <summary>Decimal fraction of the retail sale price (6.5% → 0.065).</summary>
    Percentage,

    /// <summary>Dollar amount per unit (e.g., $0.231 per cigarette pack).</summary>
    FlatPerUnit,

    /// <summary>Dollar amount per volume unit (e.g., $1.07 per gallon of beer).</summary>
    FlatPerVolume,

    /// <summary>Dollar amount per weight unit (e.g., $1.20 per ounce of tobacco).</summary>
    FlatPerWeight,

    /// <summary>Dollar amount per proof gallon (distilled spirits federal/state excise).</summary>
    FlatPerProofGallon,

    /// <summary>
    /// Decimal fraction of the wholesale/manufacturer's price, not the retail price.
    /// Used for OTP (Other Tobacco Products) taxes in most states: cigars, snuff, pipe tobacco.
    /// Examples: Rhode Island 80% of wholesale, New York 75% of wholesale (cigars).
    /// Federal large cigar excise: 52.75% of manufacturer's sale price.
    /// When this basis is used, Rate is applied against the wholesale cost, not the sale price.
    /// </summary>
    PercentageOfWholesale,
}
