using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Scrapers;
using TaxRateCollector.Infrastructure.Services;
using TaxRateCollector.UnitTests.Helpers;

namespace TaxRateCollector.UnitTests.ScraperTests;

/// <summary>
/// Unit tests for general sales-tax bulk scrapers (state + county + city sales/use tax).
/// First fixture covers WisconsinSalesTaxScraper which pulls from two WI DOR FAQ pages:
/// the County Sales Tax FAQ and the Premier Resort Area Tax FAQ.
/// </summary>
[TestFixture]
public class BulkSalesTaxScraperTests
{
    // ── Wisconsin General Sales &amp; Use Tax ──────────────────────────────────

    private const string WiCountyUrl  = "https://www.revenue.wi.gov/Pages/FAQS/pcs-county.aspx";
    private const string WiPremierUrl = "https://www.revenue.wi.gov/Pages/FAQS/pcs-premier.aspx";

    private static readonly string WiCountyHtml = """
        <html><body>
        <h1>County Sales and Use Tax — FAQ</h1>
        <p>The state sales and use tax rate is 5%.</p>
        <p>The county sales and use tax rate is 0.5% in 71 of Wisconsin's 72 counties.</p>
        <p>Milwaukee County imposes a 0.9% county sales and use tax effective January 1, 2024.</p>
        </body></html>
        """;

    private static readonly string WiPremierHtml = """
        <html><body>
        <h1>Premier Resort Area Tax — FAQ</h1>
        <p>The premier resort area tax is imposed under Wis. Stat. § 77.994.</p>
        <ul>
          <li>Village of Bayfield: 0.5%</li>
          <li>City of Eagle River: 0.5%</li>
          <li>Village of Ephraim: 0.5%</li>
          <li>Village of Lake Delton: 1.25% effective January 1, 2014</li>
          <li>Town of Minocqua: 0.5%</li>
          <li>City of Rhinelander: 0.5% effective January 1, 2017</li>
          <li>Village of Sister Bay: 0.5%</li>
          <li>Village of Stockholm: 0.5%</li>
          <li>City of Sturgeon Bay: 0.5%</li>
          <li>City of Wisconsin Dells: 1.25% effective January 1, 2014</li>
        </ul>
        </body></html>
        """;

    private static IStateBulkScraper MakeWiSalesScraper(
        string? countyHtml = null,
        string? premierHtml = null,
        bool wayback = false)
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(WiCountyUrl,  countyHtml  ?? WiCountyHtml);
        handler.Register(WiPremierUrl, premierHtml ?? WiPremierHtml);

