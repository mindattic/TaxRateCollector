using System.Text.RegularExpressions;
using TaxRateCollector.Core.Interfaces;

namespace TaxRateCollector.Infrastructure.Scrapers;

/// <summary>
/// Bulk scraper for Wisconsin alcohol sales and use tax rates.
/// Fetches live data from the Wisconsin DOR county tax FAQ page and parses:
///   - State sales tax rate (WI Stat. § 77.52)
///   - County sales tax rates — Milwaukee County 0.9% (eff. 2024-01-01),
///     all other 71 counties 0.5% per WI Stat. § 77.71.
/// Source: https://www.revenue.wi.gov/Pages/FAQS/pcs-county.aspx
/// </summary>
public sealed class WisconsinAlcoholScraper : IStateBulkScraper
{
    private readonly IHttpClientFactory httpClientFactory;

    public string StateCode => "WI";
    public string? SstCategoryName => "Alcoholic Beverages";

    private const string SourceUrl     = "https://www.revenue.wi.gov/Pages/FAQS/pcs-county.aspx";
    private const string EffectiveDate = "2024-01-01";

    public WisconsinAlcoholScraper(IHttpClientFactory httpClientFactory)
    {
        this.httpClientFactory = httpClientFactory;
    }

    public async Task<IReadOnlyList<BulkRateResult>> ScrapeAsync(CancellationToken ct = default)
    {
        var http = httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TaxRateCollector/1.0 (tax compliance research)");

        var html  = await http.GetStringAsync(SourceUrl, ct);
        var bytes = System.Text.Encoding.UTF8.GetBytes(html);

        var (stateRate, milwaukeeRate, defaultCountyRate) = ParseRates(html);

        var results = new List<BulkRateResult>
        {
            new("55", "Wisconsin", "Wisconsin State Sales Tax — Alcoholic Beverages",
                stateRate, SourceUrl, bytes, "text/html", "", EffectiveDate),
        };

        foreach (var (fips, name) in Counties)
        {
            var rate = name.Equals("Milwaukee", StringComparison.OrdinalIgnoreCase)
                ? milwaukeeRate
                : defaultCountyRate;

            results.Add(new BulkRateResult(
                FipsCode: fips,
                JurisdictionName: name,
                RateName: "Wisconsin County Sales Tax — Alcoholic Beverages",
                Rate: rate,
                SourceUrl: SourceUrl,
                EvidenceBytes: bytes,
                EvidenceMimeType: "text/html",
                EvidenceOriginalFileName: "",
                EffectiveDate: EffectiveDate));
        }

        return results;
    }

    private static (decimal State, decimal Milwaukee, decimal DefaultCounty) ParseRates(string html)
    {
        // Strip tags and decode entities for clean regex matching
        var text = Regex.Replace(html, @"<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ");

        // State sales tax: "State sales and use tax: 5%"
        var stateMatch = Regex.Match(text,
            @"\bstate\s+sales[^%]{0,80}?(\d+(?:\.\d+)?)\s*%",
            RegexOptions.IgnoreCase);
        if (!stateMatch.Success)
            throw new InvalidOperationException(
                $"Could not parse state sales tax rate from {SourceUrl}. " +
                "The page structure may have changed.");
        var stateRate = decimal.Parse(stateMatch.Groups[1].Value) / 100m;

        // Default county rate: "County sales and use tax: 0.5% or 0.9%"
        // Non-greedy match stops at the first (smaller) percentage
        var countyMatch = Regex.Match(text,
            @"\bcounty\s+sales[^%]{0,80}?(\d+(?:\.\d+)?)\s*%",
            RegexOptions.IgnoreCase);
        if (!countyMatch.Success)
            throw new InvalidOperationException(
                $"Could not parse default county sales tax rate from {SourceUrl}.");
        var defaultCountyRate = decimal.Parse(countyMatch.Groups[1].Value) / 100m;

        // Milwaukee county rate: "Milwaukee County imposes a 0.9%"
        var milwaukeeMatch = Regex.Match(text,
            @"\bMilwaukee\s+County[^%]{0,120}?(\d+(?:\.\d+)?)\s*%",
            RegexOptions.IgnoreCase);
        if (!milwaukeeMatch.Success)
            throw new InvalidOperationException(
                $"Could not parse Milwaukee County sales tax rate from {SourceUrl}.");
        var milwaukeeRate = decimal.Parse(milwaukeeMatch.Groups[1].Value) / 100m;

        if (milwaukeeRate <= defaultCountyRate)
            throw new InvalidOperationException(
                $"Parsed Milwaukee rate ({milwaukeeRate}) is not greater than the default county rate " +
                $"({defaultCountyRate}) — parsing may have matched the wrong value.");

        return (stateRate, milwaukeeRate, defaultCountyRate);
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
}
