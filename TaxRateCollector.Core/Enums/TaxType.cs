namespace TaxRateCollector.Core.Enums;

/// <summary>
/// Classifies what kind of tax a TaxRate row represents.
/// This drives calculation behaviour: sales/use/occupancy/rental taxes are added to the
/// customer invoice at checkout; excise taxes may already be embedded upstream.
/// Use <see cref="TaxRate.IsIncludedInPrice"/> to determine whether to add a rate to the
/// customer-facing total or treat it as already reflected in the shelf price.
/// </summary>
public enum TaxType
{
    /// <summary>General retail sales tax — collected by the retailer, added to the customer invoice.</summary>
    SalesTax,

    /// <summary>
    /// Use tax — same rate as sales tax; applies when a taxable purchase was made without
    /// collecting sales tax (e.g. out-of-state purchase). Consumer self-remits.
    /// </summary>
    UseTax,

    /// <summary>
    /// Excise tax on specific goods (alcohol, tobacco, cannabis, fuel, firearms, etc.).
    /// May be remitted at the manufacturer, importer, or distributor tier, in which case
    /// it is embedded in the wholesale price before the retailer marks up — see IsIncludedInPrice.
    /// Retailer-level excise (e.g. Chicago restaurant occupation tax) is added at checkout.
    /// </summary>
    ExciseTax,

    /// <summary>Hotel / lodging occupancy tax — collected by the lodging operator at checkout.</summary>
    OccupancyTax,

    /// <summary>Rental car surcharge — collected at point of rental.</summary>
    RentalSurcharge,
}
