using System.Text.RegularExpressions;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.Infrastructure.Scrapers;

/// <summary>
/// Bulk scraper for Iowa alcohol excise taxes. Fetches live data from:
///   1. Iowa DOR — Tax/Fee Descriptions and Rates (beer and wine gallonage tax)
///        Beer:  $5.89 per 31-gallon barrel  (Iowa Code § 123.136)
///        Wine:  $1.75 per gallon — flat rate, all ABV tiers (Iowa Code § 123.183)
///   2. salestaxhandbook.com/iowa/alcohol (third-party) — confirms Iowa spirits are
///        state-controlled. Iowa ABD is the exclusive wholesaler; no per-gallon excise
///        applies. A $0.00 evidence record is stored so the control-state status is
///        visible in the excise panel.
/// All rates are remitted by licensed wholesalers / distributors. Iowa SST member.
/// Beer/wine source: https://revenue.iowa.gov/taxes/tax-guidance/general/iowa-taxfee-descriptions-and-rates
/// Spirits source:   https://www.salestaxhandbook.com/iowa/alcohol
/// </summary>
public sealed class IowaAlcoholScraper : IStateBulkScraper
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly SettingsService settings;

    public string StateCode => "IA";
    public string? SstCategoryName => "Alcoholic Beverages";

    private const string SourceUrl   = "https://revenue.iowa.gov/taxes/tax-guidance/general/iowa-taxfee-descriptions-and-rates";
    private const string SpiritsUrl  = "https://www.salestaxhandbook.com/iowa/alcohol";

    public IowaAlcoholScraper(IHttpClientFactory httpClientFactory, SettingsService settings)
    {
        this.httpClientFactory = httpClientFactory;
        this.settings = settings;
    }

    public async Task<IReadOnlyList<BulkRateResult>> ScrapeAsync(CancellationToken ct = default)
    {
        var http    = httpClientFactory.CreateClient();
        var wayback = settings.Current.WaybackMachineFallback;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TaxRateCollector/1.0 (tax compliance research)");

        // Page text reads "$5.89 per 31 gallon barrel" — match on "5.89" so the check survives
        // both punctuation drift ("31-gallon" vs "31 gallon") and minor wording changes.
        var beerWineTask = ScraperHttpHelper.GetRequiredStringAsync(
            http, SourceUrl, wayback, ct, requiredContent: "5.89");
        var spiritsTask = ScraperHttpHelper.GetRequiredStringAsync(
            http, SpiritsUrl, wayback, ct, requiredContent: "STATE-CONTROLLED");
        await Task.WhenAll(beerWineTask, spiritsTask);

        var (html, urlUsed)               = beerWineTask.Result;
        var (spiritsHtml, spiritsUrlUsed) = spiritsTask.Result;

        var bytes        = System.Text.Encoding.UTF8.GetBytes(html);
        var spiritsBytes = System.Text.Encoding.UTF8.GetBytes(spiritsHtml);

        var conf        = urlUsed.Contains("archive.org", StringComparison.OrdinalIgnoreCase)
            ? SourceConfidence.Archive : SourceConfidence.Official;

        var (beer, wine) = Parse(html, urlUsed);

        return
        [
            // Beer excise — Iowa Code § 123.136
            // $5.89/31-gal barrel → per-gallon rate
            new("19", "Iowa",
                "Iowa Beer Excise Tax (per gallon, $5.89/31-gal barrel, Iowa Code § 123.136)",
                beer, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, null, 1.00m, conf,
                ProductCategory.Beer,
                Conditions: "Statutory authority: Iowa Code § 123.136 (beer barrel tax). Rate: $5.89 per 31-gallon barrel (~$0.19 per gallon)."),

            // Wine gallonage tax — Iowa Code § 123.183 — flat $1.75/gal, all ABV tiers
            new("19", "Iowa",
                "Iowa Wine Gallonage Tax — All ABV Tiers (Iowa Code § 123.183)",
                wine, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, null, null, conf,
                ProductCategory.Wine,
                Conditions: "Statutory authority: Iowa Code § 123.183 (wine gallonage tax). Rate: $1.75 per gallon, applied flat across all ABV tiers."),

            // Spirits — Iowa ABD is the exclusive wholesaler (control state).
            // No statutory per-gallon excise; state revenue is embedded in ABD wholesale markup.
            // $0.00 entry records this determination with third-party evidence.
            new("19", "Iowa",
                "Iowa Spirits — State-Controlled (No Per-Gallon Excise)",
                0m, spiritsUrlUsed, spiritsBytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: false, SaleContext.Any, null, null, SourceConfidence.ThirdParty,
                ProductCategory.Spirits,
                Conditions: "Statutory authority: Iowa Code § 123.20 establishes the Iowa Alcoholic Beverages Division (ABD) as the exclusive spirits wholesaler. Iowa is a control state: no per-gallon excise applies; state revenue is embedded in the ABD wholesale markup."),
        ];
    }

    internal static (decimal Beer, decimal Wine) Parse(string html, string urlUsed)
    {
        var text = Regex.Replace(html, @"<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ");

        // Beer: "$5.89 per 31-gallon barrel"
        var beerM = Regex.Match(text,
            @"\$\s*(\d+\.\d+)\s*per\s*31\s*[-–]?\s*gallon\s+barrel",
            RegexOptions.IgnoreCase);
        if (!beerM.Success)
            throw new InvalidOperationException(
                $"Could not parse beer excise barrel rate from {urlUsed}. The page structure may have changed.");
        var beerBarrel = decimal.Parse(beerM.Groups[1].Value);
        var beer = beerBarrel / 31m;

        // Wine: "$1.75 per gallon" — appears in the wine gallonage section
        var wineM = Regex.Match(text,
            @"wine\b.{0,800}?\$\s*(\d+\.\d+)\s*per\s*gallon",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!wineM.Success)
            throw new InvalidOperationException(
                $"Could not parse wine gallonage rate from {urlUsed}. The page structure may have changed.");
        var wine = decimal.Parse(wineM.Groups[1].Value);

        if (beer <= 0 || wine <= 0)
            throw new InvalidOperationException(
                $"Iowa rate sanity check failed: beer={beer:C4} wine={wine:C4}");

        return (beer, wine);
    }
}
