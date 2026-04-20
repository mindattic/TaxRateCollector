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
    Consumer,

    /// <summary>
    /// A marketplace platform (Amazon, Etsy, eBay, Walmart Marketplace, etc.) collects and
    /// remits sales tax on behalf of third-party sellers under marketplace facilitator laws.
    /// Effective in all 45 taxing states + DC post-Wayfair (2018–2019). When RemittancePoint
    /// = MarketplaceFacilitator, the third-party seller has NO collection obligation for
    /// that sale — the platform handles it. Affects accounting treatment and nexus filings.
    /// </summary>
    MarketplaceFacilitator,
}