        var settings = new SettingsService();
        settings.Current.WaybackMachineFallback = wayback;
        return new WisconsinSalesTaxScraper(new FakeHttpClientFactory(handler), settings);
    }

    [Test]
    public void WiSales_StateCode_IsWI()
    {
        Assert.That(MakeWiSalesScraper().StateCode, Is.EqualTo("WI"));
    }

    [Test]
    public void WiSales_SstCategoryName_IsNull_BecauseGeneralSalesTaxAppliesToAllCategories()
    {
        Assert.That(MakeWiSalesScraper().SstCategoryName, Is.Null);
    }

    [Test]
    public async Task WiSales_Returns_StateRow_WithFips55_AndFivePercent()
    {
        var results = await MakeWiSalesScraper().ScrapeAsync();
        var stateRow = results.FirstOrDefault(r => r.FipsCode == "55");
        Assert.That(stateRow, Is.Not.Null, "Should emit a state-level row with FIPS 55");
        Assert.That(stateRow!.Rate, Is.EqualTo(0.05m), "WI state sales tax is 5%");
        Assert.That(stateRow.TaxType, Is.EqualTo(TaxType.SalesTax));
        Assert.That(stateRow.RateBasis, Is.EqualTo(RateBasis.Percentage));
        Assert.That(stateRow.RemittancePoint, Is.EqualTo(RemittancePoint.Retailer));
        Assert.That(stateRow.ProductCategory, Is.Null,
            "General sales tax must not be tagged with an excise ProductCategory");
    }

    [Test]
    public async Task WiSales_Returns_72_CountyRows()
    {
        var results = await MakeWiSalesScraper().ScrapeAsync();
        var countyRows = results
            .Where(r => r.TaxType == TaxType.SalesTax
                     && r.FipsCode.StartsWith("55")
                     && r.FipsCode.Length == 5)
            .ToList();
        Assert.That(countyRows, Has.Count.EqualTo(72), "Wisconsin has 72 counties");
    }

    [Test]
    public async Task WiSales_Milwaukee_AtPointNinePercent()
    {
        var results = await MakeWiSalesScraper().ScrapeAsync();
        var milwaukee = results.First(r => r.FipsCode == "55079");
        Assert.That(milwaukee.Rate, Is.EqualTo(0.009m), "Milwaukee County sales tax is 0.9%");
        Assert.That(milwaukee.JurisdictionName, Is.EqualTo("Milwaukee"));
    }

    [Test]
    public async Task WiSales_OtherCounties_AtPointFivePercent()
    {
        var results = await MakeWiSalesScraper().ScrapeAsync();
        var dane = results.First(r => r.FipsCode == "55025");
        Assert.That(dane.Rate, Is.EqualTo(0.005m), "Dane County sales tax is 0.5%");

        var brown = results.First(r => r.FipsCode == "55009");
        Assert.That(brown.Rate, Is.EqualTo(0.005m), "Brown County sales tax is 0.5%");
    }

    [Test]
    public async Task WiSales_AllExpectedCounties_AreEmitted()
    {
        var results = await MakeWiSalesScraper().ScrapeAsync();
        // Spot-check first, mid, and last FIPS in alphabetical (numeric) order
        var fipsCodes = results
            .Where(r => r.FipsCode.Length == 5 && r.FipsCode.StartsWith("55"))
            .Select(r => r.FipsCode)
            .ToHashSet();
        Assert.That(fipsCodes, Does.Contain("55001"), "Adams County");
        Assert.That(fipsCodes, Does.Contain("55078"), "Menominee County");
        Assert.That(fipsCodes, Does.Contain("55141"), "Wood County");
    }

    [Test]
    public async Task WiSales_PratRow_LakeDelton_AtElevatedRate()
    {
        var results = await MakeWiSalesScraper().ScrapeAsync();
        var lakeDelton = results.FirstOrDefault(r =>
            r.JurisdictionName.Equals("Lake Delton", StringComparison.OrdinalIgnoreCase));
        Assert.That(lakeDelton, Is.Not.Null, "Lake Delton PRAT row should be emitted");
        Assert.That(lakeDelton!.Rate, Is.EqualTo(0.0125m), "Lake Delton PRAT is 1.25%");
        Assert.That(lakeDelton.FipsCode, Is.EqualTo("5541700"), "Census place FIPS expected");
        Assert.That(lakeDelton.EffectiveDate, Is.EqualTo("2014-01-01"),
            "Effective date should normalize to ISO format");
    }

    [Test]
    public async Task WiSales_PratRow_WisconsinDells_AtElevatedRate()
    {
        var results = await MakeWiSalesScraper().ScrapeAsync();
        var dells = results.FirstOrDefault(r =>
            r.JurisdictionName.Equals("Wisconsin Dells", StringComparison.OrdinalIgnoreCase));
        Assert.That(dells, Is.Not.Null);
        Assert.That(dells!.Rate, Is.EqualTo(0.0125m));
    }

    [Test]
    public async Task WiSales_PratRow_StandardRate_IsHalfPercent()
    {
        var results = await MakeWiSalesScraper().ScrapeAsync();
        var bayfield = results.FirstOrDefault(r =>
            r.JurisdictionName.Equals("Bayfield", StringComparison.OrdinalIgnoreCase)
            && r.RateName.Contains("Premier", StringComparison.OrdinalIgnoreCase));
        Assert.That(bayfield, Is.Not.Null, "Bayfield PRAT row should be emitted");
        Assert.That(bayfield!.Rate, Is.EqualTo(0.005m));
    }

    [Test]
    public async Task WiSales_PratRows_TenJurisdictions_AreEmitted()
    {
        var results = await MakeWiSalesScraper().ScrapeAsync();
        var pratRows = results
            .Where(r => r.RateName.Contains("Premier Resort Area", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.That(pratRows, Has.Count.EqualTo(10),
            "Sample PRAT page lists ten jurisdictions");
    }

    [Test]
    public async Task WiSales_StateRow_CitesSection7752()
    {
        var results = await MakeWiSalesScraper().ScrapeAsync();
        var stateRow = results.First(r => r.FipsCode == "55");
        Assert.That(stateRow.Conditions, Does.StartWith("Statutory authority:"),
            "Per coding convention, Conditions must lead with statute citation");
        Assert.That(stateRow.Conditions, Does.Contain("§ 77.52"),
            "WI state sales tax is imposed under § 77.52");
    }

    [Test]
    public async Task WiSales_CountyRows_CiteSection7771()
    {
        var results = await MakeWiSalesScraper().ScrapeAsync();
        var countyRows = results
            .Where(r => r.FipsCode.Length == 5 && r.FipsCode.StartsWith("55"))
            .ToList();
        foreach (var row in countyRows)
        {
            Assert.That(row.Conditions, Does.StartWith("Statutory authority:"),
                $"County row '{row.JurisdictionName}' must cite statutory authority");
            Assert.That(row.Conditions, Does.Contain("§ 77.71"),
                $"County row '{row.JurisdictionName}' should cite § 77.71");
        }
    }

    [Test]
    public async Task WiSales_PratRows_CiteSection77994()
    {
        var results = await MakeWiSalesScraper().ScrapeAsync();
        var pratRows = results
            .Where(r => r.RateName.Contains("Premier Resort Area", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var row in pratRows)
        {
            Assert.That(row.Conditions, Does.StartWith("Statutory authority:"));
            Assert.That(row.Conditions, Does.Contain("§ 77.994"),
                $"PRAT row '{row.JurisdictionName}' should cite § 77.994");
        }
    }

    [Test]
    public async Task WiSales_AllRows_HaveOfficialConfidence_FromLiveUrls()
    {
        var results = await MakeWiSalesScraper().ScrapeAsync();
        Assert.That(results.All(r => r.SourceConfidence == SourceConfidence.Official), Is.True,
            "When the live DOR URLs respond, SourceConfidence should be Official");
    }

    [Test]
    public async Task WiSales_CountyAndStateRows_ShareCountyEvidence()
    {
        var results = await MakeWiSalesScraper().ScrapeAsync();
        var countyEvidenceUrls = results
            .Where(r => r.FipsCode.Length <= 5)
            .Select(r => r.SourceUrl)
            .Distinct()
            .ToList();
        Assert.That(countyEvidenceUrls, Has.Count.EqualTo(1));
        Assert.That(countyEvidenceUrls[0], Is.EqualTo(WiCountyUrl));
    }

    [Test]
    public async Task WiSales_PratRows_ReferencePremierEvidence()
    {
        var results = await MakeWiSalesScraper().ScrapeAsync();
        var pratUrls = results
            .Where(r => r.RateName.Contains("Premier Resort Area", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.SourceUrl)
            .Distinct()
            .ToList();
        Assert.That(pratUrls, Has.Count.EqualTo(1));
        Assert.That(pratUrls[0], Is.EqualTo(WiPremierUrl));
    }

    [Test]
    public void WiSales_Throws_WhenCountyPageContentNotRecognized()
    {
        var scraper = MakeWiSalesScraper(
            countyHtml: "<html><body>Page under maintenance</body></html>");
        Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());
    }

    [Test]
    public void WiSales_Throws_WhenPremierPageContentNotRecognized()
    {
        var scraper = MakeWiSalesScraper(
            premierHtml: "<html><body>Page under maintenance</body></html>");
        Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());
    }

    [Test]
    public void WiSales_Throws_WhenMilwaukeeRate_NotHigherThanDefault()
    {
        // Both rates parse to 0.5% — validator should reject it as a likely mis-parse
        var html = """
            <html><body>
            <p>State sales and use tax: 5%</p>
            <p>County sales and use tax: 0.5%</p>
            <p>Milwaukee County imposes a 0.5% county sales and use tax.</p>
            </body></html>
            """;
        var scraper = MakeWiSalesScraper(countyHtml: html);
        Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());
    }
}
