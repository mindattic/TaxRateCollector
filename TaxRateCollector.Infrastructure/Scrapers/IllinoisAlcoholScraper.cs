using System.Text;
using System.Text.RegularExpressions;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.Infrastructure.Scrapers;

/// <summary>
/// Bulk scraper for Illinois alcohol excise taxes. Fetches live data from three official sources:
///   1. IDOR Excise Tax Rates — Illinois liquor gallonage tax by ABV tier (235 ILCS 5/8-1)
///   2. Cook County — county liquor tax (Ch. 74 Art. IX, eff. 2023-12-14)
///   3. City of Chicago — city liquor tax (MC Ch. 3-44, eff. 2026-03-01)
/// Required URLs fall back to the Wayback Machine if unreachable and WaybackMachineFallback is enabled.
/// The Chicago news URL is optional and Wayback-aware.
/// </summary>
public sealed class IllinoisAlcoholScraper : IStateBulkScraper
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly SettingsService settings;

    public string StateCode => "IL";

    private const string ExciseUrl      = "https://tax.illinois.gov/research/taxrates/excise.html";
    private const string CookUrl        = "https://www.cookcountyil.gov/service/liquor-tax";
    private const string ChicagoUrl     = "https://www.chicago.gov/city/en/depts/fin/supp_info/revenue/tax_list/liquor_tax_.html";
    private const string ChicagoNewsUrl = "https://www.chicago.gov/city/en/depts/fin/provdrs/tax_division/news/2026/january/LiquorTaxChangesEffectiveMarch12026.html";

    private const string ExciseEffective  = "2009-09-01";
    private const string CookEffective    = "2023-12-14";
    private const string ChicagoEffective = "2026-03-01";

    public IllinoisAlcoholScraper(IHttpClientFactory httpClientFactory, SettingsService settings)
    {
        this.httpClientFactory = httpClientFactory;
        this.settings = settings;
    }

    public async Task<IReadOnlyList<BulkRateResult>> ScrapeAsync(CancellationToken ct = default)
    {
        var http    = httpClientFactory.CreateClient();
        var wayback = settings.Current.WaybackMachineFallback;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TaxRateCollector/1.0 (tax compliance research)");

        var exciseTask  = ScraperHttpHelper.GetRequiredStringAsync(http, ExciseUrl,  wayback, ct);
        var cookTask    = ScraperHttpHelper.GetOptionalStringAsync(http, CookUrl,    wayback, ct);
        var chicagoTask = ScraperHttpHelper.GetRequiredStringAsync(http, ChicagoUrl, wayback, ct);
        await Task.WhenAll(exciseTask, cookTask, chicagoTask);

        var (exciseHtml,  exciseUrlUsed)  = exciseTask.Result;
        var (chicagoHtml, chicagoUrlUsed) = chicagoTask.Result;

        // Cook County: rate data may no longer be on the live page; treat as optional
        var cookRaw       = cookTask.Result;
        bool hasCookRates = cookRaw?.Html.Contains("per gallon", StringComparison.OrdinalIgnoreCase) == true;
        var cookHtml      = hasCookRates ? cookRaw!.Value.Html    : "";
        var cookUrlUsed   = hasCookRates ? cookRaw!.Value.UrlUsed : CookUrl;

        // Chicago off-premises news page — optional, Wayback-aware
        var newsFetch = await ScraperHttpHelper.GetOptionalStringAsync(http, ChicagoNewsUrl, wayback, ct);

        var exciseBytes  = Encoding.UTF8.GetBytes(exciseHtml);
        var cookBytes    = Encoding.UTF8.GetBytes(cookHtml);
        var chicagoBytes = Encoding.UTF8.GetBytes(chicagoHtml);

        var state   = ParseStateExcise(exciseHtml);
        var cook    = hasCookRates ? ParseCookCounty(cookHtml) : default;
        var chicago = ParseChicago(chicagoHtml, newsFetch.HasValue ? newsFetch.Value.Html : "");

        var exciseConf  = ConfidenceOf(exciseUrlUsed);
        var cookConf    = ConfidenceOf(cookUrlUsed);
        var chicagoConf = ConfidenceOf(chicagoUrlUsed);

        var results = new List<BulkRateResult>();

        // ── Local helpers that close over fetched byte arrays ─────────────────
        BulkRateResult ExcRow(string fips, string name, string label, decimal rate,
            decimal? mn = null, decimal? mx = null, ProductCategory? cat = null) =>
            new(fips, name, label, rate, exciseUrlUsed, exciseBytes, "text/html", "", ExciseEffective,
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, mn, mx, exciseConf, cat);

        BulkRateResult CookRow(string label, decimal rate, decimal? mn = null, decimal? mx = null,
            ProductCategory? cat = null) =>
            new("17031", "Cook", label, rate, cookUrlUsed, cookBytes, "text/html", "", CookEffective,
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Distributor,
                IsIncludedInPrice: true, SaleContext.Any, mn, mx, cookConf, cat);

        BulkRateResult ChiOnPrem(string label, decimal rate, decimal? mn = null, decimal? mx = null,
            ProductCategory? cat = null) =>
            new("1714000", "Chicago", label, rate, chicagoUrlUsed, chicagoBytes, "text/html", "", ChicagoEffective,
                RateBasis.FlatPerVolume, TaxType.ExciseTax, RemittancePoint.Retailer,
                IsIncludedInPrice: false, SaleContext.OnPremise, mn, mx, chicagoConf, cat);

        // ── Illinois state excise rates (per gallon, distributor-remitted) ────
        results.Add(ExcRow("17", "Illinois",
            "Illinois Excise Tax — Beer/Cider (0.5%–7% ABV)", state.Beer,
            mn: 0.005m, mx: 0.07m, cat: ProductCategory.Beer));
        results.Add(ExcRow("17", "Illinois",
            "Illinois Excise Tax — Alcohol ≤14% ABV", state.Wine14,
            mx: 0.14m, cat: ProductCategory.Wine));
        results.Add(ExcRow("17", "Illinois",
            "Illinois Excise Tax — Alcohol >14%–<20% ABV", state.Wine20,
            mn: 0.14m, mx: 0.20m, cat: ProductCategory.Wine));
        results.Add(ExcRow("17", "Illinois",
            "Illinois Excise Tax — Spirits ≥20% ABV", state.Spirits,
            mn: 0.20m, cat: ProductCategory.Spirits));

        // ── Cook County liquor tax (distributor-remitted) — only when page has rate data ──
        if (hasCookRates)
        {
            results.Add(CookRow("Cook County Liquor Tax — Beer (<20% ABV)", cook.Beer, mx: 0.20m, cat: ProductCategory.Beer));
            results.Add(CookRow("Cook County Liquor Tax — Wine (≤14% ABV)", cook.Wine14, mx: 0.14m, cat: ProductCategory.Wine));
            results.Add(CookRow("Cook County Liquor Tax — Spirits (≥20% ABV)", cook.Spirits, mn: 0.20m, cat: ProductCategory.Spirits));
        }

        // ── Chicago on-premises liquor tax (retailer-remitted) ───────────────
        results.Add(ChiOnPrem("Chicago Liquor Tax — Beer, on-premises (per gal)", chicago.Beer, mx: 0.07m, cat: ProductCategory.Beer));
        results.Add(ChiOnPrem("Chicago Liquor Tax — Wine ≤14% ABV, on-premises (per gal)", chicago.Wine14, mx: 0.14m, cat: ProductCategory.Wine));
        results.Add(ChiOnPrem("Chicago Liquor Tax — Wine >14%–<20% ABV, on-premises (per gal)", chicago.Wine20, mn: 0.14m, mx: 0.20m, cat: ProductCategory.Wine));
        results.Add(ChiOnPrem("Chicago Liquor Tax — Spirits ≥20% ABV, on-premises (per gal)", chicago.Spirits, mn: 0.20m, cat: ProductCategory.Spirits));

        // ── Chicago off-premises liquor tax (% of retail price, eff. 2026-03-01)
        if (chicago.OffPremisePct > 0)
        {
            var (offBytes, offUrl, offConf) = newsFetch.HasValue
                ? (Encoding.UTF8.GetBytes(newsFetch.Value.Html), newsFetch.Value.UrlUsed, ConfidenceOf(newsFetch.Value.UrlUsed))
                : (chicagoBytes, chicagoUrlUsed, chicagoConf);
            results.Add(new("1714000", "Chicago",
                "Chicago Liquor Tax — Off-Premises Sales (% of retail)",
                chicago.OffPremisePct, offUrl, offBytes, "text/html", "", ChicagoEffective,
                RateBasis.Percentage, TaxType.ExciseTax, RemittancePoint.Retailer,
                IsIncludedInPrice: false, SaleContext.OffPremise, null, null, offConf,
                ProductCategory.Alcohol));
        }

        return results;
    }

    // ── Parsers ───────────────────────────────────────────────────────────────

    private static (decimal Beer, decimal Wine14, decimal Wine20, decimal Spirits) ParseStateExcise(string html)
    {
        var text = StripAndNormalize(html);

        // Beer: IDOR page writes "23.1¢ per gallon" (cents) or "$0.231 per gallon"
        decimal beer;
        var beerDollar = Regex.Match(text,
            @"(?:beer|cider).{0,300}?\$\s*(\d+\.\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var beerCents = Regex.Match(text,
            @"(?:beer|cider).{0,300}?(\d+(?:\.\d+)?)\s*¢",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (beerCents.Success)
            beer = decimal.Parse(beerCents.Groups[1].Value) / 100m;
        else if (beerDollar.Success)
            beer = decimal.Parse(beerDollar.Groups[1].Value);
        else
            throw new InvalidOperationException(
                $"Could not parse beer excise rate from {ExciseUrl}. The page structure may have changed.");

        // ≤14% tier: "$1.39 per gallon"
        var wine14 = RequireDollarRate(text,
            @"14\s*(?:percent|%)[^$]{0,60}(?:or\s*less|alcohol[^$]{0,60}?)[^$]{0,200}?\$\s*(\d+\.\d+)",
            ExciseUrl, "alcohol ≤14% ABV");

        // >14%–<20% tier (same $1.39 rate in IL; fall back to wine14 if page merged these rows)
        decimal wine20;
        var wine20Match = Regex.Match(text,
            @"(?:more\s*than|over|greater\s*than)\s*14[^$]{0,250}?(?:less\s*than|under)\s*20[^$]{0,200}?\$\s*(\d+\.\d+)",
            RegexOptions.IgnoreCase);
        wine20 = wine20Match.Success ? decimal.Parse(wine20Match.Groups[1].Value) : wine14;

        // ≥20% tier: "$8.55 per gallon" (old: description before $; new: $ before description)
        var spiritsOld = Regex.Match(text,
            @"20\s*(?:percent|%)\s*or\s*(?:more|greater)[^$]{0,200}?\$\s*(\d+\.\d+)",
            RegexOptions.IgnoreCase);
        var spiritsNew = Regex.Match(text,
            @"\$\s*(\d+\.\d+)\s*per\s*gallon[^.]{0,300}?20\s*(?:percent|%)\s*or\s*(?:more|greater)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var spiritsM = spiritsOld.Success ? spiritsOld : spiritsNew;
        if (!spiritsM.Success)
            throw new InvalidOperationException(
                $"Could not parse spirits ≥20% ABV rate from {ExciseUrl}. The page structure may have changed.");
        var spirits = decimal.Parse(spiritsM.Groups[1].Value);

        if (spirits <= wine14)
            throw new InvalidOperationException(
                $"Spirits rate ({spirits:C3}) is not greater than wine rate ({wine14:C3}) — IDOR page may have changed.");

        return (beer, wine14, wine20, spirits);
    }

    private static (decimal Beer, decimal Wine14, decimal Spirits) ParseCookCounty(string html)
    {
        var text = StripAndNormalize(html);

        // Beer (<20% ABV): $2.50/gal — Cook County Ch. 74 Art. IX eff. 2023-12-14
        var beer = RequireDollarRate(text,
            @"(?:beer)[^$]{0,300}?\$\s*(\d+\.\d+)",
            CookUrl, "beer (<20% ABV)");

        // Wine (≤14% ABV): $0.24/gal
        var wine14 = RequireDollarRate(text,
            @"(?:wine)[^$]{0,300}?\$\s*(\d+\.\d+)",
            CookUrl, "wine (≤14% ABV)");

        // Spirits (≥20% ABV): $2.50/gal — same rate as beer, but parse explicitly
        decimal spirits;
        var spiritsMatch = Regex.Match(text,
            @"(?:liquor|spirit|distilled)[^$]{0,300}?\$\s*(\d+\.\d+)",
            RegexOptions.IgnoreCase);
        spirits = spiritsMatch.Success ? decimal.Parse(spiritsMatch.Groups[1].Value) : beer;

        if (wine14 >= beer)
            throw new InvalidOperationException(
                $"Cook County wine rate ({wine14:C2}) should be less than beer/spirits rate ({beer:C2}) — page may have changed.");

        return (beer, wine14, spirits);
    }

    private static (decimal Beer, decimal Wine14, decimal Wine20, decimal Spirits, decimal OffPremisePct) ParseChicago(
        string html, string newsHtml)
    {
        var text = StripAndNormalize(html + " " + newsHtml);

        // Beer: $0.29/gal on-premises — page may list "$X per gallon of beer" (new) or "Beer: $X" (old)
        var beerM = Regex.Match(text, @"\$\s*(\d+\.\d+)\s*per\s*gallon\s*of\s*beer", RegexOptions.IgnoreCase);
        if (!beerM.Success) beerM = Regex.Match(text, @"(?:beer)[^$]{0,300}?\$\s*(\d+\.\d+)", RegexOptions.IgnoreCase);
        if (!beerM.Success) throw new InvalidOperationException($"Could not parse beer on-premises rate from {ChicagoUrl}. The page structure may have changed.");
        var beer = decimal.Parse(beerM.Groups[1].Value);

        // Wine ≤14%: $0.36/gal — "$X per gallon ... 14% or less" (new) or "14% or less ... $X" (old)
        var wine14M = Regex.Match(text, @"\$\s*(\d+\.\d+)\s*per\s*gallon[^.]{0,120}?14[^.]{0,30}?or\s*less", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!wine14M.Success) wine14M = Regex.Match(text, @"(?:wine|14\s*(?:percent|%)\s*or\s*less)[^$]{0,200}?\$\s*(\d+\.\d+)", RegexOptions.IgnoreCase);
        if (!wine14M.Success) throw new InvalidOperationException($"Could not parse wine ≤14% ABV on-premises rate from {ChicagoUrl}. The page structure may have changed.");
        var wine14 = decimal.Parse(wine14M.Groups[1].Value);

        // Wine >14%–<20%: $0.89/gal — "$X per gallon ... more than 14 ... less than 20" or reverse
        var wine20M = Regex.Match(text, @"\$\s*(\d+\.\d+)\s*per\s*gallon[^.]{0,200}?(?:more\s*than\s*14)[^.]{0,100}?(?:less\s*than|under)\s*20", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!wine20M.Success) wine20M = Regex.Match(text, @"(?:(?:more|greater)\s*than\s*14)[^$]{0,300}?(?:less\s*than|under)\s*20[^$]{0,200}?\$\s*(\d+\.\d+)", RegexOptions.IgnoreCase);
        if (!wine20M.Success) throw new InvalidOperationException($"Could not parse wine >14%–<20% ABV on-premises rate from {ChicagoUrl}. The page structure may have changed.");
        var wine20 = decimal.Parse(wine20M.Groups[1].Value);

        // Spirits ≥20%: $2.68/gal — "$X per gallon ... 20% or more" (new) or "20% or more ... $X" (old)
        var spiritsM = Regex.Match(text, @"\$\s*(\d+\.\d+)\s*per\s*gallon[^.]{0,100}?(?:20\s*(?:percent|%)\s*or\s*more)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!spiritsM.Success) spiritsM = Regex.Match(text, @"(?:20\s*(?:percent|%)\s*or\s*(?:more|over)|spirits)[^$]{0,200}?\$\s*(\d+\.\d+)", RegexOptions.IgnoreCase);
        if (!spiritsM.Success) throw new InvalidOperationException($"Could not parse spirits ≥20% ABV on-premises rate from {ChicagoUrl}. The page structure may have changed.");
        var spirits = decimal.Parse(spiritsM.Groups[1].Value);

        if (spirits <= wine20 || wine20 <= wine14 || wine14 <= beer)
            throw new InvalidOperationException(
                $"Chicago rate ordering check failed: beer={beer} wine14={wine14} wine20={wine20} spirits={spirits}");

        // Off-premises: 1.5% of retail price (eff. 2026-03-01, from news page or main page)
        decimal offPremisePct = 0m;
        var offMatch = Regex.Match(text,
            @"(?:off[- ]?premise|retail\s+purchas)[^%]{0,300}?(\d+(?:\.\d+)?)\s*%",
            RegexOptions.IgnoreCase);
        if (offMatch.Success && decimal.TryParse(offMatch.Groups[1].Value, out var pct))
            offPremisePct = pct / 100m;
        else if (!string.IsNullOrEmpty(newsHtml))
            offPremisePct = 0.015m; // 1.5% confirmed from MC Ch. 3-44 eff. 2026-03-01

        return (beer, wine14, wine20, spirits, offPremisePct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SourceConfidence ConfidenceOf(string urlUsed) =>
        urlUsed.Contains("archive.org", StringComparison.OrdinalIgnoreCase)
            ? SourceConfidence.Archive
            : SourceConfidence.Official;

    private static decimal RequireDollarRate(string text, string pattern, string url, string label)
    {
        var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
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
