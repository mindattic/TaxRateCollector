using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.Infrastructure.Services;

public class DiffEngine(IDbContextFactory<AppDbContext> dbFactory) : IDiffEngine
{
    // Fields compared for structural changes (value changes are detected separately).
    // Stored as text in DB so we compare their string representations.
    private static string Fingerprint(TaxRate r) =>
        $"{r.RateBasis}|{r.TaxType}|{r.IsCompound}|{r.IsIncludedInPrice}|{r.RemittancePoint}";

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
                    TaxRateId      = newRate.Id,
                    RateName       = newRate.Name,
                    ChangeType     = ChangeType.NewJurisdiction,
                    NewRate        = newRate.Rate,
                    DetectedAt     = DateTime.UtcNow.ToString("o"),
                });
            }
            else if (previous.Rate != newRate.Rate)
            {
                changes.Add(new RateChange(newRate.JurisdictionId, ChangeType.RateChanged, previous.Rate, newRate.Rate));
                db.ChangeLog.Add(new ChangeLogEntry
                {
                    JurisdictionId = newRate.JurisdictionId,
                    TaxRateId      = newRate.Id,
                    RateName       = newRate.Name,
                    ChangeType     = ChangeType.RateChanged,
                    OldRate        = previous.Rate,
                    NewRate        = newRate.Rate,
                    DetectedAt     = DateTime.UtcNow.ToString("o"),
                });
            }
            else if (Fingerprint(previous) != Fingerprint(newRate))
            {
                // Rate value unchanged but structural fields differ
                var desc = BuildStructuralDescription(previous, newRate);
                changes.Add(new RateChange(newRate.JurisdictionId, ChangeType.StructuralChange,
                    previous.Rate, newRate.Rate, desc));
                db.ChangeLog.Add(new ChangeLogEntry
                {
                    JurisdictionId    = newRate.JurisdictionId,
                    TaxRateId         = newRate.Id,
                    RateName          = newRate.Name,
                    ChangeType        = ChangeType.StructuralChange,
                    OldRate           = previous.Rate,
                    NewRate           = newRate.Rate,
                    ChangeDescription = desc,
                    DetectedAt        = DateTime.UtcNow.ToString("o"),
                });
            }
        }

        // ── Detect removed jurisdictions ──────────────────────────────────────
        // A jurisdiction that appeared in at least one previous scrape but has
        // NO current-run rate is considered removed.  DistinctBy(JurisdictionId)
        // ensures exactly one Removed entry regardless of how many historical
        // scrape runs included the jurisdiction.
        // A jurisdiction whose rates are still current was simply not part of this run's
        // scope (another state, no strategy, manual rows) — it was not removed. And a
        // jurisdiction whose latest row already has a Removed entry was reported by an
        // earlier run, so it must not be logged again on every subsequent run.
        var scrapeJurisdictionIds = newRates.Select(r => r.JurisdictionId).ToHashSet();
        var stillCurrentIds = await db.TaxRates
            .Where(t => t.IsCurrent)
            .Select(t => t.JurisdictionId)
            .Distinct()
            .ToListAsync(ct);
        var alreadyReportedRateIds = (await db.ChangeLog
            .Where(c => c.ChangeType == ChangeType.Removed && c.TaxRateId != null)
            .Select(c => c.TaxRateId)
            .ToListAsync(ct)).ToHashSet();
        var previouslyActive = (await db.TaxRates
            .Where(t => t.ScrapeRunId != scrapeRunId
                     && !scrapeJurisdictionIds.Contains(t.JurisdictionId)
                     && !stillCurrentIds.Contains(t.JurisdictionId))
            .OrderByDescending(t => t.ScrapedAt)
            .Select(t => new { t.JurisdictionId, t.Id, t.Name, t.Rate })
            .ToListAsync(ct))
            .DistinctBy(t => t.JurisdictionId)
            .Where(t => !alreadyReportedRateIds.Contains(t.Id))
            .ToList();

        foreach (var prev in previouslyActive)
        {
            changes.Add(new RateChange(prev.JurisdictionId, ChangeType.Removed, prev.Rate, null));
            db.ChangeLog.Add(new ChangeLogEntry
            {
                JurisdictionId = prev.JurisdictionId,
                TaxRateId      = prev.Id,
                RateName       = prev.Name,
                ChangeType     = ChangeType.Removed,
                OldRate        = prev.Rate,
                DetectedAt     = DateTime.UtcNow.ToString("o"),
            });
        }

        await db.SaveChangesAsync(ct);
        return new DiffReport(newRates.Count, changes);
    }

    private static string BuildStructuralDescription(TaxRate prev, TaxRate next)
    {
        var parts = new List<string>();

        if (prev.RateBasis != next.RateBasis)
            parts.Add($"Basis: {prev.RateBasis} → {next.RateBasis}");
        if (prev.TaxType != next.TaxType)
            parts.Add($"TaxType: {prev.TaxType} → {next.TaxType}");
        if (prev.IsCompound != next.IsCompound)
            parts.Add($"IsCompound: {prev.IsCompound} → {next.IsCompound}");
        if (prev.IsIncludedInPrice != next.IsIncludedInPrice)
            parts.Add($"IsIncludedInPrice: {prev.IsIncludedInPrice} → {next.IsIncludedInPrice}");
        if (prev.RemittancePoint != next.RemittancePoint)
            parts.Add($"RemittancePoint: {prev.RemittancePoint} → {next.RemittancePoint}");

        return parts.Count > 0 ? string.Join("; ", parts) : "Structural change detected";
    }
}
