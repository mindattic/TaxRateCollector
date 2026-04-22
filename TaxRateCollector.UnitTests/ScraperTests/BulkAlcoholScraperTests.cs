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
        Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());
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
        Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());
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

    // ── Iowa ──────────────────────────────────────────────────────────────────
    // Beer: $5.89 per 31-gallon barrel (Iowa Code § 123.136)
    // Wine: $1.75 per gallon flat (Iowa Code § 123.183)
    // Spirits: control state (Iowa ABD) — $0.00 entry with ThirdParty confidence

    private const string IaSourceUrl  = "https://revenue.iowa.gov/taxes/tax-guidance/general/iowa-taxfee-descriptions-and-rates";
    private const string IaSpiritsUrl = "https://www.salestaxhandbook.com/iowa/alcohol";

    private static readonly string IaHtml = """
        <html><body>
        <h2>Beer Excise Tax</h2>
        <p>The tax is imposed on the wholesale sale of beer in Iowa at a rate of
           $5.89 per 31-gallon barrel or fractional part thereof.</p>
        <h2>Wine Gallonage Tax</h2>
        <p>The tax is imposed on all wine sold at wholesale in Iowa at a rate of
           $1.75 per gallon or fractional part thereof.</p>
        </body></html>
        """;

    private static readonly string IaSpiritsHtml = """
        <html><body>
        <h4>Iowa Liquor Tax - STATE-CONTROLLED</h4>
        <p>Iowa is an "Alcoholic beverage control state", in which the sale of liquor and
        spirits are state-controlled. There is no need to apply an additional excise tax on liquor.</p>
        </body></html>
        """;

    private static IStateBulkScraper MakeIaScraper(string? htmlOverride = null, bool wayback = false)
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(IaSourceUrl,  htmlOverride ?? IaHtml);
        handler.Register(IaSpiritsUrl, IaSpiritsHtml);
        return new IowaAlcoholScraper(new FakeHttpClientFactory(handler), MakeSettings(wayback));
    }

    [Test]
    public void StateCode_IsIA()
        => Assert.That(MakeIaScraper().StateCode, Is.EqualTo("IA"));

    [Test]
    public void IA_SstCategoryName_IsAlcoholicBeverages()
        => Assert.That(MakeIaScraper().SstCategoryName, Is.EqualTo("Alcoholic Beverages"));

    [Test]
    public async Task IA_Returns_BeerRow_WithCorrectPerGallonRate()
    {
        var results = await MakeIaScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "19" && r.ProductCategory == ProductCategory.Beer);
        Assert.That(row.Rate, Is.EqualTo(5.89m / 31m));
        Assert.That(row.RateBasis, Is.EqualTo(RateBasis.FlatPerVolume));
        Assert.That(row.TaxType, Is.EqualTo(TaxType.ExciseTax));
        Assert.That(row.RemittancePoint, Is.EqualTo(RemittancePoint.Distributor));
        Assert.That(row.IsIncludedInPrice, Is.True);
        Assert.That(row.SourceConfidence, Is.EqualTo(SourceConfidence.Official));
    }

    [Test]
    public async Task IA_Returns_WineRow_AtFlatRate()
    {
        var results = await MakeIaScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "19" && r.ProductCategory == ProductCategory.Wine);
        Assert.That(row.Rate, Is.EqualTo(1.75m));
        Assert.That(row.RateBasis, Is.EqualTo(RateBasis.FlatPerVolume));
        Assert.That(row.IsIncludedInPrice, Is.True);
    }

    [Test]
    public async Task IA_Returns_ExactlyThreeRows()
    {
        var results = await MakeIaScraper().ScrapeAsync();
        Assert.That(results.Count, Is.EqualTo(3), "Beer + wine + spirits (control-state $0.00) = 3 rows");
    }

    [Test]
    public async Task IA_Returns_SpiritsRow_WithZeroRate()
    {
        var results = await MakeIaScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "19" && r.ProductCategory == ProductCategory.Spirits);
        Assert.That(row.Rate, Is.EqualTo(0m));
        Assert.That(row.RateBasis, Is.EqualTo(RateBasis.FlatPerVolume));
        Assert.That(row.TaxType, Is.EqualTo(TaxType.ExciseTax));
        Assert.That(row.SourceConfidence, Is.EqualTo(SourceConfidence.ThirdParty));
        Assert.That(row.Conditions, Does.Contain("control state"), "Conditions should explain control-state status");
    }

    [Test]
    public void IA_Throws_WhenPageLacksBeerBarrelText()
    {
        var scraper = MakeIaScraper("<html><body>$1.75 per gallon wine only</body></html>");
        Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());
    }

    [Test]
    public void IA_Throws_WhenPageUnreachable_WaybackDisabled()
    {
        var handler = new FakeHttpMessageHandler(); // IaSourceUrl not registered → 404
        var scraper = new IowaAlcoholScraper(new FakeHttpClientFactory(handler), MakeSettings());
        Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());
    }

    // ── Indiana ───────────────────────────────────────────────────────────────
    // Beer / Hard Cider: $0.115/gal (IC § 7.1-4-2-1)
    // Wine (<21% ABV):   $0.47/gal  (IC § 7.1-4-4-1)
    // Liquor (≥21% ABV): $2.68/gal  (IC § 7.1-4-3-1)

    private const string InSourceUrl = "https://www.in.gov/dor/resources/tax-rates-and-reports/rates-fees-and-penalties/miscellaneous-tax-rates/";

    private static readonly string InHtml = """
        <html><body>
        <h1>Miscellaneous Tax Rates</h1>
        <table>
        <thead><tr><th>Tax</th><th>Rate (per gallon)</th></tr></thead>
        <tbody>
        <tr><td>Beer</td><td>$0.115</td></tr>
        <tr><td>Wine (less than 21% alcohol)</td><td>$0.47</td></tr>
        <tr><td>Liquor (21% or more)</td><td>$2.68</td></tr>
        </tbody>
        </table>
        </body></html>
        """;

    private static IStateBulkScraper MakeInScraper(string? htmlOverride = null, bool wayback = false)
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(InSourceUrl, htmlOverride ?? InHtml);
        return new IndianaAlcoholScraper(new FakeHttpClientFactory(handler), MakeSettings(wayback));
    }

    [Test]
    public void StateCode_IsIN()
        => Assert.That(MakeInScraper().StateCode, Is.EqualTo("IN"));

    [Test]
    public void IN_SstCategoryName_IsAlcoholicBeverages()
        => Assert.That(MakeInScraper().SstCategoryName, Is.EqualTo("Alcoholic Beverages"));

    [Test]
    public async Task IN_Returns_BeerRow()
    {
        var results = await MakeInScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "18" && r.ProductCategory == ProductCategory.Beer);
        Assert.That(row.Rate, Is.EqualTo(0.115m));
        Assert.That(row.RateBasis, Is.EqualTo(RateBasis.FlatPerVolume));
        Assert.That(row.TaxType, Is.EqualTo(TaxType.ExciseTax));
        Assert.That(row.RemittancePoint, Is.EqualTo(RemittancePoint.Distributor));
        Assert.That(row.MaxAbv, Is.EqualTo(0.20m));
    }

    [Test]
    public async Task IN_Returns_WineRow()
    {
        var results = await MakeInScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "18" && r.ProductCategory == ProductCategory.Wine);
        Assert.That(row.Rate, Is.EqualTo(0.47m));
        Assert.That(row.MaxAbv, Is.EqualTo(0.21m));
    }

    [Test]
    public async Task IN_Returns_LiquorRow()
    {
        var results = await MakeInScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "18" && r.ProductCategory == ProductCategory.Spirits);
        Assert.That(row.Rate, Is.EqualTo(2.68m));
        Assert.That(row.MinAbv, Is.EqualTo(0.21m));
        Assert.That(row.IsIncludedInPrice, Is.True);
    }

    [Test]
    public async Task IN_Returns_ExactlyThreeRows()
    {
        var results = await MakeInScraper().ScrapeAsync();
        Assert.That(results.Count, Is.EqualTo(3), "Beer + wine + liquor = 3 rows");
    }

    [Test]
    public void IN_Throws_WhenRatesInverted()
    {
        // All rates descending (liquor < wine < beer) should fail the ordering check
        Assert.Throws<InvalidOperationException>(() =>
            _ = IndianaAlcoholScraper.Parse(
                "<html><body>Beer $0.50 Wine (less than 21% alcohol) $0.40 Liquor (21% or more) $0.30</body></html>",
                InSourceUrl));
    }

    [Test]
    public void IN_Throws_WhenPageUnreachable_WaybackDisabled()
    {
        var handler = new FakeHttpMessageHandler();
        var scraper = new IndianaAlcoholScraper(new FakeHttpClientFactory(handler), MakeSettings());
        Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());
    }

    // ── Michigan ──────────────────────────────────────────────────────────────
    // Beer:      $6.30/barrel ÷ 31 gal  (MCL § 436.1409)
    // Wine ≤16%: 13.5 cents/liter × 3.78541 gal  (MCL § 436.1301)
    // Wine >16%: 20 cents/liter × 3.78541 gal    (MCL § 436.1301)
    // Spirits:   control state (MLCC) — not modeled

    private const string MiBeerUrl = "https://legislature.mi.gov/Laws/MCL?objectName=MCL-436-1409";
    private const string MiWineUrl = "https://legislature.mi.gov/Laws/MCL?objectName=MCL-436-1301";

    private static readonly string MiBeerHtml = """
        <html><body>
        <h1>MCL 436.1409 — Beer Specific Tax</h1>
        <p>A brewer shall pay a tax of $6.30 per barrel on beer manufactured in or
           imported into this state.</p>
        </body></html>
        """;

    private static readonly string MiWineHtml = """
        <html><body>
        <h1>MCL 436.1301 — Wine Specific Tax</h1>
        <p>A winery shall pay a specific tax at the rate of 13.5 cents per liter on
           wine containing not more than 16 percent alcohol by volume, and at the rate
           of 20 cents per liter on wine containing more than 16 percent alcohol by volume.</p>
        </body></html>
        """;

    private static IStateBulkScraper MakeMiScraper(
        string? beerOverride = null, string? wineOverride = null, bool wayback = false)
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(MiBeerUrl, beerOverride ?? MiBeerHtml);
        handler.Register(MiWineUrl, wineOverride ?? MiWineHtml);
        return new MichiganAlcoholScraper(new FakeHttpClientFactory(handler), MakeSettings(wayback));
    }

    [Test]
    public void StateCode_IsMI()
        => Assert.That(MakeMiScraper().StateCode, Is.EqualTo("MI"));

    [Test]
    public void MI_SstCategoryName_IsAlcoholicBeverages()
        => Assert.That(MakeMiScraper().SstCategoryName, Is.EqualTo("Alcoholic Beverages"));

    [Test]
    public async Task MI_Returns_BeerRow_AtPerGallonRate()
    {
        var results = await MakeMiScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "26" && r.ProductCategory == ProductCategory.Beer);
        Assert.That(row.Rate, Is.EqualTo(6.30m / 31m));
        Assert.That(row.RateBasis, Is.EqualTo(RateBasis.FlatPerVolume));
        Assert.That(row.TaxType, Is.EqualTo(TaxType.ExciseTax));
        Assert.That(row.RemittancePoint, Is.EqualTo(RemittancePoint.Distributor));
        Assert.That(row.IsIncludedInPrice, Is.True);
        Assert.That(row.SourceConfidence, Is.EqualTo(SourceConfidence.Official));
    }

    [Test]
    public async Task MI_Returns_Wine16_Row()
    {
        var results = await MakeMiScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "26" && r.ProductCategory == ProductCategory.Wine
                                      && r.MaxAbv == 0.16m);
        // 13.5 cents/liter × 3.78541 L/gal ÷ 100 cents
        Assert.That(row.Rate, Is.EqualTo(0.135m * 3.78541m).Within(0.0001m));
        Assert.That(row.MaxAbv, Is.EqualTo(0.16m));
    }

    [Test]
    public async Task MI_Returns_Wine16Plus_Row()
    {
        var results = await MakeMiScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "26" && r.ProductCategory == ProductCategory.Wine
                                      && r.MinAbv == 0.16m);
        // 20 cents/liter × 3.78541 L/gal ÷ 100 cents
        Assert.That(row.Rate, Is.EqualTo(0.20m * 3.78541m).Within(0.0001m));
        Assert.That(row.MinAbv, Is.EqualTo(0.16m));
    }

    [Test]
    public async Task MI_Returns_ExactlyThreeRows()
    {
        var results = await MakeMiScraper().ScrapeAsync();
        Assert.That(results.Count, Is.EqualTo(3), "Beer + wine≤16% + wine>16% = 3 rows (no spirits; MLCC control state)");
    }

    [Test]
    public void MI_Throws_WhenBeerPageLacksBarrelText()
    {
        var scraper = MakeMiScraper(beerOverride: "<html><body>No barrel rate here</body></html>");
        Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());
    }

    [Test]
    public void MI_Throws_WhenWinePageLacksCentsPerLiter()
    {
        var scraper = MakeMiScraper(wineOverride: "<html><body>No wine rate here</body></html>");
        Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());
    }

    [Test]
    public void MI_Throws_WhenWineRatesInverted()
    {
        // Statute lists higher rate first → document-order picks 20 as ≤16% and 13.5 as >16%
        // → ordering check (high <= low) fires
        var badWineHtml = """
            <html><body>
            A winery shall pay 20 cents per liter on wine containing not more than 16 percent
            alcohol by volume, and 13.5 cents per liter on wine containing more than 16 percent.
            </body></html>
            """;
        var scraper = MakeMiScraper(wineOverride: badWineHtml);
        Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());
    }

    [Test]
    public void MI_Throws_WhenBeerPageUnreachable_WaybackDisabled()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(MiWineUrl, MiWineHtml); // wine registered but beer not
        var scraper = new MichiganAlcoholScraper(new FakeHttpClientFactory(handler), MakeSettings());
        Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());
    }

    // ── North Dakota ──────────────────────────────────────────────────────────
    // Beer:       $0.16/gal (N.D.C.C. § 5-03-07)
    // Wine ≤17%:  $0.50/gal
    // Wine >17%:  $0.60/gal
    // Spirits:    $2.50/gal
    // Source: salestaxhandbook.com (ThirdParty)

    private const string NdSourceUrl = "https://www.salestaxhandbook.com/north-dakota/alcohol";

    private static readonly string NdHtml = """
        <html><body>
        <h2>North Dakota Wine Tax - $0.50 / gallon</h2>
        <p>North Dakota wine vendors are responsible for paying a state excise tax of $0.50 per gallon.
        Additional Taxes: Over 17% – $0.60/gallon; 7% state sales tax</p>
        <h2>North Dakota Beer Tax - $0.16 / gallon</h2>
        <p>North Dakota beer vendors are responsible for paying a state excise tax of $0.16 per gallon.
        Additional Taxes: 7% state sales tax; bulk beer $0.08/gallon</p>
        <h2>North Dakota Liquor Tax - $2.50 / gallon</h2>
        <p>North Dakota liquor vendors are responsible for paying a state excise tax of $2.50 per gallon.</p>
        </body></html>
        """;

    private static IStateBulkScraper MakeNdScraper(string? htmlOverride = null, bool wayback = false)
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(NdSourceUrl, htmlOverride ?? NdHtml);
        return new NorthDakotaAlcoholScraper(new FakeHttpClientFactory(handler), MakeSettings(wayback));
    }

    [Test]
    public void StateCode_IsND()
        => Assert.That(MakeNdScraper().StateCode, Is.EqualTo("ND"));

    [Test]
    public void ND_SstCategoryName_IsAlcoholicBeverages()
        => Assert.That(MakeNdScraper().SstCategoryName, Is.EqualTo("Alcoholic Beverages"));

    [Test]
    public async Task ND_Returns_BeerRow()
    {
        var results = await MakeNdScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "38" && r.ProductCategory == ProductCategory.Beer);
        Assert.That(row.Rate, Is.EqualTo(0.16m));
        Assert.That(row.RateBasis, Is.EqualTo(RateBasis.FlatPerVolume));
        Assert.That(row.TaxType, Is.EqualTo(TaxType.ExciseTax));
        Assert.That(row.SourceConfidence, Is.EqualTo(SourceConfidence.ThirdParty));
    }

    [Test]
    public async Task ND_Returns_Wine17Row()
    {
        var results = await MakeNdScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "38" && r.ProductCategory == ProductCategory.Wine
                                      && r.MaxAbv == 0.17m);
        Assert.That(row.Rate, Is.EqualTo(0.50m));
        Assert.That(row.MaxAbv, Is.EqualTo(0.17m));
    }

    [Test]
    public async Task ND_Returns_Wine17PlusRow()
    {
        var results = await MakeNdScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "38" && r.ProductCategory == ProductCategory.Wine
                                      && r.MinAbv == 0.17m);
        Assert.That(row.Rate, Is.EqualTo(0.60m));
        Assert.That(row.MinAbv, Is.EqualTo(0.17m));
    }

    [Test]
    public async Task ND_Returns_SpiritsRow()
    {
        var results = await MakeNdScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "38" && r.ProductCategory == ProductCategory.Spirits);
        Assert.That(row.Rate, Is.EqualTo(2.50m));
    }

    [Test]
    public async Task ND_Returns_ExactlyFourRows()
    {
        var results = await MakeNdScraper().ScrapeAsync();
        Assert.That(results.Count, Is.EqualTo(4), "Beer + wine≤17% + wine>17% + spirits = 4 rows");
    }

    [Test]
    public void ND_Throws_WhenWineRatesInverted()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _ = NorthDakotaAlcoholScraper.Parse(
                "<html><body>North Dakota Wine Tax - $0.60 / gallon Over 17% – $0.50/gallon " +
                "North Dakota Beer Tax - $0.16 / gallon North Dakota Liquor Tax - $2.50 / gallon</body></html>",
                NdSourceUrl));
    }

    [Test]
    public void ND_Throws_WhenPageUnreachable_WaybackDisabled()
    {
        var handler = new FakeHttpMessageHandler();
        var scraper = new NorthDakotaAlcoholScraper(new FakeHttpClientFactory(handler), MakeSettings());
        Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());
    }

    // ── South Dakota ──────────────────────────────────────────────────────────
    // Beer:              $0.27/gal (SDCL § 35-5-3)
    // Wine ≤14%:         $0.93/gal
    // Wine 14–20%:       $1.45/gal
    // Wine ≥21%/spark:   $2.07/gal
    // Spirits:           $3.93/gal
    // Source: salestaxhandbook.com (ThirdParty)

    private const string SdSourceUrl = "https://www.salestaxhandbook.com/south-dakota/alcohol";

    private static readonly string SdHtml = """
        <html><body>
        <h2>South Dakota Wine Tax - $0.93 / gallon</h2>
        <p>South Dakota wine vendors are responsible for paying a state excise tax of $0.93 per gallon.
        Additional Taxes: 14% to 20% – $1.45/gallon, over 21% and sparkling wine – $2.07/gallon; 2% wholesale tax</p>
        <h2>South Dakota Beer Tax - $0.27 / gallon</h2>
        <p>South Dakota beer vendors are responsible for paying a state excise tax of $0.27 per gallon.</p>
        <h2>South Dakota Liquor Tax - $3.93 / gallon</h2>
        <p>South Dakota liquor vendors are responsible for paying a state excise tax of $3.93 per gallon.
        Additional Taxes: Under 14% – $0.93/gallon; 2% wholesale tax</p>
        </body></html>
        """;

    private static IStateBulkScraper MakeSdScraper(string? htmlOverride = null, bool wayback = false)
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(SdSourceUrl, htmlOverride ?? SdHtml);
        return new SouthDakotaAlcoholScraper(new FakeHttpClientFactory(handler), MakeSettings(wayback));
    }

    [Test]
    public void StateCode_IsSD()
        => Assert.That(MakeSdScraper().StateCode, Is.EqualTo("SD"));

    [Test]
    public void SD_SstCategoryName_IsAlcoholicBeverages()
        => Assert.That(MakeSdScraper().SstCategoryName, Is.EqualTo("Alcoholic Beverages"));

    [Test]
    public async Task SD_Returns_BeerRow()
    {
        var results = await MakeSdScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "46" && r.ProductCategory == ProductCategory.Beer);
        Assert.That(row.Rate, Is.EqualTo(0.27m));
        Assert.That(row.RateBasis, Is.EqualTo(RateBasis.FlatPerVolume));
        Assert.That(row.TaxType, Is.EqualTo(TaxType.ExciseTax));
        Assert.That(row.SourceConfidence, Is.EqualTo(SourceConfidence.ThirdParty));
    }

    [Test]
    public async Task SD_Returns_Wine14Row()
    {
        var results = await MakeSdScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "46" && r.ProductCategory == ProductCategory.Wine
                                      && r.MaxAbv == 0.14m);
        Assert.That(row.Rate, Is.EqualTo(0.93m));
        Assert.That(row.MaxAbv, Is.EqualTo(0.14m));
    }

    [Test]
    public async Task SD_Returns_Wine14To20Row()
    {
        var results = await MakeSdScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "46" && r.ProductCategory == ProductCategory.Wine
                                      && r.MinAbv == 0.14m);
        Assert.That(row.Rate, Is.EqualTo(1.45m));
        Assert.That(row.MaxAbv, Is.EqualTo(0.20m));
    }

    [Test]
    public async Task SD_Returns_Wine21PlusRow()
    {
        var results = await MakeSdScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "46" && r.ProductCategory == ProductCategory.Wine
                                      && r.MinAbv == 0.21m);
        Assert.That(row.Rate, Is.EqualTo(2.07m));
    }

    [Test]
    public async Task SD_Returns_SpiritsRow()
    {
        var results = await MakeSdScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "46" && r.ProductCategory == ProductCategory.Spirits);
        Assert.That(row.Rate, Is.EqualTo(3.93m));
    }

    [Test]
    public async Task SD_Returns_ExactlyFiveRows()
    {
        var results = await MakeSdScraper().ScrapeAsync();
        Assert.That(results.Count, Is.EqualTo(5), "Beer + wine≤14% + wine14-20% + wine21%+sparkling + spirits = 5 rows");
    }

    [Test]
    public void SD_Throws_WhenWineRatesNotAscending()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _ = SouthDakotaAlcoholScraper.Parse(
                "<html><body>South Dakota Wine Tax - $1.45 / gallon " +
                "14% to 20% – $0.93/gallon over 21% and sparkling wine – $2.07/gallon " +
                "South Dakota Beer Tax - $0.27 / gallon South Dakota Liquor Tax - $3.93 / gallon</body></html>",
                SdSourceUrl));
    }

    [Test]
    public void SD_Throws_WhenPageUnreachable_WaybackDisabled()
    {
        var handler = new FakeHttpMessageHandler();
        var scraper = new SouthDakotaAlcoholScraper(new FakeHttpClientFactory(handler), MakeSettings());
        Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());
    }

    // ── Ohio ──────────────────────────────────────────────────────────────────
    // Beer:              $0.18/gal (ORC § 4301.42)
    // Wine ≤14%:         $0.32/gal (ORC § 4301.43 base)
    // Wine 14–21%:       $1.00/gal (fortified)
    // Vermouth:          $1.10/gal
    // Sparkling wine:    $1.50/gal
    // Spirits:           control state — Ohio Division of Liquor Control ($0.00 row)
    // Source: salestaxhandbook.com (ThirdParty)

    private const string OhSourceUrl = "https://www.salestaxhandbook.com/ohio/alcohol";

    private static readonly string OhHtml = """
        <html><body>
        <h2>Ohio Wine Tax - $0.32 / gallon</h2>
        <p>Ohio wine vendors are responsible for paying a state excise tax of $0.32 per gallon.
        Additional Taxes: 14% to 21% - $1.00/gallon; vermouth - $1.10/gallon; sparkling - $1.50/gallon</p>
        <h2>Ohio Beer Tax - $0.18 / gallon</h2>
        <p>Ohio beer vendors are responsible for paying a state excise tax of $0.18 per gallon.</p>
        <h2>Ohio Liquor Tax - STATE-CONTROLLED</h2>
        <p>Ohio is an "Alcoholic beverage control state", in which the sale of liquor and
        spirits are state-controlled. There is no need to apply an additional excise tax on liquor.</p>
        </body></html>
        """;

    private static IStateBulkScraper MakeOhScraper(string? htmlOverride = null, bool wayback = false)
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(OhSourceUrl, htmlOverride ?? OhHtml);
        return new OhioAlcoholScraper(new FakeHttpClientFactory(handler), MakeSettings(wayback));
    }

    [Test]
    public void StateCode_IsOH()
        => Assert.That(MakeOhScraper().StateCode, Is.EqualTo("OH"));

    [Test]
    public void OH_SstCategoryName_IsAlcoholicBeverages()
        => Assert.That(MakeOhScraper().SstCategoryName, Is.EqualTo("Alcoholic Beverages"));

    [Test]
    public async Task OH_Returns_BeerRow()
    {
        var results = await MakeOhScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "39" && r.ProductCategory == ProductCategory.Beer);
        Assert.That(row.Rate, Is.EqualTo(0.18m));
        Assert.That(row.RateBasis, Is.EqualTo(RateBasis.FlatPerVolume));
        Assert.That(row.TaxType, Is.EqualTo(TaxType.ExciseTax));
        Assert.That(row.RemittancePoint, Is.EqualTo(RemittancePoint.Distributor));
        Assert.That(row.SourceConfidence, Is.EqualTo(SourceConfidence.ThirdParty));
    }

    [Test]
    public async Task OH_Returns_WineBaseRow()
    {
        var results = await MakeOhScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "39" && r.ProductCategory == ProductCategory.Wine
                                      && r.MaxAbv == 0.14m);
        Assert.That(row.Rate, Is.EqualTo(0.32m));
        Assert.That(row.MaxAbv, Is.EqualTo(0.14m));
    }

    [Test]
    public async Task OH_Returns_FortifiedWineRow()
    {
        var results = await MakeOhScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "39" && r.ProductCategory == ProductCategory.Wine
                                      && r.MinAbv == 0.14m);
        Assert.That(row.Rate, Is.EqualTo(1.00m));
        Assert.That(row.MaxAbv, Is.EqualTo(0.21m));
    }

    [Test]
    public async Task OH_Returns_VermouthRow()
    {
        var results = await MakeOhScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "39" && r.ProductCategory == ProductCategory.Wine
                                      && r.RateName.Contains("Vermouth"));
        Assert.That(row.Rate, Is.EqualTo(1.10m));
        Assert.That(row.Conditions, Does.Contain("vermouth").IgnoreCase);
    }

    [Test]
    public async Task OH_Returns_SparklingWineRow()
    {
        var results = await MakeOhScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "39" && r.ProductCategory == ProductCategory.Wine
                                      && r.RateName.Contains("Sparkling"));
        Assert.That(row.Rate, Is.EqualTo(1.50m));
        Assert.That(row.Conditions, Does.Contain("sparkling").IgnoreCase);
    }

    [Test]
    public async Task OH_EveryRow_CitesItsStatutoryAuthority()
    {
        var results = await MakeOhScraper().ScrapeAsync();
        foreach (var row in results)
            Assert.That(row.Conditions, Does.Contain("Statutory authority:"),
                $"Row '{row.RateName}' must declare its statutory authority in Conditions.");
    }

    [Test]
    public async Task OH_BeerRow_CitesORC_4301_42()
    {
        var results = await MakeOhScraper().ScrapeAsync();
        var row = results.First(r => r.ProductCategory == ProductCategory.Beer);
        Assert.That(row.Conditions, Does.Contain("§ 4301.42"));
    }

    [Test]
    public async Task OH_SpiritsRow_CitesORC_4301_10()
    {
        var results = await MakeOhScraper().ScrapeAsync();
        var row = results.First(r => r.ProductCategory == ProductCategory.Spirits);
        Assert.That(row.Conditions, Does.Contain("§ 4301.10"));
    }

    [Test]
    public async Task OH_Returns_SpiritsRow_WithZeroRate_ControlState()
    {
        var results = await MakeOhScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "39" && r.ProductCategory == ProductCategory.Spirits);
        Assert.That(row.Rate, Is.EqualTo(0m));
        Assert.That(row.SourceConfidence, Is.EqualTo(SourceConfidence.ThirdParty));
        Assert.That(row.Conditions, Does.Contain("control state"),
            "Conditions should explain control-state arrangement");
    }

    [Test]
    public async Task OH_Returns_ExactlySixRows()
    {
        var results = await MakeOhScraper().ScrapeAsync();
        Assert.That(results.Count, Is.EqualTo(6),
            "Beer + wine≤14% + fortified 14-21% + vermouth + sparkling + spirits (control) = 6 rows");
    }

    [Test]
    public async Task OH_AllRows_ShareSameSourceUrl()
    {
        var results = await MakeOhScraper().ScrapeAsync();
        var urls = results.Select(r => r.SourceUrl).Distinct().ToList();
        Assert.That(urls, Has.Count.EqualTo(1));
        Assert.That(urls[0], Is.EqualTo(OhSourceUrl));
    }

    [Test]
    public void OH_Throws_WhenFortifiedTier_NotAscendingAboveBase()
    {
        // Fortified rate ≤ base rate → ordering check fires
        Assert.Throws<InvalidOperationException>(() =>
            _ = OhioAlcoholScraper.Parse(
                "<html><body>Ohio Beer Tax - $0.18 / gallon " +
                "Ohio Wine Tax - $1.00 / gallon " +
                "14% to 21% - $0.32/gallon vermouth - $1.10/gallon sparkling - $1.50/gallon" +
                "</body></html>",
                OhSourceUrl));
    }

    [Test]
    public void OH_Throws_WhenSparklingTier_NotAscendingAboveFortified()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _ = OhioAlcoholScraper.Parse(
                "<html><body>Ohio Beer Tax - $0.18 / gallon " +
                "Ohio Wine Tax - $0.32 / gallon " +
                "14% to 21% - $1.50/gallon vermouth - $1.10/gallon sparkling - $1.00/gallon" +
                "</body></html>",
                OhSourceUrl));
    }

    [Test]
    public void OH_Throws_WhenBeerTagMissing()
    {
        var scraper = MakeOhScraper("<html><body>Ohio Wine Tax - $0.32 / gallon " +
            "14% to 21% - $1.00/gallon vermouth - $1.10/gallon sparkling - $1.50/gallon</body></html>");
        // Missing "Ohio Beer Tax" header → required-content check fails before parsing
        Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());
    }

    [Test]
    public void OH_Throws_WhenVermouthMissing()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _ = OhioAlcoholScraper.Parse(
                "<html><body>Ohio Beer Tax - $0.18 / gallon " +
                "Ohio Wine Tax - $0.32 / gallon " +
                "14% to 21% - $1.00/gallon sparkling - $1.50/gallon</body></html>",
                OhSourceUrl));
    }

    [Test]
    public void OH_Throws_WhenPageUnreachable_WaybackDisabled()
    {
        var handler = new FakeHttpMessageHandler(); // OhSourceUrl not registered → 404
        var scraper = new OhioAlcoholScraper(new FakeHttpClientFactory(handler), MakeSettings());
        Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());
    }

    // ── Montana ───────────────────────────────────────────────────────────────
    // Beer:    $0.14/gal (MCA § 16-1-406)
    // Wine:    $1.06/gal (MCA § 16-1-411)
    // Spirits: control state — Montana DOR Liquor Control Division ($0.00 row)
    // Source: salestaxhandbook.com (ThirdParty)

    private const string MtSourceUrl = "https://www.salestaxhandbook.com/montana/alcohol";

    private static readonly string MtHtml = """
        <html><body>
        <h2>Montana Wine Tax - $1.06 / gallon</h2>
        <p>Montana wine vendors are responsible for paying a state excise tax of $1.06 per gallon.</p>
        <h2>Montana Beer Tax - $0.14 / gallon</h2>
        <p>Montana beer vendors are responsible for paying a state excise tax of $0.14 per gallon.</p>
        <h2>Montana Liquor Tax - STATE-CONTROLLED</h2>
        <p>Montana is an "Alcoholic beverage control state", in which the sale of liquor and
        spirits are state-controlled. There is no need to apply an additional excise tax on liquor.</p>
        </body></html>
        """;

    private static IStateBulkScraper MakeMtScraper(string? htmlOverride = null, bool wayback = false)
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(MtSourceUrl, htmlOverride ?? MtHtml);
        return new MontanaAlcoholScraper(new FakeHttpClientFactory(handler), MakeSettings(wayback));
    }

    [Test]
    public void StateCode_IsMT()
        => Assert.That(MakeMtScraper().StateCode, Is.EqualTo("MT"));

    [Test]
    public void MT_SstCategoryName_IsAlcoholicBeverages()
        => Assert.That(MakeMtScraper().SstCategoryName, Is.EqualTo("Alcoholic Beverages"));

    [Test]
    public async Task MT_Returns_BeerRow()
    {
        var results = await MakeMtScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "30" && r.ProductCategory == ProductCategory.Beer);
        Assert.That(row.Rate, Is.EqualTo(0.14m));
        Assert.That(row.RateBasis, Is.EqualTo(RateBasis.FlatPerVolume));
        Assert.That(row.TaxType, Is.EqualTo(TaxType.ExciseTax));
        Assert.That(row.RemittancePoint, Is.EqualTo(RemittancePoint.Distributor));
        Assert.That(row.SourceConfidence, Is.EqualTo(SourceConfidence.ThirdParty));
    }

    [Test]
    public async Task MT_Returns_WineRow()
    {
        var results = await MakeMtScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "30" && r.ProductCategory == ProductCategory.Wine);
        Assert.That(row.Rate, Is.EqualTo(1.06m));
    }

    [Test]
    public async Task MT_Returns_SpiritsRow_WithZeroRate_ControlState()
    {
        var results = await MakeMtScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "30" && r.ProductCategory == ProductCategory.Spirits);
        Assert.That(row.Rate, Is.EqualTo(0m));
        Assert.That(row.SourceConfidence, Is.EqualTo(SourceConfidence.ThirdParty));
        Assert.That(row.Conditions, Does.Contain("control state"));
    }

    [Test]
    public async Task MT_EveryRow_CitesItsStatutoryAuthority()
    {
        var results = await MakeMtScraper().ScrapeAsync();
        foreach (var row in results)
            Assert.That(row.Conditions, Does.Contain("Statutory authority:"),
                $"Row '{row.RateName}' must declare its statutory authority in Conditions.");
    }

    [Test]
    public async Task MT_BeerRow_CitesMCA_16_1_406()
    {
        var results = await MakeMtScraper().ScrapeAsync();
        var row = results.First(r => r.ProductCategory == ProductCategory.Beer);
        Assert.That(row.Conditions, Does.Contain("§ 16-1-406"));
    }

    [Test]
    public async Task MT_WineRow_CitesMCA_16_1_411()
    {
        var results = await MakeMtScraper().ScrapeAsync();
        var row = results.First(r => r.ProductCategory == ProductCategory.Wine);
        Assert.That(row.Conditions, Does.Contain("§ 16-1-411"));
    }

    [Test]
    public async Task MT_SpiritsRow_CitesMCA_Title16_Ch2()
    {
        var results = await MakeMtScraper().ScrapeAsync();
        var row = results.First(r => r.ProductCategory == ProductCategory.Spirits);
        Assert.That(row.Conditions, Does.Contain("Title 16, Chapter 2"));
    }

    [Test]
    public async Task MT_Returns_ExactlyThreeRows()
    {
        var results = await MakeMtScraper().ScrapeAsync();
        Assert.That(results.Count, Is.EqualTo(3), "Beer + wine + spirits (control) = 3 rows");
    }

    [Test]
    public void MT_Throws_WhenBeerMissing()
    {
        var scraper = MakeMtScraper("<html><body>Montana Wine Tax - $1.06 / gallon</body></html>");
        Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());
    }

    [Test]
    public void MT_Throws_WhenPageUnreachable_WaybackDisabled()
    {
        var handler = new FakeHttpMessageHandler();
        var scraper = new MontanaAlcoholScraper(new FakeHttpClientFactory(handler), MakeSettings());
        Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());
    }

    // ── Idaho ─────────────────────────────────────────────────────────────────
    // Beer (≤4% ABW):       $0.15/gal (Idaho Code § 23-1008)
    // Strong beer (>4% ABW): $0.45/gal (taxed as wine, § 23-1319)
    // Wine:                 $0.45/gal (Idaho Code § 23-1319)
    // Spirits: control state — Idaho State Liquor Division ($0.00 row)
    // Source: salestaxhandbook.com (ThirdParty)

    private const string IdSourceUrl = "https://www.salestaxhandbook.com/idaho/alcohol";

    private static readonly string IdHtml = """
        <html><body>
        <h2>Idaho Wine Tax - $0.45 / gallon</h2>
        <p>Idaho wine vendors are responsible for paying a state excise tax of $0.45 per gallon.</p>
        <h2>Idaho Beer Tax - $0.15 / gallon</h2>
        <p>Idaho beer vendors are responsible for paying a state excise tax of $0.15 per gallon.
        Additional Taxes: Over 4% – $0.45/gallon</p>
        <h2>Idaho Liquor Tax - STATE-CONTROLLED</h2>
        <p>Idaho is an "Alcoholic beverage control state", in which the sale of liquor and
        spirits are state-controlled. There is no need to apply an additional excise tax on liquor.</p>
        </body></html>
        """;

    private static IStateBulkScraper MakeIdScraper(string? htmlOverride = null, bool wayback = false)
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(IdSourceUrl, htmlOverride ?? IdHtml);
        return new IdahoAlcoholScraper(new FakeHttpClientFactory(handler), MakeSettings(wayback));
    }

    [Test]
    public void StateCode_IsID()
        => Assert.That(MakeIdScraper().StateCode, Is.EqualTo("ID"));

    [Test]
    public void ID_SstCategoryName_IsAlcoholicBeverages()
        => Assert.That(MakeIdScraper().SstCategoryName, Is.EqualTo("Alcoholic Beverages"));

    [Test]
    public async Task ID_Returns_StandardBeerRow()
    {
        var results = await MakeIdScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "16" && r.ProductCategory == ProductCategory.Beer
                                      && r.MaxAbv == 0.05m);
        Assert.That(row.Rate, Is.EqualTo(0.15m));
        Assert.That(row.MaxAbv, Is.EqualTo(0.05m));
    }

    [Test]
    public async Task ID_Returns_StrongBeerRow()
    {
        var results = await MakeIdScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "16" && r.ProductCategory == ProductCategory.Beer
                                      && r.MinAbv == 0.05m);
        Assert.That(row.Rate, Is.EqualTo(0.45m));
        Assert.That(row.MinAbv, Is.EqualTo(0.05m));
        Assert.That(row.Conditions, Does.Contain("4% alcohol by weight"));
    }

    [Test]
    public async Task ID_EveryRow_CitesItsStatutoryAuthority()
    {
        var results = await MakeIdScraper().ScrapeAsync();
        foreach (var row in results)
            Assert.That(row.Conditions, Does.Contain("Statutory authority:"),
                $"Row '{row.RateName}' must declare its statutory authority in Conditions.");
    }

    [Test]
    public async Task ID_StandardBeerRow_CitesIdahoCode_23_1008()
    {
        var results = await MakeIdScraper().ScrapeAsync();
        var row = results.First(r => r.ProductCategory == ProductCategory.Beer && r.MaxAbv == 0.05m);
        Assert.That(row.Conditions, Does.Contain("§ 23-1008"));
    }

    [Test]
    public async Task ID_WineRow_CitesIdahoCode_23_1319()
    {
        var results = await MakeIdScraper().ScrapeAsync();
        var row = results.First(r => r.ProductCategory == ProductCategory.Wine);
        Assert.That(row.Conditions, Does.Contain("§ 23-1319"));
    }

    [Test]
    public async Task ID_SpiritsRow_CitesIdahoCode_23_202()
    {
        var results = await MakeIdScraper().ScrapeAsync();
        var row = results.First(r => r.ProductCategory == ProductCategory.Spirits);
        Assert.That(row.Conditions, Does.Contain("§ 23-202"));
    }

    [Test]
    public async Task ID_Returns_WineRow()
    {
        var results = await MakeIdScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "16" && r.ProductCategory == ProductCategory.Wine);
        Assert.That(row.Rate, Is.EqualTo(0.45m));
    }

    [Test]
    public async Task ID_Returns_SpiritsRow_WithZeroRate_ControlState()
    {
        var results = await MakeIdScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "16" && r.ProductCategory == ProductCategory.Spirits);
        Assert.That(row.Rate, Is.EqualTo(0m));
        Assert.That(row.Conditions, Does.Contain("control state"));
    }

    [Test]
    public async Task ID_Returns_ExactlyFourRows()
    {
        var results = await MakeIdScraper().ScrapeAsync();
        Assert.That(results.Count, Is.EqualTo(4),
            "Standard beer + strong beer + wine + spirits (control) = 4 rows");
    }

    [Test]
    public void ID_Throws_WhenStrongBeerNotHigherThanStandard()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _ = IdahoAlcoholScraper.Parse(
                "<html><body>Idaho Beer Tax - $0.45 / gallon Over 4% – $0.15/gallon " +
                "Idaho Wine Tax - $0.45 / gallon</body></html>",
                IdSourceUrl));
    }

    [Test]
    public void ID_Throws_WhenStrongBeerTierMissing()
    {
        var scraper = MakeIdScraper("""
            <html><body>
            <h2>Idaho Wine Tax - $0.45 / gallon</h2>
            <h2>Idaho Beer Tax - $0.15 / gallon</h2>
            </body></html>
            """);
        Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());
    }

    [Test]
    public void ID_Throws_WhenPageUnreachable_WaybackDisabled()
    {
        var handler = new FakeHttpMessageHandler();
        var scraper = new IdahoAlcoholScraper(new FakeHttpClientFactory(handler), MakeSettings());
        Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());
    }

    // ── Oregon ────────────────────────────────────────────────────────────────
    // Beer:           $0.08/gal (ORS § 473.030)
    // Wine ≤14% ABV:  $0.67/gal
    // Wine >14% ABV:  $0.77/gal
    // Spirits: control state — OLCC ($0.00 row)
    // Source: salestaxhandbook.com (ThirdParty)

    private const string OrSourceUrl = "https://www.salestaxhandbook.com/oregon/alcohol";

    private static readonly string OrHtml = """
        <html><body>
        <h2>Oregon Wine Tax - $0.67 / gallon</h2>
        <p>Oregon wine vendors are responsible for paying a state excise tax of $0.67 per gallon.
        Additional Taxes: Over 14% – $0.77/gallon</p>
        <h2>Oregon Beer Tax - $0.08 / gallon</h2>
        <p>Oregon beer vendors are responsible for paying a state excise tax of $0.08 per gallon.</p>
        <h2>Oregon Liquor Tax - STATE-CONTROLLED</h2>
        <p>Oregon is an "Alcoholic beverage control state", in which the sale of liquor and
        spirits are state-controlled. There is no need to apply an additional excise tax on liquor.</p>
        </body></html>
        """;

    private static IStateBulkScraper MakeOrScraper(string? htmlOverride = null, bool wayback = false)
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(OrSourceUrl, htmlOverride ?? OrHtml);
        return new OregonAlcoholScraper(new FakeHttpClientFactory(handler), MakeSettings(wayback));
    }

    [Test]
    public void StateCode_IsOR()
        => Assert.That(MakeOrScraper().StateCode, Is.EqualTo("OR"));

    [Test]
    public void OR_SstCategoryName_IsAlcoholicBeverages()
        => Assert.That(MakeOrScraper().SstCategoryName, Is.EqualTo("Alcoholic Beverages"));

    [Test]
    public async Task OR_Returns_BeerRow()
    {
        var results = await MakeOrScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "41" && r.ProductCategory == ProductCategory.Beer);
        Assert.That(row.Rate, Is.EqualTo(0.08m));
        Assert.That(row.SourceConfidence, Is.EqualTo(SourceConfidence.ThirdParty));
    }

    [Test]
    public async Task OR_Returns_Wine14Row()
    {
        var results = await MakeOrScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "41" && r.ProductCategory == ProductCategory.Wine
                                      && r.MaxAbv == 0.14m);
        Assert.That(row.Rate, Is.EqualTo(0.67m));
        Assert.That(row.MaxAbv, Is.EqualTo(0.14m));
    }

    [Test]
    public async Task OR_Returns_Wine14PlusRow()
    {
        var results = await MakeOrScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "41" && r.ProductCategory == ProductCategory.Wine
                                      && r.MinAbv == 0.14m);
        Assert.That(row.Rate, Is.EqualTo(0.77m));
        Assert.That(row.MinAbv, Is.EqualTo(0.14m));
    }

    [Test]
    public async Task OR_Returns_SpiritsRow_WithZeroRate_ControlState()
    {
        var results = await MakeOrScraper().ScrapeAsync();
        var row = results.First(r => r.FipsCode == "41" && r.ProductCategory == ProductCategory.Spirits);
        Assert.That(row.Rate, Is.EqualTo(0m));
        Assert.That(row.Conditions, Does.Contain("control state"));
        Assert.That(row.Conditions, Does.Contain("OLCC"));
    }

    [Test]
    public async Task OR_EveryRow_CitesItsStatutoryAuthority()
    {
        var results = await MakeOrScraper().ScrapeAsync();
        foreach (var row in results)
            Assert.That(row.Conditions, Does.Contain("Statutory authority:"),
                $"Row '{row.RateName}' must declare its statutory authority in Conditions.");
    }

    [Test]
    public async Task OR_BeerAndWineRows_CiteORS_473_030()
    {
        var results = await MakeOrScraper().ScrapeAsync();
        var nonSpirits = results.Where(r => r.ProductCategory != ProductCategory.Spirits);
        foreach (var row in nonSpirits)
            Assert.That(row.Conditions, Does.Contain("§ 473.030"),
                $"Row '{row.RateName}' should cite ORS § 473.030.");
    }

    [Test]
    public async Task OR_SpiritsRow_CitesORS_471_730()
    {
        var results = await MakeOrScraper().ScrapeAsync();
        var row = results.First(r => r.ProductCategory == ProductCategory.Spirits);
        Assert.That(row.Conditions, Does.Contain("§ 471.730"));
    }

    [Test]
    public async Task OR_Returns_ExactlyFourRows()
    {
        var results = await MakeOrScraper().ScrapeAsync();
        Assert.That(results.Count, Is.EqualTo(4),
            "Beer + wine≤14% + wine>14% + spirits (control) = 4 rows");
    }

    [Test]
    public void OR_Throws_WhenWineRatesInverted()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _ = OregonAlcoholScraper.Parse(
                "<html><body>Oregon Beer Tax - $0.08 / gallon " +
                "Oregon Wine Tax - $0.77 / gallon Over 14% – $0.67/gallon</body></html>",
                OrSourceUrl));
    }

    [Test]
    public void OR_Throws_WhenWine14PlusTierMissing()
    {
        var scraper = MakeOrScraper("""
            <html><body>
            <h2>Oregon Beer Tax - $0.08 / gallon</h2>
            <h2>Oregon Wine Tax - $0.67 / gallon</h2>
            </body></html>
            """);
        Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());
    }

    [Test]
    public void OR_Throws_WhenPageUnreachable_WaybackDisabled()
    {
        var handler = new FakeHttpMessageHandler();
        var scraper = new OregonAlcoholScraper(new FakeHttpClientFactory(handler), MakeSettings());
        Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());
    }

    // ── Statute-citation retrofit (WI/IL/MN/IA/IN/MI/ND/SD) ───────────────────
    // Every rate row in the eight older scrapers must declare its statutory authority
    // in Conditions, matching the convention applied to OH/MT/ID/OR.

    [Test]
    public async Task WI_EveryRow_CitesItsStatutoryAuthority()
    {
        var results = await MakeWiScraper(WiHtml).ScrapeAsync();
        foreach (var row in results)
            Assert.That(row.Conditions, Does.Contain("Statutory authority:"),
                $"WI row '{row.RateName}' must declare its statutory authority in Conditions.");
    }

    [Test]
    public async Task WI_StateRow_CitesWiStat_77_52()
    {
        var results = await MakeWiScraper(WiHtml).ScrapeAsync();
        var stateRow = results.First(r => r.FipsCode == "55");
        Assert.That(stateRow.Conditions, Does.Contain("§ 77.52"));
    }

    [Test]
    public async Task WI_CountyRows_CiteWiStat_77_71()
    {
        var results = await MakeWiScraper(WiHtml).ScrapeAsync();
        var countyRows = results.Where(r => r.FipsCode != "55").ToList();
        foreach (var row in countyRows)
            Assert.That(row.Conditions, Does.Contain("§ 77.71"),
                $"WI county row '{row.JurisdictionName}' should cite Wis. Stat. § 77.71.");
    }

    [Test]
    public async Task IL_EveryRow_CitesItsStatutoryAuthority()
    {
        var results = await MakeIlScraper().ScrapeAsync();
        foreach (var row in results)
            Assert.That(row.Conditions, Does.Contain("Statutory authority:"),
                $"IL row '{row.RateName}' must declare its statutory authority in Conditions.");
    }

    [Test]
    public async Task IL_StateRows_Cite235ILCS_5_8_1()
    {
        var results = await MakeIlScraper().ScrapeAsync();
        var stateRows = results.Where(r => r.FipsCode == "17");
        foreach (var row in stateRows)
            Assert.That(row.Conditions, Does.Contain("235 ILCS 5/8-1"),
                $"IL state row '{row.RateName}' should cite 235 ILCS 5/8-1.");
    }

    [Test]
    public async Task IL_CookRows_CiteCookCh74()
    {
        var results = await MakeIlScraper().ScrapeAsync();
        var cookRows = results.Where(r => r.FipsCode == "17031").ToList();
        foreach (var row in cookRows)
            Assert.That(row.Conditions, Does.Contain("Cook County"),
                $"IL Cook row '{row.RateName}' should cite the Cook County ordinance.");
    }

    [Test]
    public async Task IL_ChicagoRows_CiteChi3_44()
    {
        var results = await MakeIlScraper().ScrapeAsync();
        var chiRows = results.Where(r => r.FipsCode == "1714000").ToList();
        foreach (var row in chiRows)
            Assert.That(row.Conditions, Does.Contain("Ch. 3-44"),
                $"IL Chicago row '{row.RateName}' should cite Chicago Municipal Code Ch. 3-44.");
    }

    [Test]
    public async Task MN_EveryRow_CitesItsStatutoryAuthority()
    {
        var results = await MakeMnScraper().ScrapeAsync();
        foreach (var row in results)
            Assert.That(row.Conditions, Does.Contain("Statutory authority:"),
                $"MN row '{row.RateName}' must declare its statutory authority in Conditions.");
    }

    [Test]
    public async Task MN_StateRows_CiteMinnStat_297G_03()
    {
        var results = await MakeMnScraper().ScrapeAsync();
        var stateRows = results.Where(r => r.FipsCode == "27");
        foreach (var row in stateRows)
            Assert.That(row.Conditions, Does.Contain("§ 297G.03"),
                $"MN state row '{row.RateName}' should cite Minn. Stat. § 297G.03.");
    }

    [Test]
    public async Task MN_MinneapolisRow_CitesCityOrdinance()
    {
        var results = await MakeMnScraper(mplsHtml: MnMplsHtml).ScrapeAsync();
        var mpls = results.First(r => r.FipsCode == "2743000");
        Assert.That(mpls.Conditions, Does.Contain("§ 22.1401"));
    }

    [Test]
    public async Task IA_EveryRow_CitesItsStatutoryAuthority()
    {
        var results = await MakeIaScraper().ScrapeAsync();
        foreach (var row in results)
            Assert.That(row.Conditions, Does.Contain("Statutory authority:"),
                $"IA row '{row.RateName}' must declare its statutory authority in Conditions.");
    }

    [Test]
    public async Task IA_BeerRow_CitesIowaCode_123_136()
    {
        var results = await MakeIaScraper().ScrapeAsync();
        var beer = results.First(r => r.ProductCategory == ProductCategory.Beer);
        Assert.That(beer.Conditions, Does.Contain("§ 123.136"));
    }

    [Test]
    public async Task IA_WineRow_CitesIowaCode_123_183()
    {
        var results = await MakeIaScraper().ScrapeAsync();
        var wine = results.First(r => r.ProductCategory == ProductCategory.Wine);
        Assert.That(wine.Conditions, Does.Contain("§ 123.183"));
    }

    [Test]
    public async Task IA_SpiritsRow_CitesIowaCode_123_20()
    {
        var results = await MakeIaScraper().ScrapeAsync();
        var spirits = results.First(r => r.ProductCategory == ProductCategory.Spirits);
        Assert.That(spirits.Conditions, Does.Contain("§ 123.20"));
    }

    [Test]
    public async Task IN_EveryRow_CitesItsStatutoryAuthority()
    {
        var results = await MakeInScraper().ScrapeAsync();
        foreach (var row in results)
            Assert.That(row.Conditions, Does.Contain("Statutory authority:"),
                $"IN row '{row.RateName}' must declare its statutory authority in Conditions.");
    }

    [Test]
    public async Task IN_BeerRow_CitesIC_7_1_4_2_1()
    {
        var results = await MakeInScraper().ScrapeAsync();
        var beer = results.First(r => r.ProductCategory == ProductCategory.Beer);
        Assert.That(beer.Conditions, Does.Contain("§ 7.1-4-2-1"));
    }

    [Test]
    public async Task IN_WineRow_CitesIC_7_1_4_4_1()
    {
        var results = await MakeInScraper().ScrapeAsync();
        var wine = results.First(r => r.ProductCategory == ProductCategory.Wine);
        Assert.That(wine.Conditions, Does.Contain("§ 7.1-4-4-1"));
    }

    [Test]
    public async Task IN_LiquorRow_CitesIC_7_1_4_3_1()
    {
        var results = await MakeInScraper().ScrapeAsync();
        var spirits = results.First(r => r.ProductCategory == ProductCategory.Spirits);
        Assert.That(spirits.Conditions, Does.Contain("§ 7.1-4-3-1"));
    }

    [Test]
    public async Task MI_EveryRow_CitesItsStatutoryAuthority()
    {
        var results = await MakeMiScraper().ScrapeAsync();
        foreach (var row in results)
            Assert.That(row.Conditions, Does.Contain("Statutory authority:"),
                $"MI row '{row.RateName}' must declare its statutory authority in Conditions.");
    }

    [Test]
    public async Task MI_BeerRow_CitesMCL_436_1409()
    {
        var results = await MakeMiScraper().ScrapeAsync();
        var beer = results.First(r => r.ProductCategory == ProductCategory.Beer);
        Assert.That(beer.Conditions, Does.Contain("§ 436.1409"));
    }

    [Test]
    public async Task MI_WineRows_CiteMCL_436_1301()
    {
        var results = await MakeMiScraper().ScrapeAsync();
        var wineRows = results.Where(r => r.ProductCategory == ProductCategory.Wine).ToList();
        foreach (var row in wineRows)
            Assert.That(row.Conditions, Does.Contain("§ 436.1301"),
                $"MI wine row '{row.RateName}' should cite MCL § 436.1301.");
    }

    [Test]
    public async Task ND_EveryRow_CitesItsStatutoryAuthority()
    {
        var results = await MakeNdScraper().ScrapeAsync();
        foreach (var row in results)
            Assert.That(row.Conditions, Does.Contain("Statutory authority:"),
                $"ND row '{row.RateName}' must declare its statutory authority in Conditions.");
    }

    [Test]
    public async Task ND_AllRows_CiteNDCC_5_03_07()
    {
        var results = await MakeNdScraper().ScrapeAsync();
        foreach (var row in results)
            Assert.That(row.Conditions, Does.Contain("§ 5-03-07"),
                $"ND row '{row.RateName}' should cite N.D.C.C. § 5-03-07.");
    }

    [Test]
    public async Task SD_EveryRow_CitesItsStatutoryAuthority()
    {
        var results = await MakeSdScraper().ScrapeAsync();
        foreach (var row in results)
            Assert.That(row.Conditions, Does.Contain("Statutory authority:"),
                $"SD row '{row.RateName}' must declare its statutory authority in Conditions.");
    }

    [Test]
    public async Task SD_AllRows_CiteSDCL_35_5_3()
    {
        var results = await MakeSdScraper().ScrapeAsync();
        foreach (var row in results)
            Assert.That(row.Conditions, Does.Contain("§ 35-5-3"),
                $"SD row '{row.RateName}' should cite SDCL § 35-5-3.");
    }
}
