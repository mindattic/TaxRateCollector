using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.Infrastructure.Seeding;

// Data sources:
//   SST membership — SST Governing Board (https://www.streamlinedsalestax.org/for-businesses/member-states)
//   State rates     — Tax Foundation 2025 State Sales Tax Rates; verified against each state revenue agency
//   Agency names/URLs — official state revenue agency websites
//   Rates as of January 1, 2025; see Notes on individual entries for known rate changes.
public static class StateTaxProfileSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.StateTaxProfiles.AnyAsync()) return;

        db.StateTaxProfiles.AddRange(Profiles);
        await db.SaveChangesAsync();
    }

    private const string AsOf = "2025-01-01T00:00:00Z";

    private static readonly StateTaxProfile[] Profiles =
    [
        P("AL", "Alabama",               0.04m,    false, LocalTaxAuthorityType.Piggyback,
            "Alabama Department of Revenue",
            "https://www.revenue.alabama.gov/sales-use/"),

        P("AK", "Alaska",                0m,       false, LocalTaxAuthorityType.HomeRule,
            "Alaska Department of Revenue",
            "https://tax.alaska.gov",
            "No state sales tax. Boroughs and municipalities may levy their own taxes independently with no state uniformity requirement."),

        P("AZ", "Arizona",               0.056m,   false, LocalTaxAuthorityType.Piggyback,
            "Arizona Department of Revenue",
            "https://azdor.gov/business/transaction-privilege-tax",
            "Arizona levies a Transaction Privilege Tax (TPT) on the privilege of doing business rather than a traditional retail sales tax. Businesses remit on gross receipts."),

        P("AR", "Arkansas",              0.065m,   true,  LocalTaxAuthorityType.SstUniform,
            "Arkansas Department of Finance and Administration",
            "https://www.dfa.arkansas.gov/excise-tax/sales-and-use-tax/"),

        P("CA", "California",            0.0725m,  false, LocalTaxAuthorityType.Piggyback,
            "California Department of Tax and Fee Administration",
            "https://www.cdtfa.ca.gov/industry/sales-use-tax.html",
            "Base state rate of 7.25% (6% state + 1% Local Revenue Fund + 0.25% county). Local district taxes may add up to 3.5% more; statewide average combined rate exceeds 10%."),

        P("CO", "Colorado",              0.029m,   false, LocalTaxAuthorityType.HomeRule,
            "Colorado Department of Revenue",
            "https://tax.colorado.gov/sales-tax",
            "Approximately 70 home-rule municipalities define their own tax bases and self-administer collection independently of the state. State rate of 2.9% is among the lowest in the country."),

        P("CT", "Connecticut",           0.0635m,  false, LocalTaxAuthorityType.Piggyback,
            "Connecticut Department of Revenue Services",
            "https://portal.ct.gov/DRS/Sales-Tax/Sales-and-Use-Tax-Information",
            "No local sales tax; 6.35% applies statewide."),

        P("DE", "Delaware",              0m,       false, LocalTaxAuthorityType.Piggyback,
            "Delaware Division of Revenue",
            "https://revenue.delaware.gov",
            "No state or local general sales tax."),

        P("DC", "District of Columbia",  0.06m,    false, LocalTaxAuthorityType.Piggyback,
            "DC Office of Tax and Revenue",
            "https://otr.cfo.dc.gov/page/sales-and-use-taxes",
            "Single jurisdiction; no sub-district local rates."),

        P("FL", "Florida",               0.06m,    false, LocalTaxAuthorityType.Piggyback,
            "Florida Department of Revenue",
            "https://floridarevenue.com/taxes/taxesfees/Pages/sales_tax.aspx"),

        P("GA", "Georgia",               0.04m,    true,  LocalTaxAuthorityType.SstUniform,
            "Georgia Department of Revenue",
            "https://dor.georgia.gov/taxes/business-taxes/sales-use-tax"),

        P("HI", "Hawaii",                0.04m,    false, LocalTaxAuthorityType.Piggyback,
            "Hawaii Department of Taxation",
            "https://tax.hawaii.gov/taxtype/det/",
            "Hawaii levies a General Excise Tax (GET) at 4% on gross business receipts, not a traditional retail sales tax. County surcharges of 0.5% apply in most counties."),

        P("ID", "Idaho",                 0.06m,    false, LocalTaxAuthorityType.Piggyback,
            "Idaho State Tax Commission",
            "https://tax.idaho.gov/taxes/sales-use/"),

        P("IL", "Illinois",              0.0625m,  false, LocalTaxAuthorityType.Piggyback,
            "Illinois Department of Revenue",
            "https://tax.illinois.gov/research/taxinformation/sales/rot.html",
            "Reduced rate of 1% applies to qualifying food and drugs; 6.25% on general merchandise. Local Retailers' Occupation Tax (ROT) stacks on top."),

        P("IN", "Indiana",               0.07m,    true,  LocalTaxAuthorityType.SstUniform,
            "Indiana Department of Revenue",
            "https://www.in.gov/dor/business-tax/sales-tax/",
            "No local sales tax; 7% applies statewide."),

        P("IA", "Iowa",                  0.06m,    true,  LocalTaxAuthorityType.SstUniform,
            "Iowa Department of Revenue",
            "https://tax.iowa.gov/businesses/sales-use-tax"),

        P("KS", "Kansas",                0.065m,   true,  LocalTaxAuthorityType.SstUniform,
            "Kansas Department of Revenue",
            "https://www.ksrevenue.gov/salesratecurrent.html",
            "State sales tax on groceries phased out: 4% (2023) → 2% (2024) → 0% (Jan 1, 2025). General rate of 6.5% applies to all other items."),

        P("KY", "Kentucky",              0.06m,    true,  LocalTaxAuthorityType.SstUniform,
            "Kentucky Department of Revenue",
            "https://revenue.ky.gov/Business/Sales-Use-Tax/Pages/default.aspx",
            "No local sales tax; 6% applies statewide."),

        P("LA", "Louisiana",             0.0445m,  false, LocalTaxAuthorityType.Independent,
            "Louisiana Department of Revenue",
            "https://revenue.louisiana.gov/SalesTax",
            "Parish and municipal governments have broad independent authority to levy and administer their own sales taxes on their own bases. State rate subject to legislative change; verify for current year."),

        P("ME", "Maine",                 0.055m,   false, LocalTaxAuthorityType.Piggyback,
            "Maine Revenue Services",
            "https://www.maine.gov/revenue/taxes/sales-use-service-provider-tax",
            "No local sales tax; 5.5% applies statewide."),

        P("MD", "Maryland",              0.06m,    false, LocalTaxAuthorityType.Piggyback,
            "Maryland Comptroller",
            "https://www.marylandtaxes.gov/business/sales-use/index.shtml",
            "No local sales tax; 6% applies statewide."),

        P("MA", "Massachusetts",         0.0625m,  false, LocalTaxAuthorityType.Piggyback,
            "Massachusetts Department of Revenue",
            "https://www.mass.gov/info-details/sales-and-use-tax",
            "No local sales tax; 6.25% applies statewide."),

        P("MI", "Michigan",              0.06m,    true,  LocalTaxAuthorityType.SstUniform,
            "Michigan Department of Treasury",
            "https://www.michigan.gov/taxes/business-taxes/sales-use-tax",
            "No local sales tax; 6% applies statewide."),

        P("MN", "Minnesota",             0.06875m, true,  LocalTaxAuthorityType.SstUniform,
            "Minnesota Department of Revenue",
            "https://www.revenue.state.mn.us/sales-and-use-tax"),

        P("MS", "Mississippi",           0.07m,    false, LocalTaxAuthorityType.Piggyback,
            "Mississippi Department of Revenue",
            "https://www.dor.ms.gov/taxes/sales-use/",
            "No local sales tax; 7% applies statewide."),

        P("MO", "Missouri",              0.04225m, false, LocalTaxAuthorityType.Piggyback,
            "Missouri Department of Revenue",
            "https://dor.mo.gov/taxation/business/tax-types/sales-use/",
            "State rate of 4.225% = 4% base + 0.225% supplemental. Combined local rates in some cities exceed 10%."),

        P("MT", "Montana",               0m,       false, LocalTaxAuthorityType.Piggyback,
            "Montana Department of Revenue",
            "https://mtrevenue.gov",
            "No state or local general sales tax."),

        P("NE", "Nebraska",              0.055m,   true,  LocalTaxAuthorityType.SstUniform,
            "Nebraska Department of Revenue",
            "https://revenue.nebraska.gov/businesses/sales-tax"),

        P("NV", "Nevada",                0.0685m,  true,  LocalTaxAuthorityType.SstUniform,
            "Nevada Department of Taxation",
            "https://tax.nv.gov",
            "Base state rate of 6.85% includes mandatory shared county components (BCCRT, SCCRT). Counties may add further local rates on top."),

        P("NH", "New Hampshire",         0m,       false, LocalTaxAuthorityType.Piggyback,
            "New Hampshire Department of Revenue Administration",
            "https://www.revenue.nh.gov",
            "No general sales tax. Specific taxes apply to meals and rooms, motor vehicles, and tobacco."),

        P("NJ", "New Jersey",            0.06625m, true,  LocalTaxAuthorityType.SstUniform,
            "New Jersey Division of Taxation",
            "https://www.njtaxation.org",
            "Urban Enterprise Zone municipalities have a reduced rate of 3.3125% (half the state rate)."),

        P("NM", "New Mexico",            0.05m,    false, LocalTaxAuthorityType.Piggyback,
            "New Mexico Taxation and Revenue Department",
            "https://www.tax.newmexico.gov/businesses/gross-receipts-tax-overview/",
            "New Mexico levies a Gross Receipts Tax (GRT) on business receipts rather than a traditional retail sales tax. Rate varies by location."),

        P("NY", "New York",              0.04m,    false, LocalTaxAuthorityType.Piggyback,
            "New York Department of Taxation and Finance",
            "https://www.tax.ny.gov/bus/st/stidx.htm",
            "State rate is 4%. Combined with mandatory county and NYC/MTA add-ons, effective rates range from 7% to 8.875%."),

        P("NC", "North Carolina",        0.0475m,  true,  LocalTaxAuthorityType.SstUniform,
            "North Carolina Department of Revenue",
            "https://www.ncdor.gov/taxes-forms/sales-and-use-tax"),

        P("ND", "North Dakota",          0.05m,    true,  LocalTaxAuthorityType.SstUniform,
            "North Dakota Office of State Tax Commissioner",
            "https://www.nd.gov/tax/salesanduse/"),

        P("OH", "Ohio",                  0.0575m,  true,  LocalTaxAuthorityType.SstUniform,
            "Ohio Department of Taxation",
            "https://tax.ohio.gov/business/ohio-business-taxes/sales-and-use/introduction"),

        P("OK", "Oklahoma",              0.045m,   true,  LocalTaxAuthorityType.SstUniform,
            "Oklahoma Tax Commission",
            "https://www.tax.ok.gov/"),

        P("OR", "Oregon",                0m,       false, LocalTaxAuthorityType.Piggyback,
            "Oregon Department of Revenue",
            "https://www.oregon.gov/dor",
            "No state or local general sales tax."),

        P("PA", "Pennsylvania",          0.06m,    false, LocalTaxAuthorityType.Piggyback,
            "Pennsylvania Department of Revenue",
            "https://www.revenue.pa.gov/TaxTypes/SUT/Pages/default.aspx",
            "Allegheny County adds 1%; Philadelphia adds 2%, bringing Philadelphia's combined rate to 8%."),

        P("RI", "Rhode Island",          0.07m,    true,  LocalTaxAuthorityType.SstUniform,
            "Rhode Island Division of Taxation",
            "https://tax.ri.gov/tax-sections/sales-excise/sales-use-tax",
            "No local sales tax; 7% applies statewide."),

        P("SC", "South Carolina",        0.06m,    false, LocalTaxAuthorityType.Piggyback,
            "South Carolina Department of Revenue",
            "https://dor.sc.gov/tax/sales"),

        P("SD", "South Dakota",          0.042m,   true,  LocalTaxAuthorityType.SstUniform,
            "South Dakota Department of Revenue",
            "https://dor.sd.gov/businesses/taxes/sales-and-use-tax/",
            "Rate reduced from 4.5% to 4.2% effective July 1, 2023 (HB 1137)."),

        P("TN", "Tennessee",             0.07m,    true,  LocalTaxAuthorityType.SstUniform,
            "Tennessee Department of Revenue",
            "https://www.tn.gov/revenue/taxes/sales-and-use-tax.html",
            "Reduced rate of 4% applies to food and food ingredients; 7% general rate applies to all other items."),

        P("TX", "Texas",                 0.0625m,  false, LocalTaxAuthorityType.Piggyback,
            "Texas Comptroller of Public Accounts",
            "https://comptroller.texas.gov/taxes/sales/",
            "Combined local rate capped at 2% by statute (Tex. Tax Code §321.101). Maximum combined total rate is 8.25%.",
            hasLocalCap: true, localCap: 0.02m),

        P("UT", "Utah",                  0.0485m,  true,  LocalTaxAuthorityType.SstUniform,
            "Utah State Tax Commission",
            "https://tax.utah.gov/sales"),

        P("VT", "Vermont",               0.06m,    true,  LocalTaxAuthorityType.SstUniform,
            "Vermont Department of Taxes",
            "https://tax.vermont.gov/business-and-corp/sales-and-use-tax"),

        P("VA", "Virginia",              0.043m,   false, LocalTaxAuthorityType.Piggyback,
            "Virginia Department of Taxation",
            "https://www.tax.virginia.gov/sales-and-use-tax",
            "State rate 4.3% plus mandatory 1% local add-on = 5.3% statewide minimum. Northern Virginia and Hampton Roads regions add 0.7% more for a combined 6%."),

        P("WA", "Washington",            0.065m,   true,  LocalTaxAuthorityType.SstUniform,
            "Washington Department of Revenue",
            "https://dor.wa.gov/taxes-rates/sales-and-use-tax"),

        P("WV", "West Virginia",         0.06m,    true,  LocalTaxAuthorityType.SstUniform,
            "West Virginia State Tax Department",
            "https://tax.wv.gov/Business/SalesAndUseTax/Pages/SalesandUseTax.aspx"),

        P("WI", "Wisconsin",             0.05m,    true,  LocalTaxAuthorityType.SstUniform,
            "Wisconsin Department of Revenue",
            "https://www.revenue.wi.gov"),

        P("WY", "Wyoming",               0.04m,    true,  LocalTaxAuthorityType.SstUniform,
            "Wyoming Department of Revenue",
            "https://revenue.wyo.gov/tax-types/sales-tax"),
    ];

    private static StateTaxProfile P(
        string stateCode,
        string stateName,
        decimal rate,
        bool isSst,
        LocalTaxAuthorityType localType,
        string agencyName,
        string agencyUrl,
        string? notes = null,
        bool hasLocalCap = false,
        decimal? localCap = null) => new()
        {
            StateCode              = stateCode,
            StateName              = stateName,
            GeneralSalesTaxRate    = rate,
            IsSstMember            = isSst,
            LocalTaxAuthorityType  = localType,
            HasLocalRateCap        = hasLocalCap,
            LocalRateCap           = localCap,
            StateRevenueAgencyName = agencyName,
            StateRevenueUrl        = agencyUrl,
            Notes                  = notes,
            UpdatedAt              = AsOf,
        };
}
