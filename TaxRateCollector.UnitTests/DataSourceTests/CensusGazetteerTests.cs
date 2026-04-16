using System.IO.Compression;
using System.Net.Http;
using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.UnitTests.DataSourceTests;

/// <summary>
/// Integration tests that hit real Census Bureau URLs and verify the downloaded
/// data is well-formed. These tests make live HTTP requests — run them with
///   dotnet test --filter Category=Integration
/// They are excluded from the default (unit) test run.
/// URLs are sourced from AppSettings defaults so they stay in sync with the service layer.
/// </summary>
[TestFixture]
[Category("Integration")]
public class CensusGazetteerTests
{
    private static readonly AppSettings Settings = new();
    private static string CountyZipUrl => Settings.CensusCountyGazUrl;
    private static string PlaceZipUrl  => Settings.CensusPlaceGazUrl;

    // Shared client — one download per file across all tests in this fixture.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    private static byte[]? countyZipBytes;
    private static byte[]? placeZipBytes;

    [OneTimeSetUp]
    public async Task DownloadFiles()
    {
        countyZipBytes = await Http.GetByteArrayAsync(CountyZipUrl);
        placeZipBytes  = await Http.GetByteArrayAsync(PlaceZipUrl);
    }

    // ── County file ────────────────────────────────────────────────────────────

    [Test]
    public void County_ZipIsNotEmpty()
    {
        Assert.That(countyZipBytes, Is.Not.Null.And.Not.Empty,
            "County ZIP download returned no bytes");
    }

    [Test]
    public void County_ZipContainsTxtEntry()
    {
        var entry = GetFirstTxtEntry(countyZipBytes!);
        Assert.That(entry, Is.Not.Null, "County ZIP must contain at least one .txt file");
    }

    [Test]
    public void County_HeaderContainsRequiredColumns()
    {
        var header = ReadFirstLine(countyZipBytes!);
        Assert.Multiple(() =>
        {
            Assert.That(header, Does.Contain("GEOID"), "Missing GEOID column");
            Assert.That(header, Does.Contain("NAME"),  "Missing NAME column");
            Assert.That(header, Does.Contain("USPS"),  "Missing USPS column");
        });
    }

    [Test]
    public void County_DelimiterIsDetectable()
    {
        var header = ReadFirstLine(countyZipBytes!);
        var delim  = header.Contains('|') ? '|' : '\t';
        var cols   = header.Split(delim);
        Assert.That(cols.Length, Is.GreaterThanOrEqualTo(5),
            $"Header should have at least 5 columns; got {cols.Length} splitting on '{delim}'");
    }

    [Test]
    public void County_DataRowCountIsAtLeast3100()
    {
        var lines = ReadAllLines(countyZipBytes!);
        // First line is header; remainder are data rows.
        Assert.That(lines.Length - 1, Is.GreaterThanOrEqualTo(3100),
            $"Expected ≥3,100 county rows; got {lines.Length - 1}");
    }

    [Test]
    public void County_FirstRowFipsIs5Digits()
    {
        var lines  = ReadAllLines(countyZipBytes!);
        Assert.That(lines.Length, Is.GreaterThan(1), "File has no data rows");
        var header = lines[0];
        var delim  = header.Contains('|') ? '|' : '\t';
        var hCols  = header.Split(delim);
        var gIdx   = Array.FindIndex(hCols, c => c.Trim().Equals("GEOID", StringComparison.OrdinalIgnoreCase));

        var row1   = lines[1].Split(delim);
        var geoid  = row1[gIdx].Trim().PadLeft(5, '0');
        Assert.That(geoid.Length, Is.EqualTo(5),
            $"County GEOID should be 5 digits; got '{geoid}'");
        Assert.That(geoid, Does.Match(@"^\d{5}$"),
            "County GEOID should be numeric");
    }

    // ── Places file ────────────────────────────────────────────────────────────

    [Test]
    public void Places_ZipIsNotEmpty()
    {
        Assert.That(placeZipBytes, Is.Not.Null.And.Not.Empty,
            "Places ZIP download returned no bytes");
    }

    [Test]
    public void Places_ZipContainsTxtEntry()
    {
        var entry = GetFirstTxtEntry(placeZipBytes!);
        Assert.That(entry, Is.Not.Null, "Places ZIP must contain at least one .txt file");
    }

    [Test]
    public void Places_HeaderContainsRequiredColumns()
    {
        var header = ReadFirstLine(placeZipBytes!);
        Assert.Multiple(() =>
        {
            Assert.That(header, Does.Contain("GEOID"), "Missing GEOID column");
            Assert.That(header, Does.Contain("NAME"),  "Missing NAME column");
            Assert.That(header, Does.Contain("USPS"),  "Missing USPS column");
        });
    }

    [Test]
    public void Places_DataRowCountIsAtLeast25000()
    {
        var lines = ReadAllLines(placeZipBytes!);
        Assert.That(lines.Length - 1, Is.GreaterThanOrEqualTo(25_000),
            $"Expected ≥25,000 place rows; got {lines.Length - 1}");
    }

    [Test]
    public void Places_FirstRowFipsIs7Digits()
    {
        var lines  = ReadAllLines(placeZipBytes!);
        Assert.That(lines.Length, Is.GreaterThan(1), "File has no data rows");
        var header = lines[0];
        var delim  = header.Contains('|') ? '|' : '\t';
        var hCols  = header.Split(delim);
        var gIdx   = Array.FindIndex(hCols, c => c.Trim().Equals("GEOID", StringComparison.OrdinalIgnoreCase));

        var row1  = lines[1].Split(delim);
        var geoid = row1[gIdx].Trim().PadLeft(7, '0');
        Assert.That(geoid.Length, Is.EqualTo(7),
            $"Place GEOID should be 7 digits; got '{geoid}'");
        Assert.That(geoid, Does.Match(@"^\d{7}$"),
            "Place GEOID should be numeric");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static ZipArchiveEntry? GetFirstTxtEntry(byte[] zipBytes)
    {
        using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        return archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
    }

    private static string ReadFirstLine(byte[] zipBytes)
    {
        using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        var entry  = archive.Entries.First(e => e.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
        using var reader = new StreamReader(entry.Open());
        return reader.ReadLine() ?? string.Empty;
    }

    private static string[] ReadAllLines(byte[] zipBytes)
    {
        using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        var entry  = archive.Entries.First(e => e.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
