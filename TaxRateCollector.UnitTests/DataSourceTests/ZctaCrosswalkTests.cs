using System.Net.Http;
using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.UnitTests.DataSourceTests;

/// <summary>
/// Integration tests that hit real Census Bureau ZCTA crosswalk URLs and verify
/// the downloaded files are well-formed with the expected column names.
/// Run with:  dotnet test --filter Category=Integration
/// URLs are sourced from AppSettings defaults so they stay in sync with the service layer.
/// </summary>
[TestFixture]
[Category("Integration")]
public class ZctaCrosswalkTests
{
    private static readonly AppSettings Settings = new();
    private static string CountyUrl => Settings.CensusZctaCountyUrl;
    private static string PlaceUrl  => Settings.CensusZctaPlaceUrl;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    private static string[]? countyHeader;
    private static string[]? placeHeader;
    private static string[]? countyFirstDataRow;
    private static string[]? placeFirstDataRow;

    [OneTimeSetUp]
    public async Task FetchHeaders()
    {
        countyHeader       = await FetchHeaderAsync(CountyUrl);
        placeHeader        = await FetchHeaderAsync(PlaceUrl);
        countyFirstDataRow = await FetchFirstDataRowAsync(CountyUrl);
        placeFirstDataRow  = await FetchFirstDataRowAsync(PlaceUrl);
    }

    // ── County crosswalk ───────────────────────────────────────────────────────

    [Test]
    public async Task CountyCrosswalk_Url_Returns200()
    {
        var resp = await Http.SendAsync(new HttpRequestMessage(HttpMethod.Head, CountyUrl));
        Assert.That((int)resp.StatusCode, Is.EqualTo(200));
    }

    [Test]
    public void CountyCrosswalk_HasZctaColumn()
        => Assert.That(countyHeader, Does.Contain("GEOID_ZCTA5_20"),
            "County crosswalk must have GEOID_ZCTA5_20 column");

    [Test]
    public void CountyCrosswalk_HasCountyFipsColumn()
        => Assert.That(countyHeader, Does.Contain("GEOID_COUNTY_20"),
            "County crosswalk must have GEOID_COUNTY_20 column");

    [Test]
    public void CountyCrosswalk_HasCountyNameColumn()
        => Assert.That(countyHeader, Does.Contain("NAMELSAD_COUNTY_20"),
            "County crosswalk must have NAMELSAD_COUNTY_20 column");

    [Test]
    public void CountyCrosswalk_HasAreaColumn()
        => Assert.That(countyHeader, Does.Contain("AREALAND_PART"),
            "County crosswalk must have AREALAND_PART column");

    [Test]
    public void CountyCrosswalk_FirstDataRow_ZctaIs5Digits()
    {
        Assert.That(countyFirstDataRow, Is.Not.Null.And.Not.Empty, "No data rows found");
        var zctaIdx = Array.IndexOf(countyHeader!, "GEOID_ZCTA5_20");
        var zcta = countyFirstDataRow![zctaIdx];
        Assert.That(zcta, Does.Match(@"^\d{5}$"), $"ZCTA '{zcta}' should be a 5-digit number");
    }

    [Test]
    public void CountyCrosswalk_FirstDataRow_CountyFipsIs5Digits()
    {
        Assert.That(countyFirstDataRow, Is.Not.Null.And.Not.Empty, "No data rows found");
        var fipsIdx = Array.IndexOf(countyHeader!, "GEOID_COUNTY_20");
        var fips = countyFirstDataRow![fipsIdx];
        Assert.That(fips, Does.Match(@"^\d{5}$"), $"County FIPS '{fips}' should be a 5-digit number");
    }

    // ── Place crosswalk ────────────────────────────────────────────────────────

    [Test]
    public async Task PlaceCrosswalk_Url_Returns200()
    {
        var resp = await Http.SendAsync(new HttpRequestMessage(HttpMethod.Head, PlaceUrl));
        Assert.That((int)resp.StatusCode, Is.EqualTo(200));
    }

    [Test]
    public void PlaceCrosswalk_HasZctaColumn()
        => Assert.That(placeHeader, Does.Contain("GEOID_ZCTA5_20"),
            "Place crosswalk must have GEOID_ZCTA5_20 column");

    [Test]
    public void PlaceCrosswalk_HasPlaceNameColumn()
        => Assert.That(placeHeader, Does.Contain("NAMELSAD_PLACE_20"),
            "Place crosswalk must have NAMELSAD_PLACE_20 column");

    [Test]
    public void PlaceCrosswalk_HasAreaColumn()
        => Assert.That(placeHeader, Does.Contain("AREALAND_PART"),
            "Place crosswalk must have AREALAND_PART column");

    [Test]
    public void PlaceCrosswalk_FirstDataRow_ZctaIs5Digits()
    {
        Assert.That(placeFirstDataRow, Is.Not.Null.And.Not.Empty, "No data rows found");
        var zctaIdx = Array.IndexOf(placeHeader!, "GEOID_ZCTA5_20");
        var zcta = placeFirstDataRow![zctaIdx];
        Assert.That(zcta, Does.Match(@"^\d{5}$"), $"ZCTA '{zcta}' should be a 5-digit number");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<string[]> FetchHeaderAsync(string url)
    {
        using var stream = await Http.GetStreamAsync(url);
        using var reader = new StreamReader(stream);
        var header = await reader.ReadLineAsync() ?? "";
        return header.Split('|');
    }

    private static async Task<string[]?> FetchFirstDataRowAsync(string url)
    {
        using var stream = await Http.GetStreamAsync(url);
        using var reader = new StreamReader(stream);
        var header = await reader.ReadLineAsync() ?? "";
        var cols   = header.Split('|');
        var zctaIdx = Array.IndexOf(cols, "GEOID_ZCTA5_20");

        // Scan until we find a row where the ZCTA column is non-empty
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            var row = line.Split('|');
            if (row.Length > zctaIdx && !string.IsNullOrEmpty(row[zctaIdx]))
                return row;
        }
        return null;
    }
}
