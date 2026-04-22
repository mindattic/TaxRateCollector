using System.Text.RegularExpressions;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.Infrastructure.Scrapers;

/// <summary>
/// Bulk scraper for Idaho alcohol excise taxes.
///   Beer (≤ 4% ABV by weight):  $0.15 per gallon  (Idaho Code § 23-1008)
///   Strong beer (> 4% ABV):     $0.45 per gallon  (taxed as wine; Idaho Code § 23-1319)
///   Wine:                       $0.45 per gallon  (Idaho Code § 23-1319)
///   Spirits: control state — Idaho State Liquor Division is the sole
///            wholesaler / retailer. No separate per-gallon excise statute.
///            Emitted as $0.00 row + ThirdParty confidence.
/// All rates are remitted by licensed wholesalers / distributors.
/// Source: https://www.salestaxhandbook.com/idaho/alcohol (third-party aggregator).
/// </summary>
public sealed class IdahoAlcoholScraper : IStateBulkScraper
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly SettingsService settings;

    public string StateCode => "ID";
    public string? SstCategoryName => "Alcoholic Beverages";

    private const string SourceUrl = "https://www.salestaxhandbook.com/idaho/alcohol";

    public IdahoAlcoholScraper(IHttpClientFactory httpClientFactory, SettingsService settings)
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
            requiredContent: "Idaho Beer Tax");

        var bytes = System.Text.Encoding.UTF8.GetBytes(html);

        var (beer, strongBeer, wine) = Parse(html, urlUsed);

        return
        [
            // Beer ≤ 4% ABV — Idaho Code § 23-1008
            // Idaho measures the threshold by weight (4% ABW ≈ 5% ABV); the rate row
            // uses the ABV decimal that approximates the statutory boundary.
            new("16", "Idaho",
                "Idaho Beer Excise Tax — Standard (≤ 4% ABW, Idaho Code § 23-1008)",
                beer, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, null, 0.05m, SourceConfidence.ThirdParty,
                ProductCategory.Beer,
                Conditions: "Statutory authority: Idaho Code § 23-1008 (beer tax). Rate: $0.15 per gallon for beer of 4% alcohol by weight or less (~5% ABV)."),

            // Strong beer > 4% ABW — taxed as wine under Idaho Code § 23-1319
            new("16", "Idaho",
                "Idaho Strong Beer Excise Tax — > 4% ABW, taxed as wine (Idaho Code § 23-1319)",
                strongBeer, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, 0.05m, null, SourceConfidence.ThirdParty,
                ProductCategory.Beer,
                Conditions: "Statutory authority: Idaho Code § 23-1319 (wine tax, applied to beer over 4% alcohol by weight under § 23-1008). Beer over 4% alcohol by weight (≈ 5% ABV) is taxed at the wine rate of $0.45 per gallon."),

            // Wine — Idaho Code § 23-1319
            new("16", "Idaho",
                "Idaho Wine Excise Tax (per gallon, Idaho Code § 23-1319)",
                wine, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, null, null, SourceConfidence.ThirdParty,
                ProductCategory.Wine,
                Conditions: "Statutory authority: Idaho Code § 23-1319 (wine tax). Rate: $0.45 per gallon."),

            // Spirits — control state (Idaho State Liquor Division)
            new("16", "Idaho",
                "Idaho Spirits Excise Tax — Control State (Idaho Code § 23-202, Idaho State Liquor Division)",
                0m, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, null, null, SourceConfidence.ThirdParty,
                ProductCategory.Spirits,
                Conditions: "Statutory authority: Idaho Code § 23-202 establishes the Idaho State Liquor Division as the sole wholesaler/retailer of spirits. Idaho is a control state: spirits are sold exclusively through state-run liquor stores. No separate per-gallon excise tax applies; the state's wholesale markup is the effective levy."),
        ];
    }

    internal static (decimal Beer, decimal StrongBeer, decimal Wine)
        Parse(string html, string urlUsed)
    {
        var text = Regex.Replace(html, @"<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ");

        // "Idaho Beer Tax - $0.15 / gallon"
        var beerM = Regex.Match(text,
            @"idaho\s+beer\s+tax\s+-\s+\$\s*([\d.]+)",
            RegexOptions.IgnoreCase);
        if (!beerM.Success)
            throw new InvalidOperationException(
                $"Could not parse beer excise rate from {urlUsed}. The page structure may have changed.");
        var beer = decimal.Parse(beerM.Groups[1].Value);

        // "Over 4% – $0.45/gallon" (en-dash or regular dash)
        var strongM = Regex.Match(text,
            @"over\s+4\s*%\s*[-–]\s*\$([\d.]+)/gallon",
            RegexOptions.IgnoreCase);
        if (!strongM.Success)
            throw new InvalidOperationException(
                $"Could not parse strong-beer (>4%) rate from {urlUsed}. The page structure may have changed.");
        var strongBeer = decimal.Parse(strongM.Groups[1].Value);

        // "Idaho Wine Tax - $0.45 / gallon"
        var wineM = Regex.Match(text,
            @"idaho\s+wine\s+tax\s+-\s+\$\s*([\d.]+)",
            RegexOptions.IgnoreCase);
        if (!wineM.Success)
            throw new InvalidOperationException(
                $"Could not parse wine excise rate from {urlUsed}. The page structure may have changed.");
        var wine = decimal.Parse(wineM.Groups[1].Value);

        if (beer <= 0 || strongBeer <= beer || wine <= 0)
            throw new InvalidOperationException(
                $"ID rate sanity check failed: beer={beer:C4} strongBeer={strongBeer:C4} wine={wine:C4}");

        return (beer, strongBeer, wine);
    }
}
