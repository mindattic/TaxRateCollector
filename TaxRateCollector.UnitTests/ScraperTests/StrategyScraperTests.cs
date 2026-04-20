using System.Text;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Infrastructure.Scrapers.Strategies;
using TaxRateCollector.UnitTests.Helpers;

namespace TaxRateCollector.UnitTests.ScraperTests;

// ── CaliforniaCsvScraper ──────────────────────────────────────────────────────

[TestFixture]
public class CaliforniaCsvScraperTests
{
    private const string Url = "http://cdtfa.ca.gov/rates.csv";

    private static CaliforniaCsvScraper MakeScraper(string csvBody)
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(Url, Encoding.UTF8.GetBytes(csvBody), "text/csv");
        return new CaliforniaCsvScraper(new HttpClient(handler), NullLogger<CaliforniaCsvScraper>.Instance);
    }

    private static Jurisdiction MakeJurisdiction(string stateCode = "CA")
        => new() { JurisdictionName = "Los Angeles", FipsCode = "06037", StateCode = stateCode, JurisdictionType = JurisdictionType.County, IsActive = true, SourceUrl = Url };

    [Test]
    public void CanHandle_ReturnsTrue_ForCA()
        => Assert.That(new CaliforniaCsvScraper(new HttpClient(), NullLogger<CaliforniaCsvScraper>.Instance)
            .CanHandle(MakeJurisdiction("CA")), Is.True);

    [Test]
    public void CanHandle_ReturnsFalse_ForNonCA()
        => Assert.That(new CaliforniaCsvScraper(new HttpClient(), NullLogger<CaliforniaCsvScraper>.Instance)
            .CanHandle(MakeJurisdiction("TX")), Is.False);

    [Test]
    public void CanHandle_IsCaseInsensitive()
        => Assert.That(new CaliforniaCsvScraper(new HttpClient(), NullLogger<CaliforniaCsvScraper>.Instance)
            .CanHandle(MakeJurisdiction("ca")), Is.True);

    [Test]
    public async Task ScrapeAsync_ParsesValidCsvRow()
    {
        var scraper = MakeScraper("City,Rate\nLos Angeles,9.5%\n");
        var results = await scraper.ScrapeAsync(MakeJurisdiction());
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].ParsedRate, Is.EqualTo(0.095m).Within(1e-6m));
        Assert.That(results[0].JurisdictionHint, Is.EqualTo("Los Angeles"));
    }

    [Test]
    public async Task ScrapeAsync_SkipsRowWithInvalidRate()
    {
        var scraper = MakeScraper("City,Rate\nSan Francisco,N/A\n");
        var results = await scraper.ScrapeAsync(MakeJurisdiction());
        Assert.That(results, Is.Empty);
    }

    [Test]
    public async Task ScrapeAsync_SkipsRowAboveCeiling()
    {
        // 25% is above the 20% ceiling
        var scraper = MakeScraper("City,Rate\nBig City,25%\n");
        var results = await scraper.ScrapeAsync(MakeJurisdiction());
        Assert.That(results, Is.Empty);
    }

    [Test]
    public async Task ScrapeAsync_ParsesMultipleRows()
    {
        var scraper = MakeScraper("City,Rate\nLA,8%\nSD,7.75%\n");
        var results = await scraper.ScrapeAsync(MakeJurisdiction());
        Assert.That(results, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ScrapeAsync_ReturnsEmpty_On404()
    {
        var scraper = new CaliforniaCsvScraper(
            new HttpClient(new FakeHttpMessageHandler()),  // returns 404 for any URL
            NullLogger<CaliforniaCsvScraper>.Instance);
        var results = await scraper.ScrapeAsync(MakeJurisdiction());
        Assert.That(results, Is.Empty);
    }
}

// ── IllinoisTableScraper ──────────────────────────────────────────────────────

[TestFixture]
public class IllinoisTableScraperTests
{
    private const string Url = "http://tax.illinois.gov/rates.html";

    private static IllinoisTableScraper MakeScraper(string html)
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(Url, Encoding.UTF8.GetBytes(html), "text/html");
        return new IllinoisTableScraper(new HttpClient(handler), NullLogger<IllinoisTableScraper>.Instance);
    }

    private static Jurisdiction MakeJurisdiction(string stateCode = "IL")
        => new() { JurisdictionName = "Cook County", FipsCode = "17031", StateCode = stateCode, JurisdictionType = JurisdictionType.County, IsActive = true, SourceUrl = Url };

    private static string MakeTable(params (string county, string rate)[] rows)
    {
        var rowHtml = string.Join("", rows.Select(r => $"<tr><td>{r.county}</td><td>{r.rate}</td></tr>"));
        return $"<html><body><table><tr><th>County</th><th>Rate</th></tr>{rowHtml}</table></body></html>";
    }

    [Test]
    public void CanHandle_ReturnsTrue_ForIL()
        => Assert.That(new IllinoisTableScraper(new HttpClient(), NullLogger<IllinoisTableScraper>.Instance)
            .CanHandle(MakeJurisdiction("IL")), Is.True);

    [Test]
    public void CanHandle_ReturnsFalse_ForNonIL()
        => Assert.That(new IllinoisTableScraper(new HttpClient(), NullLogger<IllinoisTableScraper>.Instance)
            .CanHandle(MakeJurisdiction("CA")), Is.False);

    [Test]
    public async Task ScrapeAsync_ParsesHtmlTableRow()
    {
        var scraper = MakeScraper(MakeTable(("Cook", "10.25%")));
        var results = await scraper.ScrapeAsync(MakeJurisdiction());
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].ParsedRate, Is.EqualTo(0.1025m).Within(1e-6m));
        Assert.That(results[0].JurisdictionHint, Is.EqualTo("Cook"));
    }

    [Test]
    public async Task ScrapeAsync_ParsesMultipleRows_SkipsHeader()
    {
        var scraper = MakeScraper(MakeTable(("Cook", "10.25%"), ("DuPage", "7%")));
        var results = await scraper.ScrapeAsync(MakeJurisdiction());
        Assert.That(results, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ScrapeAsync_ReturnsEmpty_WhenNoTable()
    {
        var scraper = MakeScraper("<html><body><p>No table here.</p></body></html>");
        var results = await scraper.ScrapeAsync(MakeJurisdiction());
        Assert.That(results, Is.Empty);
    }

    [Test]
    public async Task ScrapeAsync_SkipsRowsWithInvalidRate()
    {
        var scraper = MakeScraper(MakeTable(("Cook", "N/A"), ("DuPage", "7%")));
        var results = await scraper.ScrapeAsync(MakeJurisdiction());
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].JurisdictionHint, Is.EqualTo("DuPage"));
    }
}

