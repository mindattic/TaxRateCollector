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
            // Match by jurisdiction + Name to find the previous version of the same law
            var previous = await db.TaxRates
                .Where(t => t.JurisdictionId == newRate.JurisdictionId
                         && t.Name == newRate.Name
                         && t.ScrapeRunId != scrapeRunId)
                .OrderByDescending(t => t.ScrapedAt)
                .FirstOrDefaultAsync(ct);

            if (previous is null)
            {
                changes.Add(new RateChange(newRate.JurisdictionId, ChangeType.NewJurisdiction, null, newRate.Rate));
                db.ChangeLog.Add(new ChangeLogEntry
                {
                    JurisdictionId = newRate.JurisdictionId,
                    TaxRateId = newRate.Id,
                    RateName = newRate.Name,
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
                    TaxRateId = newRate.Id,
                    RateName = newRate.Name,
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
            .Select(t => new { t.JurisdictionId, t.Id, t.Name, t.Rate })
            .Distinct()
            .ToListAsync(ct);

        foreach (var prev in previouslyActive)
        {
            changes.Add(new RateChange(prev.JurisdictionId, ChangeType.Removed, prev.Rate, null));
            db.ChangeLog.Add(new ChangeLogEntry
            {
                JurisdictionId = prev.JurisdictionId,
                TaxRateId = prev.Id,
                RateName = prev.Name,
                ChangeType = ChangeType.Removed,
                OldRate = prev.Rate,
                DetectedAt = DateTime.UtcNow.ToString("o")
            });
        }

        await db.SaveChangesAsync(ct);
        return new DiffReport(newRates.Count, changes);
    }
}
