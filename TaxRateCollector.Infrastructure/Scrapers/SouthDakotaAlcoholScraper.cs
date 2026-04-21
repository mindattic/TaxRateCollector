using System.Text.RegularExpressions;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.Infrastructure.Scrapers;

/// <summary>
/// Bulk scraper for South Dakota alcohol excise taxes (SDCL § 35-5-3).
///   Beer (malt beverages):      $0.27 per gallon
///   Wine ≤ 14% ABV:             $0.93 per gallon
///   Wine 14–20% ABV:            $1.45 per gallon
///   Wine ≥ 21% ABV / sparkling: $2.07 per gallon
///   Spirits:                    $3.93 per gallon
/// Source: https://www.salestaxhandbook.com/south-dakota/alcohol (third-party aggregator)
/// The official DOR page (dor.sd.gov/businesses/taxes/alcohol/) and the legislature statute
/// (sdlegislature.gov/Statutes/35-5-3) do not publish rates in a machine-readable format.
/// All rates are remitted by licensed wholesalers / distributors.
/// </summary>
public sealed class SouthDakotaAlcoholScraper : IStateBulkScraper
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly SettingsService settings;

    public string StateCode => "SD";
    public string? SstCategoryName => "Alcoholic Beverages";

    private const string SourceUrl = "https://www.salestaxhandbook.com/south-dakota/alcohol";

    public SouthDakotaAlcoholScraper(IHttpClientFactory httpClientFactory, SettingsService settings)
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
            requiredContent: "South Dakota Beer Tax");

        var bytes = System.Text.Encoding.UTF8.GetBytes(html);

        var (beer, wine14, wine14To20, wine21Plus, spirits) = Parse(html, urlUsed);

        return
        [
            // Beer — SDCL § 35-5-3
            new("46", "South Dakota",
                "South Dakota Beer Excise Tax (per gallon, SDCL § 35-5-3)",
                beer, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, null, 1.00m, SourceConfidence.ThirdParty,
                ProductCategory.Beer),

            // Wine ≤ 14% ABV — SDCL § 35-5-3
            new("46", "South Dakota",
                "South Dakota Wine Excise Tax — Not More Than 14% ABV (SDCL § 35-5-3)",
                wine14, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, null, 0.14m, SourceConfidence.ThirdParty,
                ProductCategory.Wine),

            // Wine 14–20% ABV — SDCL § 35-5-3
            new("46", "South Dakota",
                "South Dakota Wine Excise Tax — 14% to 20% ABV (SDCL § 35-5-3)",
                wine14To20, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, 0.14m, 0.20m, SourceConfidence.ThirdParty,
                ProductCategory.Wine),

            // Wine ≥ 21% ABV and sparkling — SDCL § 35-5-3
            new("46", "South Dakota",
                "South Dakota Wine Excise Tax — Over 21% ABV and Sparkling Wine (SDCL § 35-5-3)",
                wine21Plus, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, 0.21m, null, SourceConfidence.ThirdParty,
                ProductCategory.Wine),

            // Spirits — SDCL § 35-5-3
            new("46", "South Dakota",
                "South Dakota Spirits Excise Tax (per gallon, SDCL § 35-5-3)",
                spirits, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, null, null, SourceConfidence.ThirdParty,
                ProductCategory.Spirits),
        ];
    }

    internal static (decimal Beer, decimal Wine14, decimal Wine14To20, decimal Wine21Plus, decimal Spirits)
        Parse(string html, string urlUsed)
    {
        var text = Regex.Replace(html, @"<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ");

        // "South Dakota Beer Tax - $0.27 / gallon"
        var beerM = Regex.Match(text,
            @"south\s+dakota\s+beer\s+tax\s+-\s+\$\s*([\d.]+)",
            RegexOptions.IgnoreCase);
        if (!beerM.Success)
            throw new InvalidOperationException(
                $"Could not parse beer excise rate from {urlUsed}. The page structure may have changed.");
        var beer = decimal.Parse(beerM.Groups[1].Value);

        // "South Dakota Wine Tax - $0.93 / gallon"
        var wineM = Regex.Match(text,
            @"south\s+dakota\s+wine\s+tax\s+-\s+\$\s*([\d.]+)",
            RegexOptions.IgnoreCase);
        if (!wineM.Success)
            throw new InvalidOperationException(
                $"Could not parse wine excise rate from {urlUsed}. The page structure may have changed.");
        var wine14 = decimal.Parse(wineM.Groups[1].Value);

        // "14% to 20% – $1.45/gallon"
        var wine14To20M = Regex.Match(text,
            @"14\s*%\s*to\s*20\s*%\s*[-–]\s*\$([\d.]+)/gallon",
            RegexOptions.IgnoreCase);
        if (!wine14To20M.Success)
            throw new InvalidOperationException(
                $"Could not parse wine 14-20% excise rate from {urlUsed}. The page structure may have changed.");
        var wine14To20 = decimal.Parse(wine14To20M.Groups[1].Value);

        // "over 21% and sparkling wine – $2.07/gallon"
        var wine21PlusM = Regex.Match(text,
            @"over\s+21\s*%\s*and\s*sparkling\s*wine\s*[-–]\s*\$([\d.]+)/gallon",
            RegexOptions.IgnoreCase);
        if (!wine21PlusM.Success)
            throw new InvalidOperationException(
                $"Could not parse wine >21%/sparkling excise rate from {urlUsed}. The page structure may have changed.");
        var wine21Plus = decimal.Parse(wine21PlusM.Groups[1].Value);

        // "South Dakota Liquor Tax - $3.93 / gallon"
        var spiritsM = Regex.Match(text,
            @"south\s+dakota\s+liquor\s+tax\s+-\s+\$\s*([\d.]+)",
            RegexOptions.IgnoreCase);
        if (!spiritsM.Success)
            throw new InvalidOperationException(
                $"Could not parse spirits excise rate from {urlUsed}. The page structure may have changed.");
        var spirits = decimal.Parse(spiritsM.Groups[1].Value);

        if (beer <= 0 || wine14 <= 0 || wine14To20 <= wine14 || wine21Plus <= wine14To20 || spirits <= 0)
            throw new InvalidOperationException(
                $"SD rate sanity check failed: beer={beer:C4} wine14={wine14:C4} wine14-20={wine14To20:C4} wine21+={wine21Plus:C4} spirits={spirits:C4}");

        return (beer, wine14, wine14To20, wine21Plus, spirits);
    }
}
