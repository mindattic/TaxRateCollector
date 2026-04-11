using System.Globalization;
using CsvHelper;
using Microsoft.Extensions.Logging;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Interfaces;

namespace TaxRateCollector.Infrastructure.Scrapers.Strategies;

public class CaliforniaCsvScraper(HttpClient httpClient, ILogger<CaliforniaCsvScraper> logger) : IScrapeStrategy
{
    public string StrategyKey => "CA-CSV";

    public bool CanHandle(Jurisdiction jurisdiction) =>
        jurisdiction.StateCode.Equals("CA", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<RawScrapeResult>> ScrapeAsync(
        Jurisdiction jurisdiction, CancellationToken ct = default)
    {
        var results = new List<RawScrapeResult>();

        try
        {
            var stream = await httpClient.GetStreamAsync(jurisdiction.SourceUrl, ct);
            using var reader = new StreamReader(stream);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

            await csv.ReadAsync();
            csv.ReadHeader();

            while (await csv.ReadAsync())
            {
                var cityName = csv.GetField("City") ?? string.Empty;
                var rawRate = csv.GetField("Rate") ?? string.Empty;
                var parsed = RateSanitizer.Parse(rawRate);

                if (parsed.HasValue)
                    results.Add(new RawScrapeResult(rawRate, parsed, "General", cityName, 0.85f));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "California CSV scraper failed for {Url}", jurisdiction.SourceUrl);
        }

        return results;
    }
}
