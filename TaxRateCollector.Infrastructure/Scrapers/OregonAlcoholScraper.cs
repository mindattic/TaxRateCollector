using System.Text.RegularExpressions;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.Infrastructure.Scrapers;

/// <summary>
/// Bulk scraper for Oregon alcohol excise taxes (ORS Chapter 473).
///   Beer:           $0.08 per gallon  (ORS § 473.030 — statute is $2.60/barrel,
///                                       ≈ $0.0839/gal; aggregator rounds to $0.08)
///   Wine ≤ 14% ABV: $0.67 per gallon  (ORS § 473.030)
///   Wine > 14% ABV: $0.77 per gallon
///   Spirits: control state — Oregon Liquor and Cannabis Commission (OLCC) is
///            the sole wholesaler. No separate per-gallon excise statute.
///            Emitted as $0.00 row + ThirdParty confidence.
/// Oregon has no general sales tax; alcohol excise is collected from manufacturers
/// and importers (privilege tax) and is fully embedded in wholesale price.
/// Source: https://www.salestaxhandbook.com/oregon/alcohol (third-party aggregator).
/// </summary>
public sealed class OregonAlcoholScraper : IStateBulkScraper
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly SettingsService settings;

    public string StateCode => "OR";
    public string? SstCategoryName => "Alcoholic Beverages";

    private const string SourceUrl = "https://www.salestaxhandbook.com/oregon/alcohol";

    public OregonAlcoholScraper(IHttpClientFactory httpClientFactory, SettingsService settings)
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
            requiredContent: "Oregon Beer Tax");

        var bytes = System.Text.Encoding.UTF8.GetBytes(html);

        var (beer, wine14, wine14Plus) = Parse(html, urlUsed);

        return
        [
            // Beer — ORS § 473.030 (Privilege tax — manufacturer/importer remits)
            new("41", "Oregon",
                "Oregon Beer Excise Tax (per gallon, ORS § 473.030)",
                beer, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, null, null, SourceConfidence.ThirdParty,
                ProductCategory.Beer,
                Conditions: "Statutory authority: Oregon Revised Statutes § 473.030 (privilege tax on malt beverages). Rate: $0.08 per gallon (statute: $2.60 per 31-gallon barrel)."),

            // Wine ≤ 14% ABV — ORS § 473.030
            new("41", "Oregon",
                "Oregon Wine Excise Tax — Not More Than 14% ABV (ORS § 473.030)",
                wine14, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, null, 0.14m, SourceConfidence.ThirdParty,
                ProductCategory.Wine,
                Conditions: "Statutory authority: Oregon Revised Statutes § 473.030 (privilege tax on wines, ≤14% ABV). Rate: $0.67 per gallon."),

            // Wine > 14% ABV — ORS § 473.030
            new("41", "Oregon",
                "Oregon Wine Excise Tax — More Than 14% ABV (ORS § 473.030)",
                wine14Plus, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, 0.14m, null, SourceConfidence.ThirdParty,
                ProductCategory.Wine,
                Conditions: "Statutory authority: Oregon Revised Statutes § 473.030 (privilege tax on wines, >14% ABV). Rate: $0.77 per gallon."),

            // Spirits — control state (Oregon Liquor and Cannabis Commission)
            new("41", "Oregon",
                "Oregon Spirits Excise Tax — Control State (ORS § 471.730, OLCC)",
                0m, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, null, null, SourceConfidence.ThirdParty,
                ProductCategory.Spirits,
                Conditions: "Statutory authority: Oregon Revised Statutes § 471.730 (powers and duties of the Oregon Liquor and Cannabis Commission). Oregon is a control state: spirits are sold exclusively through OLCC-licensed liquor stores. No separate per-gallon excise tax applies; the state's wholesale markup is the effective levy. Oregon also imposes no general sales tax."),
        ];
    }

    internal static (decimal Beer, decimal Wine14, decimal Wine14Plus)
        Parse(string html, string urlUsed)
    {
        var text = Regex.Replace(html, @"<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ");

        // "Oregon Beer Tax - $0.08 / gallon"
        var beerM = Regex.Match(text,
            @"oregon\s+beer\s+tax\s+-\s+\$\s*([\d.]+)",
            RegexOptions.IgnoreCase);
        if (!beerM.Success)
            throw new InvalidOperationException(
                $"Could not parse beer excise rate from {urlUsed}. The page structure may have changed.");
        var beer = decimal.Parse(beerM.Groups[1].Value);

        // "Oregon Wine Tax - $0.67 / gallon"
        var wineM = Regex.Match(text,
            @"oregon\s+wine\s+tax\s+-\s+\$\s*([\d.]+)",
            RegexOptions.IgnoreCase);
        if (!wineM.Success)
            throw new InvalidOperationException(
                $"Could not parse wine excise rate from {urlUsed}. The page structure may have changed.");
        var wine14 = decimal.Parse(wineM.Groups[1].Value);

        // "Over 14% – $0.77/gallon"
        var wine14PlusM = Regex.Match(text,
            @"over\s+14\s*%\s*[-–]\s*\$([\d.]+)/gallon",
            RegexOptions.IgnoreCase);
        if (!wine14PlusM.Success)
            throw new InvalidOperationException(
                $"Could not parse wine >14% rate from {urlUsed}. The page structure may have changed.");
        var wine14Plus = decimal.Parse(wine14PlusM.Groups[1].Value);

        if (beer <= 0 || wine14 <= 0 || wine14Plus <= wine14)
            throw new InvalidOperationException(
                $"OR rate sanity check failed: beer={beer:C4} wine14={wine14:C4} wine14+={wine14Plus:C4}");

        return (beer, wine14, wine14Plus);
    }
}
