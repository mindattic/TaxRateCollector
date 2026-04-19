namespace TaxRateCollector.Core.Enums;

/// <summary>
/// Where in the supply chain this tax is collected and remitted to the government.
/// Excise taxes on alcohol and tobacco are typically remitted by the Distributor under
/// the 3-tier system, not by the retailer — the tax is embedded in the wholesale price.
/// </summary>
public enum RemittancePoint
{
    Manufacturer,
    Importer,
    Distributor,
    Retailer,
    Consumer
}
