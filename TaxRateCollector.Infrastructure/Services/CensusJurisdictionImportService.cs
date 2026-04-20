using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.Infrastructure.Services;

/// <summary>
/// Imports all US counties and cities from the Census Bureau's free public data files,
/// then re-links every ZipCodeRecord to its County and City Jurisdiction rows.
///
/// Data sources (all free, no registration):
///   Counties  : https://www2.census.gov/geo/docs/maps-data/data/gazetteer/2025_Gazetteer/2025_Gaz_counties_national.zip
///   Cities    : https://www2.census.gov/geo/docs/maps-data/data/gazetteer/2025_Gazetteer/2025_Gaz_place_national.zip
///   City→County mapping: https://www2.census.gov/geo/docs/maps-data/data/rel2020/place/tab20_place20_county20_natl.txt (404 — national file moved; cities use first-county-per-state fallback)
///
/// File formats:
///   Gazetteer = tab-delimited, header row: USPS | GEOID | ANSICODE | NAME | ALAND | ...
///   Crosswalk = pipe-delimited, header row: GEOID_PLC_20 | GEOID_CNTY_20 | NAME_PLC_20 | ...
/// </summary>
public sealed class CensusJurisdictionImportService : ICensusJurisdictionImportService
{
    // ── Census Bureau public file URLs (defaults; overridden by AppSettings) ──
    // Note: the national place→county crosswalk no longer exists at the Census Bureau.
    // City→county mapping falls back to first-county-per-state (see ImportCitiesAsync).

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MindAttic", "TaxRateCollector", "cache");

    private static readonly string CountyCache      = Path.Combine(CacheDir, "census_counties.txt");
    private static readonly string PlaceCache       = Path.Combine(CacheDir, "census_places.txt");
    // Shared with ZipImportService — read from cache if ZIPs were already imported, or download fresh
    private static readonly string ZctaCountyCache  = Path.Combine(CacheDir, "census_zcta_county.txt");
    private static readonly string ZctaPlaceCache   = Path.Combine(CacheDir, "census_zcta_place.txt");

    // ── State FIPS → 2-letter abbreviation ───────────────────────────────────
    private static readonly Dictionary<string, string> FipsToStateCode = new(StringComparer.Ordinal)
    {
        ["01"]="AL",["02"]="AK",["04"]="AZ",["05"]="AR",["06"]="CA",
        ["08"]="CO",["09"]="CT",["10"]="DE",["11"]="DC",["12"]="FL",
        ["13"]="GA",["15"]="HI",["16"]="ID",["17"]="IL",["18"]="IN",
        ["19"]="IA",["20"]="KS",["21"]="KY",["22"]="LA",["23"]="ME",
        ["24"]="MD",["25"]="MA",["26"]="MI",["27"]="MN",["28"]="MS",
        ["29"]="MO",["30"]="MT",["31"]="NE",["32"]="NV",["33"]="NH",
        ["34"]="NJ",["35"]="NM",["36"]="NY",["37"]="NC",["38"]="ND",
        ["39"]="OH",["40"]="OK",["41"]="OR",["42"]="PA",["44"]="RI",
        ["45"]="SC",["46"]="SD",["47"]="TN",["48"]="TX",["49"]="UT",
        ["50"]="VT",["51"]="VA",["53"]="WA",["54"]="WV",["55"]="WI",
        ["56"]="WY",["60"]="AS",["66"]="GU",["69"]="MP",["72"]="PR",["78"]="VI"
    };

    private readonly IDbContextFactory<AppDbContext> dbFactory;
    private readonly IHttpClientFactory httpFactory;
    private readonly SettingsService settings;