// ── TexasExcelScraper ─────────────────────────────────────────────────────────

[TestFixture]
public class TexasExcelScraperTests
{
    private const string Url = "http://comptroller.texas.gov/rates.xlsx";

    private static byte[] MakeExcel(params (string city, string rate)[] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Rates");
        ws.Cell(1, 1).Value = "City";
        ws.Cell(1, 2).Value = "Rate";
        for (int i = 0; i < rows.Length; i++)
        {
            ws.Cell(i + 2, 1).Value = rows[i].city;
            ws.Cell(i + 2, 2).Value = rows[i].rate;
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static TexasExcelScraper MakeScraper(byte[] excelBytes)
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(Url, excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        return new TexasExcelScraper(new HttpClient(handler), NullLogger<TexasExcelScraper>.Instance);
    }

    private static Jurisdiction MakeJurisdiction(string stateCode = "TX")
        => new() { JurisdictionName = "Dallas", FipsCode = "48113", StateCode = stateCode, JurisdictionType = JurisdictionType.County, IsActive = true, SourceUrl = Url };

    [Test]
    public void CanHandle_ReturnsTrue_ForTX()
        => Assert.That(new TexasExcelScraper(new HttpClient(), NullLogger<TexasExcelScraper>.Instance)
            .CanHandle(MakeJurisdiction("TX")), Is.True);

    [Test]
    public void CanHandle_ReturnsFalse_ForNonTX()
        => Assert.That(new TexasExcelScraper(new HttpClient(), NullLogger<TexasExcelScraper>.Instance)
            .CanHandle(MakeJurisdiction("IL")), Is.False);

    [Test]
    public async Task ScrapeAsync_ParsesExcelRows()
    {
        var excel = MakeExcel(("Dallas", "8.25%"), ("Houston", "8.25%"));
        var scraper = MakeScraper(excel);
        var results = await scraper.ScrapeAsync(MakeJurisdiction());
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].JurisdictionHint, Is.EqualTo("Dallas"));
        Assert.That(results[0].ParsedRate, Is.EqualTo(0.0825m).Within(1e-6m));
    }

    [Test]
    public async Task ScrapeAsync_SkipsHeaderRow()
    {
        // Header row says "Rate" which is not parseable as a number → filtered
        var excel = MakeExcel(("Austin", "6.25%"));
        var scraper = MakeScraper(excel);
        var results = await scraper.ScrapeAsync(MakeJurisdiction());
        // Should return exactly the data rows, not the header
        Assert.That(results, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ScrapeAsync_SkipsRowWithInvalidRate()
    {
        var excel = MakeExcel(("Dallas", "N/A"), ("Houston", "8.25%"));
        var scraper = MakeScraper(excel);
        var results = await scraper.ScrapeAsync(MakeJurisdiction());
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].JurisdictionHint, Is.EqualTo("Houston"));
    }

    [Test]
    public async Task ScrapeAsync_ReturnsEmpty_OnInvalidFile()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(Url, Encoding.UTF8.GetBytes("not an excel file"), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var scraper = new TexasExcelScraper(new HttpClient(handler), NullLogger<TexasExcelScraper>.Instance);
        var results = await scraper.ScrapeAsync(MakeJurisdiction());
        Assert.That(results, Is.Empty);
    }
}
