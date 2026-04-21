using System.Text.RegularExpressions;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.Infrastructure.Scrapers;

/// <summary>
/// Bulk scraper for Michigan alcohol excise taxes. Fetches live data from two
/// Michigan Legislature statute pages:
///   1. MCL § 436.1409 — Beer specific tax: $6.30 per barrel
///        Per-gallon equivalent = $6.30 ÷ 31 ≈ $0.2032 per gallon
///   2. MCL § 436.1301 — Wine specific tax:
///        ≤ 16% ABV: 13.5 cents per liter  ($0.135 × 3.78541 ≈ $0.5110 per gallon)
///        &gt; 16% ABV: 20 cents per liter   ($0.20  × 3.78541 ≈ $0.7571 per gallon)
///   Spirits: Michigan Liquor Control Commission is the exclusive wholesaler
///            (control state). No separate per-gallon excise statute. Not modeled here.
/// All rates are remitted by licensed wholesalers / distributors. Michigan SST member.
/// Beer source:  https://legislature.mi.gov/Laws/MCL?objectName=MCL-436-1409
/// Wine source:  https://legislature.mi.gov/Laws/MCL?objectName=MCL-436-1301
/// </summary>
public sealed class MichiganAlcoholScraper : IStateBulkScraper
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly SettingsService settings;

    public string StateCode => "MI";
    public string? SstCategoryName => "Alcoholic Beverages";

    private const string BeerUrl = "https://legislature.mi.gov/Laws/MCL?objectName=MCL-436-1409";
    private const string WineUrl = "https://legislature.mi.gov/Laws/MCL?objectName=MCL-436-1301";

    private const decimal GallonsPerBarrel = 31m;
    private const decimal LitersPerGallon  = 3.78541m;

    public MichiganAlcoholScraper(IHttpClientFactory httpClientFactory, SettingsService settings)
    {
        this.httpClientFactory = httpClientFactory;
        this.settings = settings;
    }

    public async Task<IReadOnlyList<BulkRateResult>> ScrapeAsync(CancellationToken ct = default)
    {
        var http    = httpClientFactory.CreateClient();
        var wayback = settings.Current.WaybackMachineFallback;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TaxRateCollector/1.0 (tax compliance research)");

        var beerTask = ScraperHttpHelper.GetRequiredStringAsync(
            http, BeerUrl, wayback, ct, requiredContent: "barrel");
        var wineTask = ScraperHttpHelper.GetRequiredStringAsync(
            http, WineUrl, wayback, ct, requiredContent: "cents per liter");
        await Task.WhenAll(beerTask, wineTask);

        var (beerHtml, beerUrlUsed) = beerTask.Result;
        var (wineHtml, wineUrlUsed) = wineTask.Result;

        var beerBytes = System.Text.Encoding.UTF8.GetBytes(beerHtml);
        var wineBytes = System.Text.Encoding.UTF8.GetBytes(wineHtml);

        var beerConf = beerUrlUsed.Contains("archive.org", StringComparison.OrdinalIgnoreCase)
            ? SourceConfidence.Archive : SourceConfidence.Official;
        var wineConf = wineUrlUsed.Contains("archive.org", StringComparison.OrdinalIgnoreCase)
            ? SourceConfidence.Archive : SourceConfidence.Official;

        var beerGallon = ParseBeer(beerHtml, beerUrlUsed);
        var (wine16, wine16Plus) = ParseWine(wineHtml, wineUrlUsed);

        return
        [
            // Beer specific tax — MCL § 436.1409
            // $6.30/barrel ÷ 31 gal/barrel → per-gallon rate
            new("26", "Michigan",
                "Michigan Beer Specific Tax (per gallon, $6.30/31-gal barrel, MCL § 436.1409)",
                beerGallon, beerUrlUsed, beerBytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, null, 1.00m, beerConf,
                ProductCategory.Beer),

            // Wine specific tax — MCL § 436.1301 — not more than 16% ABV
            // 13.5 cents/liter × 3.78541 L/gal → per-gallon rate
            new("26", "Michigan",
                "Michigan Wine Specific Tax — Not More Than 16% ABV (MCL § 436.1301)",
                wine16, wineUrlUsed, wineBytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, null, 0.16m, wineConf,
                ProductCategory.Wine),

            // Wine specific tax — MCL § 436.1301 — more than 16% ABV
            // 20 cents/liter × 3.78541 L/gal → per-gallon rate
            new("26", "Michigan",
                "Michigan Wine Specific Tax — More Than 16% ABV (MCL § 436.1301)",
                wine16Plus, wineUrlUsed, wineBytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, 0.16m, null, wineConf,
                ProductCategory.Wine),
        ];
    }

    internal static decimal ParseBeer(string html, string urlUsed)
    {
        var text = Normalize(html);

        // Statute text: "tax of $6.30 per barrel" or "rate of $6.30 per barrel"
        var m = Regex.Match(text,
            @"\$\s*(\d+\.\d+)\s*per\s*barrel",
            RegexOptions.IgnoreCase);
        if (!m.Success)
            m = Regex.Match(text,
                @"(?:tax|rate)\s*of\s*\$\s*(\d+\.\d+)",
                RegexOptions.IgnoreCase);
        if (!m.Success)
            throw new InvalidOperationException(
                $"Could not parse beer barrel tax rate from {urlUsed}. The page structure may have changed.");

        var barrel = decimal.Parse(m.Groups[1].Value);
        return barrel / GallonsPerBarrel;
    }

    internal static (decimal Wine16, decimal Wine16Plus) ParseWine(string html, string urlUsed)
    {
        var text = Normalize(html);

        // MCL § 436.1301 always lists the ≤16% tier first, then the >16% tier.
        // "not more than 16" and "more than 16" both contain "more than 16", so a context
        // regex is ambiguous — instead collect all "N cents per liter" values in document order
        // and take the first as the low tier and the second as the high tier.
        var allMatches = Regex.Matches(text, @"(\d+\.?\d*)\s*cents\s*per\s*liter", RegexOptions.IgnoreCase);
        if (allMatches.Count < 2)
            throw new InvalidOperationException(
                $"Could not parse Michigan wine tier rates from {urlUsed}. " +
                $"Expected ≥2 'cents per liter' values; found {allMatches.Count}. The page structure may have changed.");

        var wine16    = decimal.Parse(allMatches[0].Groups[1].Value) / 100m * LitersPerGallon;
        var wine16Plus = decimal.Parse(allMatches[1].Groups[1].Value) / 100m * LitersPerGallon;

        if (wine16Plus <= wine16)
            throw new InvalidOperationException(
                $"Michigan wine rate ordering check failed: ≤16%={wine16:C4} >16%={wine16Plus:C4}");

        return (wine16, wine16Plus);
    }

    private static string Normalize(string html)
    {
        var text = Regex.Replace(html, @"<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return Regex.Replace(text, @"\s+", " ");
    }
}