    public CensusJurisdictionImportService(
        IDbContextFactory<AppDbContext> dbFactory,
        IHttpClientFactory httpFactory,
        SettingsService settings)
    {
        this.dbFactory   = dbFactory;
        this.httpFactory = httpFactory;
        this.settings    = settings;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<(int SeededCounties, int SeededCities, int LinkedZips)> GetCoverageAsync(
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var counties   = await db.Jurisdictions.CountAsync(j => j.JurisdictionType == JurisdictionType.County, ct);
        var cities     = await db.Jurisdictions.CountAsync(j => j.JurisdictionType == JurisdictionType.City,   ct);
        var linkedZips = await db.ZipCodes.CountAsync(z => z.CountyJurisdictionId != null, ct);
        return (counties, cities, linkedZips);
    }

    public void ClearCache()
    {
        foreach (var f in new[] { CountyCache, PlaceCache })
            if (File.Exists(f)) File.Delete(f);
    }

    public async Task<CensusImportResult> ImportAsync(
        IProgress<CensusImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        Directory.CreateDirectory(CacheDir);

        var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromMinutes(10);

        // ── 1. Download files ─────────────────────────────────────────────────
        // Note: the national place→county crosswalk no longer exists on the Census server.
        // Cities fall back to first-county-per-state.
        var countyGazUrl = settings.Current.CensusCountyGazUrl;
        var placeGazUrl  = settings.Current.CensusPlaceGazUrl;
        Report(progress, "Downloading", 0, 4, 0, 0, 0, "Census county gazetteer…");
        await EnsureCachedAsync(http, countyGazUrl, CountyCache, ct);
        Report(progress, "Downloading", 1, 4, 0, 0, 0, "Census places gazetteer…");
        await EnsureCachedAsync(http, placeGazUrl,  PlaceCache,  ct);

        // ── 2. Download ZCTA crosswalk files to build place→county mapping ───
        Report(progress, "Downloading", 2, 4, 0, 0, 0, "ZCTA→county crosswalk…");
        await EnsureCachedAsync(http, settings.Current.CensusZctaCountyUrl, ZctaCountyCache, ct);
        Report(progress, "Downloading", 3, 4, 0, 0, 0, "ZCTA→place crosswalk…");
        await EnsureCachedAsync(http, settings.Current.CensusZctaPlaceUrl,  ZctaPlaceCache,  ct);
        Report(progress, "Downloading", 4, 4, 0, 0, 0, "Building place→county map…");

        // ── 3. Parse files ────────────────────────────────────────────────────
        var counties    = CensusGazetteerParser.ParseGazetteerCounties(await File.ReadAllTextAsync(CountyCache, ct), FipsToStateCode);
        var places      = CensusGazetteerParser.ParseGazetteerPlaces(await File.ReadAllTextAsync(PlaceCache, ct), FipsToStateCode);
        var zctaCounty  = CensusGazetteerParser.ParseZctaCountyMap(await File.ReadAllTextAsync(ZctaCountyCache, ct));
        var placeCounty = CensusGazetteerParser.BuildPlaceCountyFromZcta(await File.ReadAllTextAsync(ZctaPlaceCache, ct), zctaCounty);

        // ── 4. Import counties ────────────────────────────────────────────────
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var (countiesCreated, countiesSkipped) = await ImportCountiesAsync(
            db, counties, progress, ct);

        // ── 5. Import cities (using real place→county crosswalk) ──────────────
        var (citiesCreated, citiesSkipped) = await ImportCitiesAsync(
            db, places, placeCounty, progress, ct);

        // ── 6. Re-parent any cities that were previously assigned to wrong county
        var reparented = await ReparentCitiesAsync(db, placeCounty, progress, ct);

        // ── 7. Re-link ZIP codes ──────────────────────────────────────────────
        var zipsRelinked = await RelinkZipsAsync(db, progress, ct);

        sw.Stop();
        return new CensusImportResult(
            countiesCreated, citiesCreated, zipsRelinked,
            countiesSkipped, citiesSkipped, sw.Elapsed);
    }

    // ── County import ─────────────────────────────────────────────────────────

    private async Task<(int Created, int Skipped)> ImportCountiesAsync(
        AppDbContext db,
        List<CensusGazetteerParser.GazCounty> counties,
        IProgress<CensusImportProgress>? progress,
        CancellationToken ct)
    {
        // Pre-load existing county FIPSes and state jurisdiction lookup
        var existingFips = await db.Jurisdictions
            .Where(j => j.JurisdictionType == JurisdictionType.County)
            .Select(j => j.FipsCode)
            .ToHashSetAsync(ct);

        var stateJurisdictions = await db.Jurisdictions
            .Where(j => j.JurisdictionType == JurisdictionType.State)
            .Select(j => new { j.Id, j.FipsCode })
            .ToDictionaryAsync(j => j.FipsCode, ct);

        int total = counties.Count, processed = 0, created = 0, skipped = 0, errors = 0;
        var batch = new List<Jurisdiction>(200);

        foreach (var c in counties)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (existingFips.Contains(c.Fips))
                {
                    skipped++;
                }
                else if (stateJurisdictions.TryGetValue(c.StateFips, out var stateJ))
                {
                    batch.Add(new Jurisdiction
                    {
                        JurisdictionType = JurisdictionType.County,
                        JurisdictionName = c.Name,
                        StateCode        = c.StateCode,
                        FipsCode         = c.Fips,
                        SourceUrl        = "",
                        IsActive         = true,
                        ParentId         = stateJ.Id,
                    });
                    created++;
                }
                else
                {
                    skipped++; // state not seeded (non-US territory)
                }
            }
            catch { errors++; }

            processed++;

            if (batch.Count >= 200)
            {
                await FlushBatchAsync(db, batch, ct);
                batch.Clear();
                Report(progress, "Counties", processed, total, created, skipped, errors, c.Name);
            }
        }

