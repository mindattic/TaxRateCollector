namespace TaxRateCollector.Core.Billing;

/// <summary>
/// Subscription billing arithmetic shared by the Subscribe page and its tests. Money that
/// is charged (sent to PayPal and stored on a BillingRecord) is whole cents: the tax is
/// rounded at the final amount, half a cent away from zero.
/// </summary>
public static class BillingMath
{
    public static decimal RoundToCents(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    /// <summary>Sales tax on the monthly price, rounded to cents.</summary>
    public static decimal TaxAmount(decimal pricePerMonth, decimal taxRate) =>
        RoundToCents(pricePerMonth * taxRate);

    /// <summary>Monthly price plus its rounded tax, in cents.</summary>
    public static decimal Total(decimal pricePerMonth, decimal taxRate) =>
        RoundToCents(pricePerMonth + TaxAmount(pricePerMonth, taxRate));
}
