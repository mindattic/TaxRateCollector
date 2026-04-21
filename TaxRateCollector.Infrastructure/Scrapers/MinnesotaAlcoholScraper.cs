using System.Text;
using System.Text.RegularExpressions;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.Infrastructure.Scrapers;

/// <summary>
/// Bulk scraper for Minnesota alcohol excise taxes. Fetches live data from four official sources:
///   1. MN DOR — Fermented Malt Beverage Excise Tax (beer, Minn. Stat. § 297G.03 subd. 1)
///   2. MN DOR — Wine Excise Tax (cider + four wine ABV tiers, Minn. Stat. § 297G.03 subd. 2)
///   3. MN DOR — Distilled Spirits Excise Tax ($5.03/gal, Minn. Stat. § 297G.03 subd. 3)
///   4. City of Minneapolis — Downtown Liquor Tax (3% on-sale, Minneapolis City Ordinance § 22.1401)
///      Optional: falls back to Wayback Machine if the city URL is unreachable.
/// All rates are state-level; per-barrel beer rates are divided by 31 to yield per-gallon.
/// </summary>
public sealed class MinnesotaAlcoholScraper : IStateBulkScraper
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly SettingsService settings;

    public string StateCode => "MN";
    public string? SstCategoryName => "Alcoholic Beverages";

    private const string BeerUrl    = "https://www.revenue.state.mn.us/fermented-malt-beverage-excise-tax";
    private const string WineUrl    = "https://www.revenue.state.mn.us/wine-excise-tax";
    private const string SpiritsUrl = "https://www.revenue.state.mn.us/distilled-spirits-excise-tax";
    private const string MplsUrl    = "https://www.minneapolismn.gov/resident-services/taxes/business-taxes/downtown-liquor-tax/";

    public MinnesotaAlcoholScraper(IHttpClientFactory httpClientFactory, SettingsService settings)
    {
        this.httpClientFactory = httpClientFactory;
        this.settings = settings;
    }

    public async Task<IReadOnlyList<BulkRateResult>> ScrapeAsync(CancellationToken ct = default)
    {
        var http    = httpClientFactory.CreateClient();
        var wayback = settings.Current.WaybackMachineFallback;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TaxRateCollector/1.0 (tax compliance research)");

        var beerTask    = ScraperHttpHelper.GetRequiredStringAsync(http, BeerUrl,    wayback, ct);
        var wineTask    = ScraperHttpHelper.GetRequiredStringAsync(http, WineUrl,    wayback, ct);
        var spiritsTask = ScraperHttpHelper.GetRequiredStringAsync(http, SpiritsUrl, wayback, ct);
        await Task.WhenAll(beerTask, wineTask, spiritsTask);

        var (beerHtml,    beerUrlUsed)    = beerTask.Result;
        var (wineHtml,    wineUrlUsed)    = wineTask.Result;
        var (spiritsHtml, spiritsUrlUsed) = spiritsTask.Result;

        var beerBytes    = Encoding.UTF8.GetBytes(beerHtml);
        var wineBytes    = Encoding.UTF8.GetBytes(wineHtml);
        var spiritsBytes = Encoding.UTF8.GetBytes(spiritsHtml);

        var beer    = ParseBeer(beerHtml);
        var wine    = ParseWine(wineHtml);
        var spirits = ParseSpirits(spiritsHtml);

        var beerConf    = ConfidenceOf(beerUrlUsed);
        var wineConf    = ConfidenceOf(wineUrlUsed);
        var spiritsConf = ConfidenceOf(spiritsUrlUsed);

        // Minneapolis downtown on-sale liquor tax — optional, Wayback-aware
        var mplsFetch = await ScraperHttpHelper.GetOptionalStringAsync(http, MplsUrl, wayback, ct);

        BulkRateResult BeerRow(string label, decimal rate, decimal? mn = null, decimal? mx = null) =>
            new("27", "Minnesota", label, rate, beerUrlUsed, beerBytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, mn, mx, beerConf, ProductCategory.Beer);

        BulkRateResult WineRow(string label, decimal rate, decimal? mn = null, decimal? mx = null,
            ProductCategory cat = ProductCategory.Wine) =>
            new("27", "Minnesota", label, rate, wineUrlUsed, wineBytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, mn, mx, wineConf, cat);

        var results = new List<BulkRateResult>
        {
            // ── Fermented malt beverages (per gallon, converted from $X.XX/31-gal barrel) ──
            BeerRow("Minnesota Excise Tax — Beer ≤3.2% ABW (per gal, $2.40/31-gal bbl)",
                beer.Beer32, mx: 0.032m),
            BeerRow("Minnesota Excise Tax — Beer >3.2% ABW (per gal, $4.60/31-gal bbl)",
                beer.Beer, mn: 0.032m),

            // ── Wine and cider (per gallon, Minn. Stat. § 297G.03 subd. 2) ─────────────────
            WineRow("Minnesota Excise Tax — Cider (0.5–7% ABV)",
                wine.Cider, mn: 0.005m, mx: 0.07m, cat: ProductCategory.Beer),
            WineRow("Minnesota Excise Tax — Wine ≤14% ABV",
                wine.Wine14, mx: 0.14m),
            WineRow("Minnesota Excise Tax — Wine >14%–≤21% ABV",
                wine.Wine21, mn: 0.14m, mx: 0.21m),
            WineRow("Minnesota Excise Tax — Wine >21%–≤24% ABV",
                wine.Wine24, mn: 0.21m, mx: 0.24m),
            WineRow("Minnesota Excise Tax — Wine >24% ABV",
                wine.Wine24Plus, mn: 0.24m),

            // ── Distilled spirits (per gallon, Minn. Stat. § 297G.03 subd. 3) ─────────────
            new("27", "Minnesota",
                "Minnesota Excise Tax — Distilled Spirits",
                spirits, spiritsUrlUsed, spiritsBytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, null, null, spiritsConf,
                ProductCategory.Spirits),
        };

        // ── Minneapolis downtown on-sale liquor tax (3% of sale price) ───────────────────
        if (mplsFetch is var (mplsHtml, mplsUrlUsed))
        {
            var mplsBytes = Encoding.UTF8.GetBytes(mplsHtml);
            var mplsRate  = ParseMinneapolisRate(mplsHtml);
            var mplsConf  = ConfidenceOf(mplsUrlUsed);
            results.Add(new("2743000", "Minneapolis",
                "Minneapolis Downtown Liquor Tax — On-Sale (% of sale price)",
                mplsRate, mplsUrlUsed, mplsBytes, "text/html", "", "",
                RateBasis.Percentage, TaxType.ExciseTax, RemittancePoint.Retailer,
                IsIncludedInPrice: false, SaleContext.OnPremise, null, null, mplsConf,
                ProductCategory.Alcohol));
        }

        return results;
    }

    // ── Parsers ───────────────────────────────────────────────────────────────

    private static (decimal Beer32, decimal Beer) ParseBeer(string html)
    {
        var text = StripAndNormalize(html);

        // DOR page table: "3.2% or less | $2.40" (per 31-gal barrel column)
        var beer32Match = Regex.Match(text,
            @"3\.2\s*%\s*or\s*less.{0,200}?\$\s*(\d+\.\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!beer32Match.Success)
            throw new InvalidOperationException(
                $"Could not parse ≤3.2% malt beverage rate from {BeerUrl}. The page structure may have changed.");
        var beer32 = decimal.Parse(beer32Match.Groups[1].Value) / 31m;

        // DOR page table: "More than 3.2% | $4.60"
        var beerMatch = Regex.Match(text,
            @"(?:more\s*than|>)\s*3\.2\s*%.{0,200}?\$\s*(\d+\.\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!beerMatch.Success)
            throw new InvalidOperationException(
                $"Could not parse >3.2% malt beverage rate from {BeerUrl}. The page structure may have changed.");
        var beer = decimal.Parse(beerMatch.Groups[1].Value) / 31m;

        if (beer <= beer32)
            throw new InvalidOperationException(
                $"Beer >3.2% rate ({beer:C4}) must exceed ≤3.2% rate ({beer32:C4}) — page may have changed.");

        return (beer32, beer);
    }

    private static (decimal Cider, decimal Wine14, decimal Wine21, decimal Wine24, decimal Wine24Plus) ParseWine(string html)
    {
        var text = StripAndNormalize(html);

        // DOR table: "14% ABV or less (except cider) | $0.30"
        var wine14 = RequireDollarRate(text,
            @"14\s*%\s*(?:ABV\s*)?or\s*less.{0,200}?\$\s*(\d+\.\d+)",
            WineUrl, "wine ≤14% ABV");

        // Standalone "Cider | $0.15" row — negative lookahead excludes "(except cider)" parenthetical
        var ciderMatch = Regex.Match(text,
            @"\bcider\b(?!\s*\)).{0,100}?\$\s*(\d+\.\d+)",
            RegexOptions.IgnoreCase);
        var cider = ciderMatch.Success ? decimal.Parse(ciderMatch.Groups[1].Value) : 0.15m;

        // DOR table: ">14% to <21% ABV | $0.95" (old) or "more than 14% but less than 21%" (new)
        var wine21 = RequireDollarRate(text,
            @"(?:more\s*than\s*14|>14)\s*%.{0,30}?(?:less\s*than|<)\s*21\s*%.{0,200}?\$\s*(\d+\.\d+)",
            WineUrl, "wine >14%–≤21% ABV");

        // DOR table: ">21% to <24% ABV | $1.82" (old) or "more than 21% but less than 24%" (new)
        var wine24 = RequireDollarRate(text,
            @"(?:more\s*than\s*21|>21)\s*%.{0,30}?(?:less\s*than|<)\s*24\s*%.{0,200}?\$\s*(\d+\.\d+)",
            WineUrl, "wine >21%–≤24% ABV");

        // DOR table: ">24% ABV | $3.52" (old) or "more than 24%" (new)
        var wine24Plus = RequireDollarRate(text,
            @"(?:more\s*than\s*24|>24)\s*%.{0,200}?\$\s*(\d+\.\d+)",
            WineUrl, "wine >24% ABV");

        if (wine21 <= wine14 || wine24 <= wine21 || wine24Plus <= wine24)
            throw new InvalidOperationException(
                $"Wine rate ordering check failed: ≤14%={wine14} >14–≤21%={wine21} >21–≤24%={wine24} >24%={wine24Plus}");

        return (cider, wine14, wine21, wine24, wine24Plus);
    }

    private static decimal ParseSpirits(string html)
    {
        var text = StripAndNormalize(html);

        // DOR page: "Distilled spirits...: $5.03 per gallon or $1.33 per liter"
        var gallonMatch = Regex.Match(text,
            @"distilled\s*spirits.{0,500}?\$\s*(\d+\.\d+)\s*per\s*gallon",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (gallonMatch.Success)
            return decimal.Parse(gallonMatch.Groups[1].Value);

        // Fallback: per-liter rate × 3.78541
        var literMatch = Regex.Match(text,
            @"distilled\s*spirits.{0,500}?\$\s*(\d+\.\d+)\s*per\s*liter",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (literMatch.Success)
            return decimal.Parse(literMatch.Groups[1].Value) * 3.78541m;

        throw new InvalidOperationException(
            $"Could not parse distilled spirits rate from {SpiritsUrl}. The page structure may have changed.");
    }

    private static decimal ParseMinneapolisRate(string html)
    {
        var text = StripAndNormalize(html);

        var m = Regex.Match(text,
            @"(?:downtown\s*liquor\s*tax|liquor\s*tax\s*is).{0,150}?(\d+(?:\.\d+)?)\s*percent",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success)
            m = Regex.Match(text,
                @"(\d+(?:\.\d+)?)\s*percent\s*(?:on|of)\s*(?:alcoholic|liquor)",
                RegexOptions.IgnoreCase);

        if (m.Success && decimal.TryParse(m.Groups[1].Value, out var pct) && pct > 0)
            return pct / 100m;

        return 0.03m; // 3% fallback — confirmed from Minneapolis City Ordinance § 22.1401
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SourceConfidence ConfidenceOf(string urlUsed) =>
        urlUsed.Contains("archive.org", StringComparison.OrdinalIgnoreCase)
            ? SourceConfidence.Archive
            : SourceConfidence.Official;

    private static decimal RequireDollarRate(string text, string pattern, string url, string label)
    {
        var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success)
            throw new InvalidOperationException(
                $"Could not parse {label} rate from {url}. The page structure may have changed.");
        return decimal.Parse(m.Groups[1].Value);
    }

    private static string StripAndNormalize(string html)
    {
        var text = Regex.Replace(html, @"<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return Regex.Replace(text, @"\s+", " ");
    }
}
