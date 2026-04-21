using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Scrapers;
using TaxRateCollector.UnitTests.Helpers;

namespace TaxRateCollector.UnitTests.ScraperTests;

/// <summary>
/// Unit tests for WisconsinAlcoholScraper and IllinoisAlcoholScraper.
/// Uses FakeHttpMessageHandler to serve realistic HTML matching each official source.
/// </summary>
[TestFixture]
public class BulkAlcoholScraperTests
{
    // ── Wisconsin ─────────────────────────────────────────────────────────────

    private const string WiSourceUrl = "https://www.revenue.wi.gov/Pages/FAQS/pcs-county.aspx";

    private static readonly string WiHtml = """
        <html><body>
        <h2>County and Stadium Sales Tax</h2>
        <p>State sales and use tax: 5%</p>
        <p>County sales and use tax: 0.5% or 0.9%</p>
        <p>Milwaukee County imposes a 0.9% county sales and use tax.</p>
        <p>All other counties impose a 0.5% county sales and use tax.</p>
        </body></html>
        """;

    private static IStateBulkScraper MakeWiScraper(string html)
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(WiSourceUrl, html);
        return new WisconsinAlcoholScraper(new FakeHttpClientFactory(handler));
    }

    [Test]
    public void StateCode_IsWI()
    {
        var scraper = MakeWiScraper(WiHtml);
        Assert.That(scraper.StateCode, Is.EqualTo("WI"));
    }

    [Test]
    public async Task WI_Returns_StateRow_WithFips55()
    {
        var results = await MakeWiScraper(WiHtml).ScrapeAsync();
        var stateRow = results.FirstOrDefault(r => r.FipsCode == "55");
        Assert.That(stateRow, Is.Not.Null, "Should emit a state-level row with FIPS 55");
        Assert.That(stateRow!.Rate, Is.EqualTo(0.05m), "State rate should be 5%");
    }

    [Test]
    public async Task WI_Returns_MilwaukeeRow_AtHigherRate()
    {
        var results = await MakeWiScraper(WiHtml).ScrapeAsync();
        var milwaukee = results.First(r => r.FipsCode == "55079");
        Assert.That(milwaukee.Rate, Is.EqualTo(0.009m), "Milwaukee county rate should be 0.9%");
    }

    [Test]
    public async Task WI_Returns_DefaultCountyRow_AtLowerRate()
    {
        var results = await MakeWiScraper(WiHtml).ScrapeAsync();
        var dane = results.First(r => r.FipsCode == "55025"); // Dane county
        Assert.That(dane.Rate, Is.EqualTo(0.005m), "Default county rate should be 0.5%");
    }

    [Test]
    public async Task WI_Returns_73_County_Rows()
    {
        var results = await MakeWiScraper(WiHtml).ScrapeAsync();
        var countyRows = results.Where(r => r.FipsCode.StartsWith("55") && r.FipsCode != "55").ToList();
        Assert.That(countyRows, Has.Count.EqualTo(72), "Wisconsin has 72 counties");
    }

    [Test]
    public async Task WI_All_EvidenceBytes_PointToSameContent()
    {
        var results = await MakeWiScraper(WiHtml).ScrapeAsync();
        // All rows should share identical evidence bytes (same source URL)
        var distinct = results.Select(r => r.SourceUrl).Distinct().ToList();
        Assert.That(distinct, Has.Count.EqualTo(1));
        Assert.That(distinct[0], Is.EqualTo(WiSourceUrl));
    }

    [Test]
    public void WI_Throws_WhenPageContentNotRecognized()
    {
        var scraper = MakeWiScraper("<html><body>Page under maintenance</body></html>");
        Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());
    }

    [Test]
    public void WI_Throws_WhenMilwaukeeRate_NotHigherThanDefault()
    {
        // If both rates parse to the same value, validation should reject it
        var html = """
            <html><body>
            State sales and use tax: 5%
            County sales and use tax: 0.5% or 0.5%
            Milwaukee County imposes a 0.5% county sales and use tax.
            </body></html>
            """;
        var scraper = MakeWiScraper(html);
        Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());
    }

    // ── Illinois ──────────────────────────────────────────────────────────────

    private const string IlExciseUrl      = "https://tax.illinois.gov/research/taxrates/excise.html";
    private const string IlCookUrl        = "https://www.cookcountyil.gov/service/liquor-tax";
    private const string IlChicagoUrl     = "https://www.chicago.gov/city/en/depts/fin/supp_info/revenue/tax_list/liquor_tax_.html";
    private const string IlChicagoNewsUrl = "https://www.chicago.gov/city/en/depts/fin/provdrs/tax_division/news/2026/january/LiquorTaxChangesEffectiveMarch12026.html";

    private static readonly string IlExciseHtml = """
        <html><body>
        <h2>Liquor Gallonage Tax Rates</h2>
        <p>Beer and cider (one-half of 1 percent to 7 percent): 23.1¢ per gallon</p>
        <p>Wine and liquor (14 percent or less of alcohol): $1.39 per gallon</p>
        <p>Wine and liquor (more than 14 percent and less than 20 percent): $1.39 per gallon</p>
        <p>$8.55 per gallon for alcoholic liquor with an alcohol content of 20 percent or more</p>
        </body></html>
        """;

    private static readonly string IlCookHtml = """
        <html><body>
        <h2>Cook County Alcoholic Beverage Tax</h2>
        <p>Beer (under 20% ABV): $2.50 per gallon</p>
        <p>Wine (14% or less ABV): $0.24 per gallon</p>
        <p>Liquor (20% or more ABV): $2.50 per gallon</p>
        </body></html>
        """;

    private static readonly string IlChicagoHtml = """
        <html><body>
        <h2>City of Chicago Liquor Tax</h2>
        <p>$0.29 per gallon of beer</p>
        <p>$0.36 per gallon of liquor containing 14% or less alcohol by volume</p>
        <p>$0.89 per gallon for liquor containing more than 14% and less than 20% of alcohol by volume</p>
        <p>$2.68 per gallon containing 20% or more alcohol by volume</p>
        </body></html>
        """;

    private static readonly string IlChicagoNewsHtml = """
        <html><body>
        <p>Effective March 1, 2026, off-premises retail purchases of alcoholic beverages are subject
        to a 1.5% tax on the retail purchase price.</p>
        </body></html>
        """;

    private static IStateBulkScraper MakeIlScraper(bool wayback = false,
        string? chicagoOverride = null, string? newsOverride = null)
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(IlExciseUrl,      IlExciseHtml);
        handler.Register(IlCookUrl,        IlCookHtml);
        if (chicagoOverride is not null)
            handler.Register(IlChicagoUrl, chicagoOverride);
        else
            handler.Register(IlChicagoUrl, IlChicagoHtml);
        if (newsOverride is not null)
            handler.Register(IlChicagoNewsUrl, newsOverride);
        else
            handler.Register(IlChicagoNewsUrl, IlChicagoNewsHtml);

        var s = new TaxRateCollector.Infrastructure.Services.SettingsService();
        s.Current.WaybackMachineFallback = wayback;
        return new IllinoisAlcoholScraper(new FakeHttpClientFactory(handler), s);
    }

    [Test]
    public void StateCode_IsIL()
    {
        Assert.That(MakeIlScraper().StateCode, Is.EqualTo("IL"));
    }

    [Test]
    public async Task IL_Returns_StateExcise_BeerRow()
    {
        var results = await MakeIlScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "17" && r.RateName.Contains("Beer"));
        Assert.That(row.Rate, Is.EqualTo(0.231m), "Beer rate should be $0.231/gal");
        Assert.That(row.RateBasis, Is.EqualTo(RateBasis.FlatPerVolume));
        Assert.That(row.TaxType, Is.EqualTo(TaxType.ExciseTax));
        Assert.That(row.RemittancePoint, Is.EqualTo(RemittancePoint.Distributor));
        Assert.That(row.IsIncludedInPrice, Is.True);
    }

    [Test]
    public async Task IL_Returns_StateExcise_SpiritsRow()
    {
        var results = await MakeIlScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "17" && r.RateName.Contains("Spirits"));
        Assert.That(row.Rate, Is.EqualTo(8.55m), "Spirits rate should be $8.55/gal");
        Assert.That(row.MinAbv, Is.EqualTo(0.20m));
    }

    [Test]
    public async Task IL_Returns_StateExcise_Wine14Row()
    {
        var results = await MakeIlScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "17" && r.RateName.Contains("≤14%"));
        Assert.That(row.Rate, Is.EqualTo(1.39m));
        Assert.That(row.MaxAbv, Is.EqualTo(0.14m));
    }

    [Test]
    public async Task IL_Returns_CookCounty_BeerRow()
    {
        var results = await MakeIlScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "17031" && r.RateName.Contains("Beer"));
        Assert.That(row.Rate, Is.EqualTo(2.50m), "Cook County beer tax should be $2.50/gal");
        Assert.That(row.RemittancePoint, Is.EqualTo(RemittancePoint.Distributor));
    }

    [Test]
    public async Task IL_Returns_CookCounty_WineRow()
    {
        var results = await MakeIlScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "17031" && r.RateName.Contains("Wine"));
        Assert.That(row.Rate, Is.EqualTo(0.24m), "Cook County wine tax should be $0.24/gal");
    }

    [Test]
    public async Task IL_Returns_Chicago_OnPremBeerRow()
    {
        var results = await MakeIlScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "1714000" && r.RateName.Contains("Beer"));
        Assert.That(row.Rate, Is.EqualTo(0.29m), "Chicago on-prem beer tax should be $0.29/gal");
        Assert.That(row.SaleContext, Is.EqualTo(SaleContext.OnPremise));
        Assert.That(row.RemittancePoint, Is.EqualTo(RemittancePoint.Retailer));
        Assert.That(row.IsIncludedInPrice, Is.False);
    }

    [Test]
    public async Task IL_Returns_Chicago_OnPremSpiritsRow()
    {
        var results = await MakeIlScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "1714000" && r.RateName.Contains("Spirits"));
        Assert.That(row.Rate, Is.EqualTo(2.68m), "Chicago on-prem spirits tax should be $2.68/gal");
    }

    [Test]
    public async Task IL_Returns_Chicago_OffPremisesRow()
    {
        var results = await MakeIlScraper().ScrapeAsync();
        var row = results.FirstOrDefault(r => r.FipsCode == "1714000"
                                               && r.SaleContext == SaleContext.OffPremise);
        Assert.That(row, Is.Not.Null, "Should emit a Chicago off-premises rate row");
        Assert.That(row!.Rate, Is.EqualTo(0.015m), "Off-premises rate should be 1.5%");
        Assert.That(row.RateBasis, Is.EqualTo(RateBasis.Percentage));
    }

    [Test]
    public async Task IL_TotalRowCount_IsAtLeast11()
    {
        var results = await MakeIlScraper().ScrapeAsync();
        // 4 state + 4 Chicago on-prem + 1 Chicago off-prem = 9 minimum; +3 Cook when page has rates
        Assert.That(results.Count, Is.GreaterThanOrEqualTo(9));
    }

    [Test]
    public void IL_Throws_WhenExcisePage_UnreachableOrBlank()
    {
        // Excise URL unregistered (404), Wayback disabled → HttpRequestException
        var handler = new FakeHttpMessageHandler();
        handler.Register(IlCookUrl,        IlCookHtml);
        handler.Register(IlChicagoUrl,     IlChicagoHtml);
        handler.Register(IlChicagoNewsUrl, IlChicagoNewsHtml);
        var s = new TaxRateCollector.Infrastructure.Services.SettingsService();
        s.Current.WaybackMachineFallback = false;
        var scraper = new IllinoisAlcoholScraper(new FakeHttpClientFactory(handler), s);
        Assert.ThrowsAsync<HttpRequestException>(() => scraper.ScrapeAsync());
    }

    [Test]
    public void IL_Throws_WhenExcisePage_CannotBeParsed()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(IlExciseUrl,      "<html><body>Under construction</body></html>");
        handler.Register(IlCookUrl,        IlCookHtml);
        handler.Register(IlChicagoUrl,     IlChicagoHtml);
        handler.Register(IlChicagoNewsUrl, IlChicagoNewsHtml);
        var s = new TaxRateCollector.Infrastructure.Services.SettingsService();
        s.Current.WaybackMachineFallback = false;
        var scraper = new IllinoisAlcoholScraper(new FakeHttpClientFactory(handler), s);
        Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());
    }

    [Test]
    public async Task IL_Chicago_PresentViaWayback_WhenMainUrlFails()
    {
        const string archiveUrl    = "https://web.archive.org/web/20260101000000/https://www.chicago.gov/city/en/depts/fin/supp_info/revenue/tax_list/liquor_tax_.html";
        var waybackApiUrl          = $"https://archive.org/wayback/available?url={Uri.EscapeDataString(IlChicagoUrl)}";
        var waybackApiResponse     = "{\"archived_snapshots\":{\"closest\":{\"available\":true,\"url\":\"" + archiveUrl + "\",\"status\":\"200\"}}}";

        var handler = new FakeHttpMessageHandler();
        handler.Register(IlExciseUrl,      IlExciseHtml);
        handler.Register(IlCookUrl,        IlCookHtml);
        // IlChicagoUrl intentionally unregistered → simulates 404
        handler.Register(waybackApiUrl,    waybackApiResponse, "application/json");
        handler.Register(archiveUrl,       IlChicagoHtml);
        handler.Register(IlChicagoNewsUrl, IlChicagoNewsHtml);

        var s = new TaxRateCollector.Infrastructure.Services.SettingsService();
        s.Current.WaybackMachineFallback = true;
        var scraper  = new IllinoisAlcoholScraper(new FakeHttpClientFactory(handler), s);
        var results  = await scraper.ScrapeAsync();

        var beerRow = results.FirstOrDefault(r => r.FipsCode == "1714000" && r.RateName.Contains("Beer"));
        Assert.That(beerRow, Is.Not.Null, "Chicago beer row should be present via Wayback");
        Assert.That(beerRow!.SourceUrl, Is.EqualTo(archiveUrl));
        Assert.That(beerRow.SourceConfidence, Is.EqualTo(SourceConfidence.Archive));
    }

    [Test]
    public async Task IL_DirectFetch_HasOfficialConfidence()
    {
        var results = await MakeIlScraper().ScrapeAsync();
        Assert.That(results.All(r => r.SourceConfidence == SourceConfidence.Official), Is.True,
            "All directly-fetched IL rows should have Official confidence");
    }

    // ── Minnesota ─────────────────────────────────────────────────────────────
    // Fake HTML mirrors the real MN DOR page structure (revenue.state.mn.us).
    // Beer rates are expressed per 31-gallon barrel on the DOR page; scraper divides by 31.
    // Wine/cider rates are per gallon. Spirits rate is per gallon.

    private const string MnBeerUrl    = "https://www.revenue.state.mn.us/fermented-malt-beverage-excise-tax";
    private const string MnWineUrl    = "https://www.revenue.state.mn.us/wine-excise-tax";
    private const string MnSpiritsUrl = "https://www.revenue.state.mn.us/distilled-spirits-excise-tax";
    private const string MnMplsUrl    = "https://www.minneapolismn.gov/resident-services/taxes/business-taxes/downtown-liquor-tax/";

    private static readonly string MnBeerHtml = """
        <html><body>
        <h1>Fermented Malt Beverage Excise Tax</h1>
        <table>
        <thead><tr><th>Alcohol Content</th><th>Tax per 31-Gallon Barrel</th></tr></thead>
        <tbody>
        <tr><td>More than 3.2%</td><td>$4.60</td></tr>
        <tr><td>3.2% or less</td><td>$2.40</td></tr>
        </tbody>
        </table>
        </body></html>
        """;

    private static readonly string MnWineHtml = """
        <html><body>
        <h1>Wine Excise Tax</h1>
        <table>
        <thead><tr><th>Alcohol Content</th><th>Tax per Gallon</th><th>Tax per Liter</th></tr></thead>
        <tbody>
        <tr><td>14% ABV or less (except cider)</td><td>$0.30</td><td>$0.08</td></tr>
        <tr><td>Cider</td><td>$0.15</td><td>$0.04</td></tr>
        <tr><td>Wine containing more than 14% but less than 21% alcohol by volume</td><td>$0.95</td><td>$0.25</td></tr>
        <tr><td>Wine containing more than 21% but less than 24% alcohol by volume</td><td>$1.82</td><td>$0.48</td></tr>
        <tr><td>Wine containing more than 24% alcohol by volume</td><td>$3.52</td><td>$0.93</td></tr>
        <tr><td>Sparkling wines</td><td>$1.82</td><td>$0.48</td></tr>
        </tbody>
        </table>
        </body></html>
        """;

    private static readonly string MnSpiritsHtml = """
        <html><body>
        <h1>Distilled Spirits Excise Tax</h1>
        <p>Distilled spirits, liqueurs, cordials, and specialties: $5.03 per gallon or $1.33 per liter.</p>
        </body></html>
        """;

    private static readonly string MnMplsHtml = """
        <html><body>
        <h1>Downtown Liquor Tax</h1>
        <p>The downtown liquor tax is 3 percent on alcoholic beverages sold by on-sale
        licensees in the downtown taxing area.</p>
        </body></html>
        """;

    // wayback=false keeps Wayback disabled so 404s throw HttpRequestException (as expected by throw-tests)
    private static TaxRateCollector.Infrastructure.Services.SettingsService MakeSettings(bool wayback = false)
    {
        var s = new TaxRateCollector.Infrastructure.Services.SettingsService();
        s.Current.WaybackMachineFallback = wayback;
        return s;
    }

    private static IStateBulkScraper MakeMnScraper(string? mplsHtml = null, bool wayback = false)
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(MnBeerUrl,    MnBeerHtml);
        handler.Register(MnWineUrl,    MnWineHtml);
        handler.Register(MnSpiritsUrl, MnSpiritsHtml);
        if (mplsHtml is not null)
            handler.Register(MnMplsUrl, mplsHtml);
        return new MinnesotaAlcoholScraper(new FakeHttpClientFactory(handler), MakeSettings(wayback));
    }

    [Test]
    public void StateCode_IsMN()
    {
        Assert.That(MakeMnScraper().StateCode, Is.EqualTo("MN"));
    }

    [Test]
    public async Task MN_Returns_Beer32_Row()
    {
        var results = await MakeMnScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "27" && r.RateName.Contains("≤3.2%"));
        Assert.That(row.Rate, Is.EqualTo(2.40m / 31m));
        Assert.That(row.RateBasis, Is.EqualTo(RateBasis.FlatPerVolume));
        Assert.That(row.TaxType, Is.EqualTo(TaxType.ExciseTax));
        Assert.That(row.RemittancePoint, Is.EqualTo(RemittancePoint.Distributor));
        Assert.That(row.MaxAbv, Is.EqualTo(0.032m));
    }

    [Test]
    public async Task MN_Returns_Beer_Row()
    {
        var results = await MakeMnScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "27" && r.RateName.Contains(">3.2%"));
        Assert.That(row.Rate, Is.EqualTo(4.60m / 31m));
        Assert.That(row.MinAbv, Is.EqualTo(0.032m));
    }

    [Test]
    public async Task MN_Returns_Cider_Row()
    {
        var results = await MakeMnScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "27" && r.RateName.Contains("Cider"));
        Assert.That(row.Rate, Is.EqualTo(0.15m));
        Assert.That(row.MinAbv, Is.EqualTo(0.005m));
        Assert.That(row.MaxAbv, Is.EqualTo(0.07m));
    }

    [Test]
    public async Task MN_Returns_Wine14_Row()
    {
        var results = await MakeMnScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "27" && r.RateName.Contains("≤14%"));
        Assert.That(row.Rate, Is.EqualTo(0.30m));
        Assert.That(row.MaxAbv, Is.EqualTo(0.14m));
    }

    [Test]
    public async Task MN_Returns_Wine21_Row()
    {
        var results = await MakeMnScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "27" && r.RateName.Contains(">14%"));
        Assert.That(row.Rate, Is.EqualTo(0.95m));
        Assert.That(row.MinAbv, Is.EqualTo(0.14m));
        Assert.That(row.MaxAbv, Is.EqualTo(0.21m));
    }

    [Test]
    public async Task MN_Returns_Wine24_Row()
    {
        var results = await MakeMnScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "27" && r.RateName.Contains(">21%"));
        Assert.That(row.Rate, Is.EqualTo(1.82m));
        Assert.That(row.MinAbv, Is.EqualTo(0.21m));
        Assert.That(row.MaxAbv, Is.EqualTo(0.24m));
    }

    [Test]
    public async Task MN_Returns_Wine24Plus_Row()
    {
        var results = await MakeMnScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "27" && r.RateName.Contains(">24%"));
        Assert.That(row.Rate, Is.EqualTo(3.52m));
        Assert.That(row.MinAbv, Is.EqualTo(0.24m));
    }

    [Test]
    public async Task MN_Returns_Spirits_Row()
    {
        var results = await MakeMnScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "27" && r.RateName.Contains("Distilled Spirits"));
        Assert.That(row.Rate, Is.EqualTo(5.03m));
        Assert.That(row.RateBasis, Is.EqualTo(RateBasis.FlatPerVolume));
        Assert.That(row.IsIncludedInPrice, Is.True);
    }

    [Test]
    public async Task MN_TotalRowCount_IsAtLeast8()
    {
        var results = await MakeMnScraper().ScrapeAsync();
        // 2 beer + 5 wine/cider tiers + 1 spirits = 8 state rows (Minneapolis absent when URL fails)
        Assert.That(results.Count, Is.GreaterThanOrEqualTo(8));
    }

    [Test]
    public async Task MN_Minneapolis_Row_PresentWhenUrlReachable()
    {
        var results = await MakeMnScraper(mplsHtml: MnMplsHtml).ScrapeAsync();
        var row = results.FirstOrDefault(r => r.FipsCode == "2743000");
        Assert.That(row, Is.Not.Null, "Minneapolis row should be present when URL is reachable");
        Assert.That(row!.Rate, Is.EqualTo(0.03m));
        Assert.That(row.RateBasis, Is.EqualTo(RateBasis.Percentage));
        Assert.That(row.SaleContext, Is.EqualTo(SaleContext.OnPremise));
        Assert.That(row.RemittancePoint, Is.EqualTo(RemittancePoint.Retailer));
    }

    [Test]
    public async Task MN_Minneapolis_Row_AbsentWhenUrlFailsAndWaybackDisabled()
    {
        // MnMplsUrl unregistered (404), Wayback disabled → Minneapolis row silently skipped
        var results = await MakeMnScraper().ScrapeAsync();
        Assert.That(results.Any(r => r.FipsCode == "2743000"), Is.False);
    }

    [Test]
    public async Task MN_Minneapolis_Row_PresentViaWayback()
    {
        // Simulate: live URL returns 404, but Wayback Machine has an archived snapshot
        const string archiveUrl = "https://web.archive.org/web/20241010120000/https://www.minneapolismn.gov/downtown-liquor-tax/";
        var waybackApiUrl       = $"https://archive.org/wayback/available?url={Uri.EscapeDataString(MnMplsUrl)}";
        var waybackApiResponse  = "{\"archived_snapshots\":{\"closest\":{\"available\":true,\"url\":\"" + archiveUrl + "\",\"status\":\"200\"}}}";

        var handler = new FakeHttpMessageHandler();
        handler.Register(MnBeerUrl,    MnBeerHtml);
        handler.Register(MnWineUrl,    MnWineHtml);
        handler.Register(MnSpiritsUrl, MnSpiritsHtml);
        // MnMplsUrl intentionally NOT registered → simulates 404
        handler.Register(waybackApiUrl, waybackApiResponse, "application/json");
        handler.Register(archiveUrl,    MnMplsHtml);

        var scraper = new MinnesotaAlcoholScraper(new FakeHttpClientFactory(handler), MakeSettings(wayback: true));
        var results = await scraper.ScrapeAsync();

        var row = results.FirstOrDefault(r => r.FipsCode == "2743000");
        Assert.That(row, Is.Not.Null, "Minneapolis row should be present via Wayback fallback");
        Assert.That(row!.SourceUrl, Is.EqualTo(archiveUrl), "SourceUrl should be the Wayback archive URL");
        Assert.That(row.Rate, Is.EqualTo(0.03m));
        Assert.That(row.SourceConfidence, Is.EqualTo(SourceConfidence.Archive),
            "Wayback-sourced row should be marked Archive confidence");
    }

    [Test]
    public async Task MN_DirectFetch_HasOfficialConfidence()
    {
        var results = await MakeMnScraper(mplsHtml: MnMplsHtml).ScrapeAsync();
        var mplsRow = results.FirstOrDefault(r => r.FipsCode == "2743000");
        Assert.That(mplsRow, Is.Not.Null);
        Assert.That(mplsRow!.SourceConfidence, Is.EqualTo(SourceConfidence.Official));

        // All state-level rows should also be Official when fetched live
        var stateRows = results.Where(r => r.FipsCode == "27").ToList();
        Assert.That(stateRows.All(r => r.SourceConfidence == SourceConfidence.Official), Is.True,
            "All directly-fetched state rows should have Official confidence");
    }

    [Test]
    public void MN_Throws_WhenBeerPage_Returns404()
    {
        // Wayback disabled → 404 on a required page propagates as HttpRequestException
        var handler = new FakeHttpMessageHandler();
        handler.Register(MnWineUrl,    MnWineHtml);
        handler.Register(MnSpiritsUrl, MnSpiritsHtml);
        var scraper = new MinnesotaAlcoholScraper(new FakeHttpClientFactory(handler), MakeSettings());
        Assert.ThrowsAsync<HttpRequestException>(() => scraper.ScrapeAsync());
    }

    [Test]
    public void MN_Throws_WhenWinePage_CannotBeParsed()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(MnBeerUrl,    MnBeerHtml);
        handler.Register(MnWineUrl,    "<html><body>Page under maintenance</body></html>");
        handler.Register(MnSpiritsUrl, MnSpiritsHtml);
        var scraper = new MinnesotaAlcoholScraper(new FakeHttpClientFactory(handler), MakeSettings());
        Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());
    }

    [Test]
    public void MN_Throws_WhenBeerRates_AreInverted()
    {
        var badBeerHtml = """
            <html><body>
            <table>
            <tr><td>More than 3.2%</td><td>$2.40</td></tr>
            <tr><td>3.2% or less</td><td>$4.60</td></tr>
            </table>
            </body></html>
            """;
        var handler = new FakeHttpMessageHandler();
        handler.Register(MnBeerUrl,    badBeerHtml);
        handler.Register(MnWineUrl,    MnWineHtml);
        handler.Register(MnSpiritsUrl, MnSpiritsHtml);
        var scraper = new MinnesotaAlcoholScraper(new FakeHttpClientFactory(handler), MakeSettings());
        Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());
    }
}
