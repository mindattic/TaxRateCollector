using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Interfaces;

namespace TaxRateCollector.Infrastructure.Scrapers.Strategies;

public class TexasExcelScraper(HttpClient httpClient, ILogger<TexasExcelScraper> logger) : IScrapeStrategy
{
    public string StrategyKey => "TX-EXCEL";

    public bool CanHandle(Jurisdiction jurisdiction) =>
        jurisdiction.StateCode.Equals("TX", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<RawScrapeResult>> ScrapeAsync(
        Jurisdiction jurisdiction, CancellationToken ct = default)
    {
        var results = new List<RawScrapeResult>();

        try
        {
            var bytes = await httpClient.GetByteArrayAsync(jurisdiction.SourceUrl, ct);
            using var stream = new MemoryStream(bytes);
            using var workbook = new XLWorkbook(stream);
            var ws = workbook.Worksheets.First();

            foreach (var row in ws.RowsUsed().Skip(1))
            {
                var cityName = row.Cell(1).GetValue<string>().Trim();
                var rawRate = row.Cell(2).GetValue<string>().Trim();
                var parsed = RateSanitizer.Parse(rawRate);

                if (parsed.HasValue)
                    results.Add(new RawScrapeResult(rawRate, parsed, "General", cityName, 0.8f));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Texas Excel scraper failed for {Url}", jurisdiction.SourceUrl);
        }

        return results;
    }
}
