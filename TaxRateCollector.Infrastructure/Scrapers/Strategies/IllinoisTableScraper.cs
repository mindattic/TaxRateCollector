using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Interfaces;

namespace TaxRateCollector.Infrastructure.Scrapers.Strategies;

public class IllinoisTableScraper(HttpClient httpClient, ILogger<IllinoisTableScraper> logger) : IScrapeStrategy
{
    public string StrategyKey => "IL-TABLE";

    public bool CanHandle(Jurisdiction jurisdiction) =>
        jurisdiction.StateCode.Equals("IL", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<RawScrapeResult>> ScrapeAsync(
        Jurisdiction jurisdiction, CancellationToken ct = default)
    {
        var results = new List<RawScrapeResult>();

        try
        {
            var html = await httpClient.GetStringAsync(jurisdiction.SourceUrl, ct);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var rows = doc.DocumentNode.SelectNodes("//table//tr");
            if (rows is null) return results;

            foreach (var row in rows.Skip(1))
            {
                var cells = row.SelectNodes("td");
                if (cells is null || cells.Count < 2) continue;

                var countyName = cells[0].InnerText.Trim();
                var rawRate = cells[1].InnerText.Trim();
                var parsed = RateSanitizer.Parse(rawRate);

                if (parsed.HasValue)
                    results.Add(new RawScrapeResult(rawRate, parsed, "General", countyName, 0.9f));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Illinois scraper failed for {Url}", jurisdiction.SourceUrl);
        }

        return results;
    }
}
