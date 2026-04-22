using System.Text.RegularExpressions;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.Infrastructure.Scrapers;

/// <summary>
/// Bulk scraper for Ohio alcohol excise taxes (Ohio Revised Code Chapter 4301).
///   Beer:            $0.18 per gallon  (ORC § 4301.42)
///   Wine ≤ 14% ABV:  $0.32 per gallon  (ORC § 4301.43 — base rate)
///   Wine 14–21% ABV: $1.00 per gallon  (fortified)
///   Vermouth:        $1.10 per gallon
///   Sparkling wine:  $1.50 per gallon
///   Spirits:         control state — Ohio Division of Liquor Control is the
///                    sole wholesaler / retailer. No separate per-gallon excise
///                    statute. Emitted with rate $0.00 + ThirdParty confidence
///                    so the control-state arrangement is visible in exports.
/// All rates are remitted by licensed wholesalers / distributors; Ohio is an SST member.
/// Source: https://www.salestaxhandbook.com/ohio/alcohol (third-party aggregator).
/// The official Ohio Department of Taxation page publishes rates only in non-machine-
/// readable PDF form, so this scraper uses the aggregator — hence ThirdParty confidence.
/// </summary>
public sealed class OhioAlcoholScraper : IStateBulkScraper
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly SettingsService settings;

    public string StateCode => "OH";
    public string? SstCategoryName => "Alcoholic Beverages";

    private const string SourceUrl = "https://www.salestaxhandbook.com/ohio/alcohol";

    public OhioAlcoholScraper(IHttpClientFactory httpClientFactory, SettingsService settings)
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
            requiredContent: "Ohio Beer Tax");

        var bytes = System.Text.Encoding.UTF8.GetBytes(html);

        var (beer, wineBase, wineFortified, vermouth, sparkling) = Parse(html, urlUsed);

        return
        [
            // Beer — ORC § 4301.42
            new("39", "Ohio",
                "Ohio Beer Excise Tax (per gallon, ORC § 4301.42)",
                beer, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, null, null, SourceConfidence.ThirdParty,
                ProductCategory.Beer,
                Conditions: "Statutory authority: Ohio Revised Code § 4301.42 (beer excise tax). Rate: $0.18 per gallon."),

            // Wine ≤ 14% ABV — ORC § 4301.43 (base rate)
            new("39", "Ohio",
                "Ohio Wine Excise Tax — Not More Than 14% ABV (ORC § 4301.43)",
                wineBase, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, null, 0.14m, SourceConfidence.ThirdParty,
                ProductCategory.Wine,
                Conditions: "Statutory authority: Ohio Revised Code § 4301.43 (wine excise tax — base tier, ≤14% ABV)."),

            // Fortified wine — 14–21% ABV
            new("39", "Ohio",
                "Ohio Wine Excise Tax — Fortified (14–21% ABV, ORC § 4301.43)",
                wineFortified, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, 0.14m, 0.21m, SourceConfidence.ThirdParty,
                ProductCategory.Wine,
                Conditions: "Statutory authority: Ohio Revised Code § 4301.43 (wine excise tax — fortified tier, 14–21% ABV)."),

            // Vermouth
            new("39", "Ohio",
                "Ohio Vermouth Excise Tax (per gallon, ORC § 4301.43)",
                vermouth, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, null, 0.21m, SourceConfidence.ThirdParty,
                ProductCategory.Wine,
                Conditions: "Statutory authority: Ohio Revised Code § 4301.43 (vermouth — aromatized wine)."),

            // Sparkling wine / champagne
            new("39", "Ohio",
                "Ohio Sparkling Wine Excise Tax (per gallon, ORC § 4301.43)",
                sparkling, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, null, null, SourceConfidence.ThirdParty,
                ProductCategory.Wine,
                Conditions: "Statutory authority: Ohio Revised Code § 4301.43 (sparkling wine / champagne)."),

            // Spirits — control state (Ohio Division of Liquor Control)
            new("39", "Ohio",
                "Ohio Spirits Excise Tax — Control State (ORC § 4301.10, Division of Liquor Control)",
                0m, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, null, null, SourceConfidence.ThirdParty,
                ProductCategory.Spirits,
                Conditions: "Statutory authority: Ohio Revised Code § 4301.10 establishes the Division of Liquor Control as the sole wholesaler of spirits. Ohio is a control state: spirits are sold exclusively through state-run agency stores. No separate per-gallon excise tax applies; the state's wholesale markup is the effective levy."),
        ];
    }

    internal static (decimal Beer, decimal WineBase, decimal WineFortified,
                     decimal Vermouth, decimal Sparkling)
        Parse(string html, string urlUsed)
    {
        var text = Regex.Replace(html, @"<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ");

        // "Ohio Beer Tax - $0.18 / gallon"
        var beerM = Regex.Match(text,
            @"ohio\s+beer\s+tax\s+-\s+\$\s*([\d.]+)",
            RegexOptions.IgnoreCase);
        if (!beerM.Success)
            throw new InvalidOperationException(
                $"Could not parse beer excise rate from {urlUsed}. The page structure may have changed.");
        var beer = decimal.Parse(beerM.Groups[1].Value);

        // "Ohio Wine Tax - $0.32 / gallon" — base tier (≤14% ABV)
        var wineM = Regex.Match(text,
            @"ohio\s+wine\s+tax\s+-\s+\$\s*([\d.]+)",
            RegexOptions.IgnoreCase);
        if (!wineM.Success)
            throw new InvalidOperationException(
                $"Could not parse wine excise rate from {urlUsed}. The page structure may have changed.");
        var wineBase = decimal.Parse(wineM.Groups[1].Value);

        // Fortified wine — "14% to 21%" or "14-21%" variants followed by a per-gallon $ value.
        var fortifiedM = Regex.Match(text,
            @"14\s*%?\s*(?:to|[-–])\s*21\s*%[^$]{0,60}?\$\s*([\d.]+)\s*/?\s*gallon",
            RegexOptions.IgnoreCase);
        if (!fortifiedM.Success)
            throw new InvalidOperationException(
                $"Could not parse fortified wine (14–21%) rate from {urlUsed}. The page structure may have changed.");
        var wineFortified = decimal.Parse(fortifiedM.Groups[1].Value);

        // Vermouth — "vermouth ... $1.10/gallon" (may appear before or after the $ amount)
        var vermouthM = Regex.Match(text,
            @"vermouth[^$]{0,60}?\$\s*([\d.]+)\s*/?\s*gallon",
            RegexOptions.IgnoreCase);
        if (!vermouthM.Success)
            vermouthM = Regex.Match(text,
                @"\$\s*([\d.]+)\s*/?\s*gallon[^$]{0,60}?vermouth",
                RegexOptions.IgnoreCase);
        if (!vermouthM.Success)
            throw new InvalidOperationException(
                $"Could not parse vermouth rate from {urlUsed}. The page structure may have changed.");
        var vermouth = decimal.Parse(vermouthM.Groups[1].Value);

        // Sparkling — "sparkling ... $1.50/gallon" (either order)
        var sparklingM = Regex.Match(text,
            @"sparkling[^$]{0,60}?\$\s*([\d.]+)\s*/?\s*gallon",
            RegexOptions.IgnoreCase);
        if (!sparklingM.Success)
            sparklingM = Regex.Match(text,
                @"\$\s*([\d.]+)\s*/?\s*gallon[^$]{0,60}?sparkling",
                RegexOptions.IgnoreCase);
        if (!sparklingM.Success)
            throw new InvalidOperationException(
                $"Could not parse sparkling wine rate from {urlUsed}. The page structure may have changed.");
        var sparkling = decimal.Parse(sparklingM.Groups[1].Value);

        // Tier sanity: every tier must be positive, and the three escalating wine tiers
        // must strictly ascend: base < fortified < sparkling. Vermouth sits between
        // fortified and sparkling on the real page but isn't guaranteed to — only check
        // it's positive. If the page ever reorders tiers, this catches it.
        if (beer <= 0 || wineBase <= 0 || wineFortified <= wineBase
            || sparkling <= wineFortified || vermouth <= 0)
            throw new InvalidOperationException(
                $"OH rate sanity check failed: beer={beer:C4} wineBase={wineBase:C4} " +
                $"wineFortified={wineFortified:C4} vermouth={vermouth:C4} sparkling={sparkling:C4}");

        return (beer, wineBase, wineFortified, vermouth, sparkling);
    }
}
