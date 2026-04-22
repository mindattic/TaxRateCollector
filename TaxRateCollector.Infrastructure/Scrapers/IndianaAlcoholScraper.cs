using System.Text.RegularExpressions;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.Infrastructure.Scrapers;

/// <summary>
/// Bulk scraper for Indiana alcohol excise taxes. Fetches live data from:
///   Indiana DOR — Miscellaneous Tax Rates page
///        Beer / Hard Cider (≤20% ABV): $0.115 per gallon  (IC § 7.1-4-2-1)
///        Wine (less than 21% ABV):      $0.47  per gallon  (IC § 7.1-4-4-1)
///        Liquor (21% ABV or more):      $2.68  per gallon  (IC § 7.1-4-3-1)
/// All rates are remitted by licensed distributors (ATC permit holders). Indiana SST member.
/// Source: https://www.in.gov/dor/resources/tax-rates-and-reports/rates-fees-and-penalties/miscellaneous-tax-rates/
/// </summary>
public sealed class IndianaAlcoholScraper : IStateBulkScraper
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly SettingsService settings;

    public string StateCode => "IN";
    public string? SstCategoryName => "Alcoholic Beverages";

    private const string SourceUrl = "https://www.in.gov/dor/resources/tax-rates-and-reports/rates-fees-and-penalties/miscellaneous-tax-rates/";

    public IndianaAlcoholScraper(IHttpClientFactory httpClientFactory, SettingsService settings)
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
            requiredContent: "beer");

        var bytes = System.Text.Encoding.UTF8.GetBytes(html);
        var conf  = urlUsed.Contains("archive.org", StringComparison.OrdinalIgnoreCase)
            ? SourceConfidence.Archive : SourceConfidence.Official;

        var (beer, wine, liquor) = Parse(html, urlUsed);

        return
        [
            // Beer / hard cider excise — IC § 7.1-4-2-1
            new("18", "Indiana",
                "Indiana Beer / Hard Cider Excise Tax (per gallon, IC § 7.1-4-2-1)",
                beer, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, null, 0.20m, conf,
                ProductCategory.Beer,
                Conditions: "Statutory authority: Indiana Code § 7.1-4-2-1 (beer/hard cider excise tax). Rate: $0.115 per gallon, applied to beer and hard cider with ABV less than 21%."),

            // Wine excise — IC § 7.1-4-4-1 — less than 21% ABV
            new("18", "Indiana",
                "Indiana Wine Excise Tax — Less Than 21% ABV (IC § 7.1-4-4-1)",
                wine, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, null, 0.21m, conf,
                ProductCategory.Wine,
                Conditions: "Statutory authority: Indiana Code § 7.1-4-4-1 (wine excise tax). Rate: $0.47 per gallon for wine with ABV less than 21%."),

            // Liquor excise — IC § 7.1-4-3-1 — 21% ABV or more
            new("18", "Indiana",
                "Indiana Liquor Excise Tax — 21% ABV or More (IC § 7.1-4-3-1)",
                liquor, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, 0.21m, null, conf,
                ProductCategory.Spirits,
                Conditions: "Statutory authority: Indiana Code § 7.1-4-3-1 (liquor excise tax). Rate: $2.68 per gallon for distilled spirits with ABV of 21% or more."),
        ];
    }

    internal static (decimal Beer, decimal Wine, decimal Liquor) Parse(string html, string urlUsed)
    {
        var text = Regex.Replace(html, @"<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ");

        // Beer: table cell near "Beer" keyword → "$0.115" or "0.115"
        var beerM = Regex.Match(text,
            @"\bbeer\b[^$]{0,120}?\$\s*(0\.\d+)",
            RegexOptions.IgnoreCase);
        if (!beerM.Success)
            beerM = Regex.Match(text,
                @"\$\s*(0\.\d+)[^$]{0,120}?\bbeer\b",
                RegexOptions.IgnoreCase);
        if (!beerM.Success)
            throw new InvalidOperationException(
                $"Could not parse beer excise rate from {urlUsed}. The page structure may have changed.");
        var beer = decimal.Parse(beerM.Groups[1].Value);

        // Wine: "Wine (less than 21%)" → "$0.47"
        var wineM = Regex.Match(text,
            @"wine[^$]{0,200}?less\s*than\s*21[^$]{0,100}?\$\s*(\d+\.\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!wineM.Success)
            wineM = Regex.Match(text,
                @"\$\s*(\d+\.\d+)[^$]{0,200}?wine[^$]{0,100}?less\s*than\s*21",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!wineM.Success)
            throw new InvalidOperationException(
                $"Could not parse wine excise rate from {urlUsed}. The page structure may have changed.");
        var wine = decimal.Parse(wineM.Groups[1].Value);

        // Liquor: "Liquor (21% or more)" or "21% or more" → "$2.68"
        var liquorM = Regex.Match(text,
            @"(?:liquor|spirits?)[^$]{0,200}?21\s*%?\s*or\s*more[^$]{0,100}?\$\s*(\d+\.\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!liquorM.Success)
            liquorM = Regex.Match(text,
                @"21\s*%?\s*or\s*more[^$]{0,100}?\$\s*(\d+\.\d+)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!liquorM.Success)
            liquorM = Regex.Match(text,
                @"\$\s*(\d+\.\d+)[^$]{0,200}?21\s*%?\s*or\s*more",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!liquorM.Success)
            throw new InvalidOperationException(
                $"Could not parse liquor excise rate from {urlUsed}. The page structure may have changed.");
        var liquor = decimal.Parse(liquorM.Groups[1].Value);

        if (beer >= wine || wine >= liquor)
            throw new InvalidOperationException(
                $"Indiana rate ordering check failed: beer={beer:C4} wine={wine:C4} liquor={liquor:C4}");

        return (beer, wine, liquor);
    }
}