        if (batch.Count > 0)
            await FlushBatchAsync(db, batch, ct);

        Report(progress, "Counties", total, total, created, skipped, errors);
        return (created, skipped);
    }

    private static async Task FlushBatchAsync(AppDbContext db, List<Jurisdiction> batch, CancellationToken ct)
    {
        db.Jurisdictions.AddRange(batch);
        await db.SaveChangesAsync(ct);
    }

    // ── City import ───────────────────────────────────────────────────────────

    private async Task<(int Created, int Skipped)> ImportCitiesAsync(
        AppDbContext db,
        List<CensusGazetteerParser.GazPlace> places,
        Dictionary<string, string> placeCountyMap,   // placeFips(7) → countyFips(5)
        IProgress<CensusImportProgress>? progress,
        CancellationToken ct)
    {
        // Build county lookup: countyFips → jurisdictionId
        var countyLookup = await db.Jurisdictions
            .Where(j => j.JurisdictionType == JurisdictionType.County)
            .Select(j => new { j.Id, j.FipsCode, j.StateCode })
            .ToDictionaryAsync(j => j.FipsCode, ct);

        var existingFips = await db.Jurisdictions
            .Where(j => j.JurisdictionType == JurisdictionType.City)
            .Select(j => j.FipsCode)
            .ToHashSetAsync(ct);

        int total = places.Count, processed = 0, created = 0, skipped = 0, errors = 0;
        var batch = new List<Jurisdiction>(500);

        foreach (var p in places)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (existingFips.Contains(p.Fips))
                {
                    skipped++;
                }
                else
                {
                    int? parentCountyId = null;
                    if (placeCountyMap.TryGetValue(p.Fips, out var countyFips) &&
                        countyLookup.TryGetValue(countyFips, out var cj))
                    {
                        parentCountyId = cj.Id;
                    }

                    if (parentCountyId == null)
                    {
                        var fallback = countyLookup.Values
                            .FirstOrDefault(c => c.StateCode == p.StateCode);
                        parentCountyId = fallback?.Id;
                    }

                    if (parentCountyId != null)
                    {
                        batch.Add(new Jurisdiction
                        {
                            JurisdictionType = JurisdictionType.City,
                            JurisdictionName = p.Name,
                            StateCode        = p.StateCode,
                            FipsCode         = p.Fips,
                            SourceUrl        = "",
                            IsActive         = true,
                            ParentId         = parentCountyId,
                        });
                        created++;
                    }
                    else
                    {
                        skipped++; // territory without a seeded county
                    }
                }
            }
            catch { errors++; }

            processed++;

            if (batch.Count >= 500)
            {
                await FlushBatchAsync(db, batch, ct);
                batch.Clear();
                Report(progress, "Cities", processed, total, created, skipped, errors, p.Name);
            }
        }

        if (batch.Count > 0)
            await FlushBatchAsync(db, batch, ct);

        Report(progress, "Cities", total, total, created, skipped, errors);
        return (created, skipped);
    }

    // ── ZIP re-linking ────────────────────────────────────────────────────────

    private async Task<int> RelinkZipsAsync(
        AppDbContext db,
        IProgress<CensusImportProgress>? progress,
        CancellationToken ct)
    {
        Report(progress, "Re-linking ZIPs", 0, 0, 0, 0, 0, "Loading jurisdictions…");

        // Build lookup tables
        var countyByFips = await db.Jurisdictions
            .Where(j => j.JurisdictionType == JurisdictionType.County)
            .Select(j => new { j.Id, j.FipsCode })
            .ToDictionaryAsync(j => j.FipsCode, ct);

        var cityByFips = await db.Jurisdictions
            .Where(j => j.JurisdictionType == JurisdictionType.City)
            .Select(j => new { j.Id, j.FipsCode, j.StateCode, j.JurisdictionName })
            .ToListAsync(ct);

        // City lookup by (StateCode + normalized name) for ZIPs that have a city name but no direct FIPS match
        var cityByName = cityByFips
            .GroupBy(c => (c.StateCode, Normalize(c.JurisdictionName)))
            .ToDictionary(g => g.Key, g => g.First().Id);

        var cityByFipsDict = cityByFips.ToDictionary(c => c.FipsCode, c => c.Id);

        // Process ZIPs in batches of 1000
        var allZips = await db.ZipCodes.ToListAsync(ct);
        int updated = 0, processed = 0;
        int total = allZips.Count;

        foreach (var zip in allZips)
        {
            ct.ThrowIfCancellationRequested();

            bool changed = false;

            if (zip.CountyJurisdictionId == null &&
                countyByFips.TryGetValue(zip.CountyFips, out var cj))
            {
                zip.CountyJurisdictionId = cj.Id;
                changed = true;
            }

            if (zip.CityJurisdictionId == null)
            {
                // Try matching by city name + state
                if (cityByName.TryGetValue((zip.StateCode, Normalize(zip.PrimaryCity)), out var cityId))
                {
                    zip.CityJurisdictionId = cityId;
                    changed = true;
                }
            }

            if (changed) updated++;
            processed++;

            if (processed % 1000 == 0)
            {
                await db.SaveChangesAsync(ct);
                Report(progress, "Re-linking ZIPs", processed, total, updated, 0, 0, zip.ZipCode);
            }
        }

        await db.SaveChangesAsync(ct);
        Report(progress, "Re-linking ZIPs", total, total, updated, 0, 0, "Done");
        return updated;
    }

    // ── Re-parent cities that ended up under the wrong county ─────────────────

    private async Task<int> ReparentCitiesAsync(
        AppDbContext db,
        Dictionary<string, string> placeCounty,
        IProgress<CensusImportProgress>? progress,
        CancellationToken ct)
    {
        if (placeCounty.Count == 0) return 0;

        Report(progress, "Re-parenting cities", 0, 0, 0, 0, 0, "Loading…");

        var countyById = await db.Jurisdictions
            .Where(j => j.JurisdictionType == JurisdictionType.County)
            .Select(j => new { j.Id, j.FipsCode })
            .ToDictionaryAsync(j => j.FipsCode, ct);

        var cities = await db.Jurisdictions
            .Where(j => j.JurisdictionType == JurisdictionType.City)
            .ToListAsync(ct);

        int fixed_ = 0;
        foreach (var city in cities)
        {
            if (!placeCounty.TryGetValue(city.FipsCode, out var countyFips)) continue;
            if (!countyById.TryGetValue(countyFips, out var county)) continue;
            if (city.ParentId == county.Id) continue;

            city.ParentId = county.Id;
            fixed_++;
        }

        if (fixed_ > 0) await db.SaveChangesAsync(ct);
        Report(progress, "Re-parenting cities", cities.Count, cities.Count, fixed_, 0, 0, $"{fixed_:N0} cities re-parented");
        return fixed_;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Intentional early-return sentinel to anchor the removal block below.
    // ParseZctaCountyMap, BuildPlaceCountyFromZcta, ParseGazetteerCounties,
    // ParseGazetteerPlaces, ParsePlaceCountyRel, ExtractFips, and ColIdx
    // are now in CensusGazetteerParser (internal, tested).

    // ── File download ─────────────────────────────────────────────────────────

    private static async Task EnsureCachedAsync(
        HttpClient http, string url, string localPath, CancellationToken ct)
    {
        if (File.Exists(localPath)) return;

        if (url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = await http.GetByteArrayAsync(url, ct);
            using var zipStream = new MemoryStream(bytes);
            using var archive   = new ZipArchive(zipStream, ZipArchiveMode.Read);
            var entry = archive.Entries.First(e => e.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
            using var reader = new StreamReader(entry.Open());
            var content = await reader.ReadToEndAsync(ct);
            await File.WriteAllTextAsync(localPath, content, ct);
        }
        else
        {
            var content = await http.GetStringAsync(url, ct);
            await File.WriteAllTextAsync(localPath, content, ct);
        }
    }

    private static string Normalize(string s) =>
        new string(s.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray()).Trim();

    private static void Report(
        IProgress<CensusImportProgress>? p,
        string stage, int processed, int total,
        int created, int skipped, int errors, string current = "")
    {
        p?.Report(new CensusImportProgress(stage, processed, total, created, skipped, errors, current));
    }
}
