using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.Frontend.Services;

/// <summary>
/// On startup, ensures the full Census dataset is present (~3,200 counties · ~30,000 cities).
/// If counties are missing entirely, wipes any partial data and does a full re-import.
/// If counties exist but cities don't (interrupted earlier run), imports only the missing cities.
/// Runs in the background so app startup is not blocked.
/// </summary>
public sealed class CensusAutoImportService(
    IServiceScopeFactory scopeFactory,
    ILogger<CensusAutoImportService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        await using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
        var countyCount = await db.Jurisdictions
            .CountAsync(j => j.JurisdictionType == JurisdictionType.County, stoppingToken);
        var cityCount = await db.Jurisdictions
            .CountAsync(j => j.JurisdictionType == JurisdictionType.City, stoppingToken);

        if (countyCount >= 3000 && cityCount >= 5000)
        {
            logger.LogDebug("CensusAutoImport: {Counties} counties, {Cities} cities present — nothing to do", countyCount, cityCount);
            return;
        }

        if (countyCount < 3000)
        {
            // Full wipe: stale or sample data present, re-import everything.
            logger.LogInformation("CensusAutoImport: {Count} counties found — wiping and running full Census import", countyCount);
            await WipeCitiesAndCountiesAsync(db, stoppingToken);
        }
        else
        {
            // Counties are good but cities are missing (likely interrupted run).
            logger.LogInformation("CensusAutoImport: {Counties} counties OK but only {Cities} cities — importing cities", countyCount, cityCount);
        }

        var svc = scope.ServiceProvider.GetRequiredService<ICensusJurisdictionImportService>();
        try
        {
            var progress = new Progress<CensusImportProgress>(p =>
                logger.LogDebug("CensusAutoImport [{Stage}] {Processed}/{Total} — {Current}",
                    p.Stage, p.Processed, p.Total, p.Current));

            var result = await svc.ImportAsync(progress, stoppingToken);

            logger.LogInformation(
                "CensusAutoImport complete: {Counties} counties, {Cities} cities, {Zips} zips linked in {Elapsed}",
                result.CountiesCreated, result.CitiesCreated, result.ZipsRelinked, result.Elapsed);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("CensusAutoImport cancelled during shutdown");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CensusAutoImport failed");
        }
    }

    private static async Task WipeCitiesAndCountiesAsync(AppDbContext db, CancellationToken ct)
    {
        // Null out ZIP → county/city links (no DB-level cascade for these columns).
        await db.ZipCodes
            .Where(z => z.CountyJurisdictionId != null || z.CityJurisdictionId != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(z => z.CountyJurisdictionId, (int?)null)
                .SetProperty(z => z.CityJurisdictionId,   (int?)null), ct);

        // Delete cities first (Jurisdiction.ParentId is Restrict; TaxRates cascade).
        var cityIds = await db.Jurisdictions
            .Where(j => j.JurisdictionType == JurisdictionType.City)
            .Select(j => j.Id)
            .ToListAsync(ct);

        if (cityIds.Count > 0)
        {
            await db.Jurisdictions
                .Where(j => cityIds.Contains(j.Id))
                .ExecuteDeleteAsync(ct);
        }

        // Delete counties (now leaf nodes).
        var countyIds = await db.Jurisdictions
            .Where(j => j.JurisdictionType == JurisdictionType.County)
            .Select(j => j.Id)
            .ToListAsync(ct);

        if (countyIds.Count > 0)
        {
            await db.Jurisdictions
                .Where(j => countyIds.Contains(j.Id))
                .ExecuteDeleteAsync(ct);
        }
    }
}
