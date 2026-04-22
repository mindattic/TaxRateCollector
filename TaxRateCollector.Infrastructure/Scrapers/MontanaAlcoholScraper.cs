using System.Text.RegularExpressions;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.Infrastructure.Scrapers;

/// <summary>
/// Bulk scraper for Montana alcohol excise taxes.
///   Beer:    $0.14 per gallon  (MCA § 16-1-406)
///   Wine:    $1.06 per gallon  (MCA § 16-1-411)
///   Spirits: control state — Montana Department of Revenue Liquor Control
///            Division is the sole wholesaler. No separate per-gallon excise
///            statute. Emitted as $0.00 row + ThirdParty confidence so the
///            control-state arrangement is visible in exports.
/// All rates are remitted by licensed wholesalers / distributors.
/// Source: https://www.salestaxhandbook.com/montana/alcohol (third-party aggregator).
/// The official Montana DOR alcohol page is non-machine-readable, hence ThirdParty
/// confidence rather than Official.
/// </summary>
public sealed class MontanaAlcoholScraper : IStateBulkScraper
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly SettingsService settings;

    public string StateCode => "MT";
    public string? SstCategoryName => "Alcoholic Beverages";

    private const string SourceUrl = "https://www.salestaxhandbook.com/montana/alcohol";

    public MontanaAlcoholScraper(IHttpClientFactory httpClientFactory, SettingsService settings)
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
            requiredContent: "Montana Beer Tax");

        var bytes = System.Text.Encoding.UTF8.GetBytes(html);

        var (beer, wine) = Parse(html, urlUsed);

        return
        [
            // Beer — MCA § 16-1-406
            new("30", "Montana",
                "Montana Beer Excise Tax (per gallon, MCA § 16-1-406)",
                beer, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, null, null, SourceConfidence.ThirdParty,
                ProductCategory.Beer,
                Conditions: "Statutory authority: Montana Code Annotated § 16-1-406 (beer license tax). Rate: $0.14 per gallon."),

            // Wine — MCA § 16-1-411
            new("30", "Montana",
                "Montana Wine Excise Tax (per gallon, MCA § 16-1-411)",
                wine, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, null, null, SourceConfidence.ThirdParty,
                ProductCategory.Wine,
                Conditions: "Statutory authority: Montana Code Annotated § 16-1-411 (wine tax). Rate: $1.06 per gallon."),

            // Spirits — control state (Montana DOR Liquor Control Division)
            new("30", "Montana",
                "Montana Spirits Excise Tax — Control State (MCA Title 16, Ch. 2 — Liquor Control Division)",
                0m, urlUsed, bytes, "text/html", "", "",
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, null, null, SourceConfidence.ThirdParty,
                ProductCategory.Spirits,
                Conditions: "Statutory authority: Montana Code Annotated Title 16, Chapter 2 (state liquor warehouse and distribution). Montana is a control state: spirits are sold exclusively through state-licensed agency liquor stores. No separate per-gallon excise tax applies; the state's wholesale markup is the effective levy."),
        ];
    }

    internal static (decimal Beer, decimal Wine) Parse(string html, string urlUsed)
    {
        var text = Regex.Replace(html, @"<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ");

        // "Montana Beer Tax - $0.14 / gallon"
        var beerM = Regex.Match(text,
            @"montana\s+beer\s+tax\s+-\s+\$\s*([\d.]+)",
            RegexOptions.IgnoreCase);
        if (!beerM.Success)
            throw new InvalidOperationException(
                $"Could not parse beer excise rate from {urlUsed}. The page structure may have changed.");
        var beer = decimal.Parse(beerM.Groups[1].Value);

        // "Montana Wine Tax - $1.06 / gallon"
        var wineM = Regex.Match(text,
            @"montana\s+wine\s+tax\s+-\s+\$\s*([\d.]+)",
            RegexOptions.IgnoreCase);
        if (!wineM.Success)
            throw new InvalidOperationException(
                $"Could not parse wine excise rate from {urlUsed}. The page structure may have changed.");
        var wine = decimal.Parse(wineM.Groups[1].Value);

        if (beer <= 0 || wine <= 0)
            throw new InvalidOperationException(
                $"MT rate sanity check failed: beer={beer:C4} wine={wine:C4}");

        return (beer, wine);
    }
}
