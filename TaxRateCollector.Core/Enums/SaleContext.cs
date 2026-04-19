namespace TaxRateCollector.Core.Enums;

/// <summary>
/// The transaction context in which this rate applies.
/// On-premise vs off-premise is a common distinction for alcohol (bars vs liquor stores).
/// </summary>
public enum SaleContext
{
    Any,
    OnPremise,
    OffPremise,
    Wholesale,
    DirectToConsumer
}
