namespace TaxRateCollector.Core.Enums;

public enum ChangeType
{
    RateChanged,
    NewJurisdiction,
    Removed,
    /// <summary>
    /// The decimal rate value is unchanged but one or more structural fields changed:
    /// RateBasis, TaxType, IsCompound, IsIncludedInPrice, or RemittancePoint.
    /// OldRate / NewRate will both equal the current rate; see ChangeDescription for detail.
    /// </summary>
    StructuralChange
}
