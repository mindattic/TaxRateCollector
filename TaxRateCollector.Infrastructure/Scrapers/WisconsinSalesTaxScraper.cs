using System.Text.RegularExpressions;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.Infrastructure.Scrapers;

/// <summary>
/// Bulk scraper for Wisconsin General Sales &amp; Use Tax. Authoritative sources:
///
///   1. Wisconsin DOR — County Sales and Use Tax FAQ (state + county rates)
///        State:    5%        per WI Stat. § 77.52
///        County:   0.5%      per WI Stat. § 77.71 (71 of 72 counties)
///        County:   0.9%      Milwaukee County (effective 2024-01-01)
///        Source: https://www.revenue.wi.gov/Pages/FAQS/pcs-county.aspx
///
///   2. Wisconsin DOR — Premier Resort Area Tax FAQ (city/village/town rates per § 77.994)
///        Standard: 0.5%      most PRAT jurisdictions
///        Elevated: 1.25%     Wisconsin Dells, Lake Delton
///        Source: https://www.revenue.wi.gov/Pages/FAQS/pcs-premier.aspx
///
/// Local exposition district tax (Milwaukee, § 77.98–77.985) and county-stadium tax (§ 77.706,
/// expired 2020) are intentionally excluded from this scraper because they apply only to specific
/// transaction types (food/beverage, lodging, rental cars) — not general retail sales.
/// </summary>
public sealed class WisconsinSalesTaxScraper : IStateBulkScraper
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly SettingsService settings;

    public string StateCode => "WI";
    public string? SstCategoryName => null; // General sales tax applies to all categories

    private const string CountyUrl  = "https://www.revenue.wi.gov/Pages/FAQS/pcs-county.aspx";
    private const string PremierUrl = "https://www.revenue.wi.gov/Pages/FAQS/pcs-premier.aspx";

    public WisconsinSalesTaxScraper(IHttpClientFactory httpClientFactory, SettingsService settings)
    {
        this.httpClientFactory = httpClientFactory;
        this.settings = settings;
    }

    public async Task<IReadOnlyList<BulkRateResult>> ScrapeAsync(CancellationToken ct = default)
    {
        var http    = httpClientFactory.CreateClient();
        var wayback = settings.Current.WaybackMachineFallback;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TaxRateCollector/1.0 (tax compliance research)");

        var countyTask  = ScraperHttpHelper.GetRequiredStringAsync(
            http, CountyUrl, wayback, ct, requiredContent: "5%");
        var premierTask = ScraperHttpHelper.GetRequiredStringAsync(
            http, PremierUrl, wayback, ct, requiredContent: "premier resort");
        await Task.WhenAll(countyTask, premierTask);

        var (countyHtml,  countyUrlUsed)  = countyTask.Result;
        var (premierHtml, premierUrlUsed) = premierTask.Result;

        var countyBytes  = System.Text.Encoding.UTF8.GetBytes(countyHtml);
        var premierBytes = System.Text.Encoding.UTF8.GetBytes(premierHtml);

        var countyConf  = countyUrlUsed.Contains("archive.org",  StringComparison.OrdinalIgnoreCase)
            ? Core.Enums.SourceConfidence.Archive : Core.Enums.SourceConfidence.Official;
        var premierConf = premierUrlUsed.Contains("archive.org", StringComparison.OrdinalIgnoreCase)
            ? Core.Enums.SourceConfidence.Archive : Core.Enums.SourceConfidence.Official;

        var (stateRate, milwaukeeRate, defaultCountyRate) = ParseCountyRates(countyHtml, countyUrlUsed);
        var premierRates = ParsePremierRates(premierHtml, premierUrlUsed);

        var results = new List<BulkRateResult>
        {
            new("55", "Wisconsin", "Wisconsin State Sales &amp; Use Tax",
                stateRate, countyUrlUsed, countyBytes, "text/html", "", "",
                Core.Enums.RateBasis.Percentage, Core.Enums.TaxType.SalesTax, Core.Enums.RemittancePoint.Retailer,
                IsIncludedInPrice: false, Core.Enums.SaleContext.Any, null, null, countyConf,
                ProductCategory: null,
                Conditions: $"Statutory authority: Wisconsin Statutes § 77.52 (state sales and use tax). Rate: {stateRate:P2} applied to retail sales of tangible personal property and taxable services."),
        };

        foreach (var (fips, name) in Counties)
        {
            var isMilwaukee = name.Equals("Milwaukee", StringComparison.OrdinalIgnoreCase);
            var rate        = isMilwaukee ? milwaukeeRate : defaultCountyRate;
            var conditions  = isMilwaukee
                ? $"Statutory authority: Wisconsin Statutes § 77.71 (county sales and use tax). Rate: {rate:P2} applied to retail sales in Milwaukee County (effective 2024-01-01)."
                : $"Statutory authority: Wisconsin Statutes § 77.71 (county sales and use tax). Rate: {rate:P2} applied to retail sales in {name} County.";

            results.Add(new BulkRateResult(
                FipsCode: fips,
                JurisdictionName: name,
                RateName: $"{name} County Sales &amp; Use Tax",
                Rate: rate,
                SourceUrl: countyUrlUsed,
                EvidenceBytes: countyBytes,
                EvidenceMimeType: "text/html",
                EvidenceOriginalFileName: "",
                EffectiveDate: "",
                RateBasis: Core.Enums.RateBasis.Percentage,
                TaxType: Core.Enums.TaxType.SalesTax,
                RemittancePoint: Core.Enums.RemittancePoint.Retailer,
                IsIncludedInPrice: false,
                SaleContext: Core.Enums.SaleContext.Any,
                MinAbv: null,
                MaxAbv: null,
                SourceConfidence: countyConf,
                ProductCategory: null,
                Conditions: conditions));
        }

        foreach (var prat in premierRates)
        {
            var fips = PremierResortFips.GetValueOrDefault(prat.Name, "");
            results.Add(new BulkRateResult(
                FipsCode: fips,
                JurisdictionName: prat.Name,
                RateName: $"{prat.Name} Premier Resort Area Tax",
                Rate: prat.Rate,
                SourceUrl: premierUrlUsed,
                EvidenceBytes: premierBytes,
                EvidenceMimeType: "text/html",
                EvidenceOriginalFileName: "",
                EffectiveDate: prat.EffectiveDate ?? "",
                RateBasis: Core.Enums.RateBasis.Percentage,
                TaxType: Core.Enums.TaxType.SalesTax,
                RemittancePoint: Core.Enums.RemittancePoint.Retailer,
                IsIncludedInPrice: false,
                SaleContext: Core.Enums.SaleContext.Any,
                MinAbv: null,
                MaxAbv: null,
                SourceConfidence: premierConf,
                ProductCategory: null,
                Conditions: $"Statutory authority: Wisconsin Statutes § 77.994 (premier resort area tax). Rate: {prat.Rate:P2} applied to sales of tourism-related goods and services in {prat.Name}."));
        }

        return results;
    }

    internal static (decimal State, decimal Milwaukee, decimal DefaultCounty) ParseCountyRates(string html, string sourceUrl)
    {
        var text = StripTags(html);

        // Anchor on the full noun phrase ("state sales [and use] tax") followed by a short
        // window ending at "<digit>%". The 30-char lazy window keeps the match local to a
        // rate sentence so we don't accidentally cross a heading and pick up the next %.
        var stateMatch = Regex.Match(text,
            @"\bstate\s+sales\s+(?:and\s+use\s+)?tax\b[^%]{0,30}?(\d+(?:\.\d+)?)\s*%",
            RegexOptions.IgnoreCase);
        if (!stateMatch.Success)
            throw new InvalidOperationException(
                $"Could not parse state sales tax rate from {sourceUrl}. The page structure may have changed.");
        var stateRate = decimal.Parse(stateMatch.Groups[1].Value) / 100m;

        var countyMatch = Regex.Match(text,
            @"\bcounty\s+sales\s+(?:and\s+use\s+)?tax\b[^%]{0,30}?(\d+(?:\.\d+)?)\s*%",
            RegexOptions.IgnoreCase);
        if (!countyMatch.Success)
            throw new InvalidOperationException(
                $"Could not parse default county sales tax rate from {sourceUrl}.");
        var defaultCountyRate = decimal.Parse(countyMatch.Groups[1].Value) / 100m;

        var milwaukeeMatch = Regex.Match(text,
            @"\bMilwaukee\s+County[^%]{0,120}?(\d+(?:\.\d+)?)\s*%",
            RegexOptions.IgnoreCase);
        if (!milwaukeeMatch.Success)
            throw new InvalidOperationException(
                $"Could not parse Milwaukee County sales tax rate from {sourceUrl}.");
        var milwaukeeRate = decimal.Parse(milwaukeeMatch.Groups[1].Value) / 100m;

        if (milwaukeeRate <= defaultCountyRate)
            throw new InvalidOperationException(
                $"Parsed Milwaukee rate ({milwaukeeRate}) is not greater than the default county rate " +
                $"({defaultCountyRate}) — parsing may have matched the wrong value.");

        return (stateRate, milwaukeeRate, defaultCountyRate);
    }

    internal record PremierRate(string Name, decimal Rate, string? EffectiveDate);

    internal static List<PremierRate> ParsePremierRates(string html, string sourceUrl)
    {
        var text = StripTags(html);

        // Pattern: "City of <Name>: 0.5%" or "Village of <Name>: 1.25%" or "Town of <Name>: 0.5%"
        // The rate appears immediately after the name with a colon, optionally followed by an
        // effective-date phrase like "effective January 1, 2017".
        var rateRx = new Regex(
            @"\b(?:City|Village|Town)\s+of\s+([A-Z][A-Za-z .'\-]+?)\s*[:\-]\s*(\d+(?:\.\d+)?)\s*%(?:[^.]{0,160}?effective\s+([A-Za-z]+\s+\d{1,2},\s+\d{4}))?",
            RegexOptions.IgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<PremierRate>();
        foreach (Match m in rateRx.Matches(text))
        {
            var name = m.Groups[1].Value.Trim().TrimEnd(',', '.', ';');
            if (!seen.Add(name)) continue;

            var rate    = decimal.Parse(m.Groups[2].Value) / 100m;
            var effDate = m.Groups[3].Success ? NormalizeEffectiveDate(m.Groups[3].Value) : null;

            if (rate <= 0 || rate > 0.05m)
                continue; // Sanity bound — PRAT rates are 0.5% to 1.25%; skip noise matches

            results.Add(new PremierRate(name, rate, effDate));
        }

        if (results.Count == 0)
            throw new InvalidOperationException(
                $"Could not parse any premier resort area rates from {sourceUrl}. The page structure may have changed.");

        return results;
    }

    private static string NormalizeEffectiveDate(string raw)
    {
        // "January 1, 2017" → "2017-01-01"
        if (DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var dt))
            return dt.ToString("yyyy-MM-dd");
        return raw;
    }

    private static string StripTags(string html)
    {
        var text = Regex.Replace(html, @"<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return Regex.Replace(text, @"\s+", " ");
    }

    private static readonly (string Fips, string Name)[] Counties =
    [
        ("55001", "Adams"),       ("55003", "Ashland"),     ("55005", "Barron"),
        ("55007", "Bayfield"),    ("55009", "Brown"),       ("55011", "Buffalo"),
        ("55013", "Burnett"),     ("55015", "Calumet"),     ("55017", "Chippewa"),
        ("55019", "Clark"),       ("55021", "Columbia"),    ("55023", "Crawford"),
        ("55025", "Dane"),        ("55027", "Dodge"),       ("55029", "Door"),
        ("55031", "Douglas"),     ("55033", "Dunn"),        ("55035", "Eau Claire"),
        ("55037", "Florence"),    ("55039", "Fond du Lac"), ("55041", "Forest"),
        ("55043", "Grant"),       ("55045", "Green"),       ("55047", "Green Lake"),
        ("55049", "Iowa"),        ("55051", "Iron"),        ("55053", "Jackson"),
        ("55055", "Jefferson"),   ("55057", "Juneau"),      ("55059", "Kenosha"),
        ("55061", "Kewaunee"),    ("55063", "La Crosse"),   ("55065", "Lafayette"),
        ("55067", "Langlade"),    ("55069", "Lincoln"),     ("55071", "Manitowoc"),
        ("55073", "Marathon"),    ("55075", "Marinette"),   ("55077", "Marquette"),
        ("55078", "Menominee"),   ("55079", "Milwaukee"),   ("55081", "Monroe"),
        ("55083", "Oconto"),      ("55085", "Oneida"),      ("55087", "Outagamie"),
        ("55089", "Ozaukee"),     ("55091", "Pepin"),       ("55093", "Pierce"),
        ("55095", "Polk"),        ("55097", "Portage"),     ("55099", "Price"),
        ("55101", "Racine"),      ("55103", "Richland"),    ("55105", "Rock"),
        ("55107", "Rusk"),        ("55109", "St. Croix"),   ("55111", "Sauk"),
        ("55113", "Sawyer"),      ("55115", "Shawano"),     ("55117", "Sheboygan"),
        ("55119", "Taylor"),      ("55121", "Trempealeau"), ("55123", "Vernon"),
        ("55125", "Vilas"),       ("55127", "Walworth"),    ("55129", "Washburn"),
        ("55131", "Washington"),  ("55133", "Waukesha"),    ("55135", "Waupaca"),
        ("55137", "Waushara"),    ("55139", "Winnebago"),   ("55141", "Wood"),
    ];

    // Census place FIPS for known PRAT jurisdictions. Used to match the scraped name
    // back to a Jurisdiction row; empty-string fallback triggers name-based matching.
    private static readonly Dictionary<string, string> PremierResortFips = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bayfield"]        = "5505000",
        ["Eagle River"]     = "5521900",
        ["Ephraim"]         = "5524325",
        ["Lake Delton"]     = "5541700",
        ["Minocqua"]        = "5553050",
        ["Rhinelander"]     = "5567350",
        ["Sister Bay"]      = "5573700",
        ["Stockholm"]       = "5577775",
        ["Sturgeon Bay"]    = "5578525",
        ["Wisconsin Dells"] = "5587625",
    };
}
