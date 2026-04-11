using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.Infrastructure.Scrapers;

public class ScrapeOrchestrator(
    IDbContextFactory<AppDbContext> dbFactory,
    IEnumerable<IScrapeStrategy> strategies,
    IDiffEngine diffEngine,
    ILogger<ScrapeOrchestrator> logger) : IScrapeOrchestrator
{
    public async Task<int> RunFullScrapeAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var run = new ScrapeRun
        {
            StartedAt = DateTime.UtcNow.ToString("o"),
            Status = ScrapeStatus.Running
        };
        db.ScrapeRuns.Add(run);
        await db.SaveChangesAsync(ct);

        var jurisdictions = await db.Jurisdictions
            .Where(j => j.IsActive)
            .ToListAsync(ct);

        int errors = 0;
        int scraped = 0;

        foreach (var jurisdiction in jurisdictions)
        {
            if (ct.IsCancellationRequested) break;

            var strategy = strategies.FirstOrDefault(s => s.CanHandle(jurisdiction));
            if (strategy is null)
            {
                logger.LogWarning("No strategy for jurisdiction {Id} ({Name})", jurisdiction.Id, jurisdiction.JurisdictionName);
                continue;
            }

            try
            {
                var rawResults = await strategy.ScrapeAsync(jurisdiction, ct);

                var existing = await db.TaxRates
                    .Where(t => t.JurisdictionId == jurisdiction.Id && t.IsCurrent)
                    .ToListAsync(ct);
                foreach (var r in existing) r.IsCurrent = false;

                foreach (var raw in rawResults)
                {
                    if (raw.ParsedRate is null) continue;
                    db.TaxRates.Add(new TaxRate
                    {
                        JurisdictionId = jurisdiction.Id,
                        Rate = raw.ParsedRate.Value,
                        RateType = raw.RateType ?? "General",
                        EffectiveDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        ScrapedAt = DateTime.UtcNow.ToString("o"),
                        ScrapeRunId = run.Id,
                        RawValue = raw.RawValue,
                        IsCurrent = true
                    });
                }

                scraped++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scrape failed for {Name}", jurisdiction.JurisdictionName);
                errors++;
            }
        }

        await db.SaveChangesAsync(ct);

        var diff = await diffEngine.DetectChangesAsync(run.Id, ct);

        run.Status = errors == jurisdictions.Count && jurisdictions.Count > 0
            ? ScrapeStatus.Failed
            : ScrapeStatus.Completed;
        run.CompletedAt = DateTime.UtcNow.ToString("o");
        run.TotalScraped = scraped;
        run.ChangesDetected = diff.Changes.Count;
        run.ErrorCount = errors;
        await db.SaveChangesAsync(ct);

        return diff.Changes.Count;
    }
}
