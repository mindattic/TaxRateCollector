using System.Text.RegularExpressions;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.Infrastructure.Scrapers;

/// <summary>
/// Bulk scraper for North Dakota alcohol excise taxes (N.D.C.C. § 5-03-07).
///   Beer (packaged):  $0.16 per gallon  (bulk/draft: $0.08/gal — not modeled)
///   Wine ≤ 17% ABV:   $0.50 per gallon
///   Wine > 17% ABV:   $0.60 per gallon
///   Spirits:          $2.50 per gallon
/// Source: https://www.salestaxhandbook.com/north-dakota/alcohol (third-party aggregator)
/// The official rate page (tax.nd.gov/business/alcohol-tax) and the century code PDF
/// (ndlegis.gov/cencode/t05c03.pdf) do not publish rates in a machine-readable format.
/// All rates are remitted by licensed wholesalers / distributors.
/// </summary>
public sealed class NorthDakotaAlcoholScraper : IStateBulkScraper
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly SettingsService settings;

    public string StateCode => "ND";
    public string? SstCategoryName => "Alcoholic Beverages";

    private const string SourceUrl = "https://www.salestaxhandbook.com/north-dakota/alcohol";

    public NorthDakotaAlcoholScraper(IHttpClientFactory httpClientFactory, SettingsService settings)
    {
        this.httpClientFactory = httpClientFactory;
        this.settings = settings;
    }

    public async Task<IReadOnlyList<BulkRateResult>> ScrapeAsync(CancellationToken ct = default)
    {
        var http    = httpClientFactory.CreateClient();
        var wayback = settings.Current.WaybackMachineFallback;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TaxRateCollector/1.0 (tax compliance research)");

        var (html, urlUsed) = await ScraperHttpHelper.GetRequiredStringAsync(
            http, SourceUrl, wayback, ct,
            requiredContent: "North Dakota Beer Tax");

        var bytes = System.Text.Encoding.UTF8.GetBytes(html);

        var (beer, wine17, wine17Plus, spirits) = Parse(html, urlUsed);

        return
        [
            // Beer — N.D.C.C. § 5-03-07 (packaged; bulk/draft $0.08/gal not modeled)
            new("38", "North Dakota",
                "North Dakota Beer Excise Tax (per gallon, N.D.C.C. § 5-03-07)",
                beer, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, null, 1.00m, SourceConfidence.ThirdParty,
                ProductCategory.Beer,
                Conditions: "Statutory authority: North Dakota Century Code § 5-03-07 (alcoholic beverage wholesale tax — beer). Rate: $0.16 per gallon for packaged beer. Bulk/draft beer is taxed at $0.08 per gallon (not modeled here)."),

            // Wine ≤ 17% ABV — N.D.C.C. § 5-03-07
            new("38", "North Dakota",
                "North Dakota Wine Excise Tax — Not More Than 17% ABV (N.D.C.C. § 5-03-07)",
                wine17, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, null, 0.17m, SourceConfidence.ThirdParty,
                ProductCategory.Wine,
                Conditions: "Statutory authority: North Dakota Century Code § 5-03-07 (alcoholic beverage wholesale tax — wine, ≤17% ABV). Rate: $0.50 per gallon."),

            // Wine > 17% ABV — N.D.C.C. § 5-03-07
            new("38", "North Dakota",
                "North Dakota Wine Excise Tax — More Than 17% ABV (N.D.C.C. § 5-03-07)",
                wine17Plus, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, 0.17m, null, SourceConfidence.ThirdParty,
                ProductCategory.Wine,
                Conditions: "Statutory authority: North Dakota Century Code § 5-03-07 (alcoholic beverage wholesale tax — wine, >17% ABV). Rate: $0.60 per gallon."),

            // Spirits — N.D.C.C. § 5-03-07
            new("38", "North Dakota",
                "North Dakota Spirits Excise Tax (per gallon, N.D.C.C. § 5-03-07)",
                spirits, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, null, null, SourceConfidence.ThirdParty,
                ProductCategory.Spirits,
                Conditions: "Statutory authority: North Dakota Century Code § 5-03-07 (alcoholic beverage wholesale tax — distilled spirits). Rate: $2.50 per gallon."),
        ];
    }

    internal static (decimal Beer, decimal Wine17, decimal Wine17Plus, decimal Spirits)
        Parse(string html, string urlUsed)
    {
        var text = Regex.Replace(html, @"<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ");

        // "North Dakota Beer Tax - $0.16 / gallon"
        var beerM = Regex.Match(text,
            @"north\s+dakota\s+beer\s+tax\s+-\s+\$\s*([\d.]+)",
            RegexOptions.IgnoreCase);
        if (!beerM.Success)
            throw new InvalidOperationException(
                $"Could not parse beer excise rate from {urlUsed}. The page structure may have changed.");
        var beer = decimal.Parse(beerM.Groups[1].Value);

        // "North Dakota Wine Tax - $0.50 / gallon"
        var wineM = Regex.Match(text,
            @"north\s+dakota\s+wine\s+tax\s+-\s+\$\s*([\d.]+)",
            RegexOptions.IgnoreCase);
        if (!wineM.Success)
            throw new InvalidOperationException(
                $"Could not parse wine excise rate from {urlUsed}. The page structure may have changed.");
        var wine17 = decimal.Parse(wineM.Groups[1].Value);

        // "Over 17% – $0.60/gallon" (en-dash or regular dash)
        var wine17PlusM = Regex.Match(text,
            @"over\s+17\s*%\s*[-–]\s*\$([\d.]+)/gallon",
            RegexOptions.IgnoreCase);
        if (!wine17PlusM.Success)
            throw new InvalidOperationException(
                $"Could not parse wine >17% excise rate from {urlUsed}. The page structure may have changed.");
        var wine17Plus = decimal.Parse(wine17PlusM.Groups[1].Value);

        // "North Dakota Liquor Tax - $2.50 / gallon"
        var spiritsM = Regex.Match(text,
            @"north\s+dakota\s+liquor\s+tax\s+-\s+\$\s*([\d.]+)",
            RegexOptions.IgnoreCase);
        if (!spiritsM.Success)
            throw new InvalidOperationException(
                $"Could not parse spirits excise rate from {urlUsed}. The page structure may have changed.");
        var spirits = decimal.Parse(spiritsM.Groups[1].Value);

        if (beer <= 0 || wine17 <= 0 || wine17Plus <= wine17 || spirits <= 0)
            throw new InvalidOperationException(
                $"ND rate sanity check failed: beer={beer:C4} wine17={wine17:C4} wine17+={wine17Plus:C4} spirits={spirits:C4}");

        return (beer, wine17, wine17Plus, spirits);
    }
}
