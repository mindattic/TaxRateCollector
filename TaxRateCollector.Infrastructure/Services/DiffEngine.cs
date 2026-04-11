using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.Infrastructure.Services;

public class DiffEngine(IDbContextFactory<AppDbContext> dbFactory) : IDiffEngine
{
    public async Task<DiffReport> DetectChangesAsync(int scrapeRunId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var newRates = await db.TaxRates
            .Where(t => t.ScrapeRunId == scrapeRunId && t.IsCurrent)
            .ToListAsync(ct);

        var changes = new List<RateChange>();

        foreach (var newRate in newRates)
        {
            var previous = await db.TaxRates
                .Where(t => t.JurisdictionId == newRate.JurisdictionId
                         && t.RateType == newRate.RateType
                         && t.ScrapeRunId != scrapeRunId)
                .OrderByDescending(t => t.ScrapedAt)
                .FirstOrDefaultAsync(ct);

            if (previous is null)
            {
                changes.Add(new RateChange(newRate.JurisdictionId, ChangeType.NewJurisdiction, null, newRate.Rate));
                db.ChangeLog.Add(new ChangeLogEntry
                {
                    JurisdictionId = newRate.JurisdictionId,
                    ChangeType = ChangeType.NewJurisdiction,
                    NewRate = newRate.Rate,
                    DetectedAt = DateTime.UtcNow.ToString("o")
                });
            }
            else if (previous.Rate != newRate.Rate)
            {
                changes.Add(new RateChange(newRate.JurisdictionId, ChangeType.RateChanged, previous.Rate, newRate.Rate));
                db.ChangeLog.Add(new ChangeLogEntry
                {
                    JurisdictionId = newRate.JurisdictionId,
                    ChangeType = ChangeType.RateChanged,
                    OldRate = previous.Rate,
                    NewRate = newRate.Rate,
                    DetectedAt = DateTime.UtcNow.ToString("o")
                });
            }
        }

        var scrapeJurisdictionIds = newRates.Select(r => r.JurisdictionId).ToHashSet();
        var previouslyActive = await db.TaxRates
            .Where(t => t.ScrapeRunId != scrapeRunId && !scrapeJurisdictionIds.Contains(t.JurisdictionId))
            .Select(t => t.JurisdictionId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var jId in previouslyActive)
        {
            var lastKnown = await db.TaxRates
                .Where(t => t.JurisdictionId == jId && t.ScrapeRunId != scrapeRunId)
                .OrderByDescending(t => t.ScrapedAt)
                .FirstOrDefaultAsync(ct);

            if (lastKnown is null) continue;

            changes.Add(new RateChange(jId, ChangeType.Removed, lastKnown.Rate, null));
            db.ChangeLog.Add(new ChangeLogEntry
            {
                JurisdictionId = jId,
                ChangeType = ChangeType.Removed,
                OldRate = lastKnown.Rate,
                DetectedAt = DateTime.UtcNow.ToString("o")
            });
        }

        await db.SaveChangesAsync(ct);
        return new DiffReport(newRates.Count, changes);
    }
}
