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
        var counties    = ParseGazetteerCounties(await File.ReadAllTextAsync(CountyCache, ct));
        var places      = ParseGazetteerPlaces(await File.ReadAllTextAsync(PlaceCache,   ct));
        var zctaCounty  = ParseZctaCountyMap(await File.ReadAllTextAsync(ZctaCountyCache, ct));
        var placeCounty = BuildPlaceCountyFromZcta(await File.ReadAllTextAsync(ZctaPlaceCache, ct), zctaCounty);

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
        List<GazCounty> counties,
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

        // Need a ScrapeRun for rate rows
        var nowIso = DateTime.UtcNow.ToString("o");
        var today  = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var run    = new ScrapeRun
        {
            StartedAt = nowIso, CompletedAt = nowIso,
            Status = ScrapeStatus.Manual,
            TotalScraped = 0, ChangesDetected = 0, ErrorCount = 0
        };
        db.ScrapeRuns.Add(run);
        await db.SaveChangesAsync(ct);

        var leafCats = await db.TaxCategories.Where(c => c.IsLeaf).ToListAsync(ct);

        int total = counties.Count, processed = 0, created = 0, skipped = 0, errors = 0;
        var batch = new List<(Jurisdiction J, string StateCode)>(200);

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
                    var j = new Jurisdiction
                    {
                        JurisdictionType = JurisdictionType.County,
                        JurisdictionName = c.Name,
                        StateCode        = c.StateCode,
                        FipsCode         = c.Fips,
                        SourceUrl        = "",
                        IsActive         = true,
                        ParentId         = stateJ.Id,
                    };
                    batch.Add((j, c.StateCode));
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
                await FlushCountyBatchAsync(db, batch, run.Id, today, nowIso, leafCats, ct);
                batch.Clear();
                Report(progress, "Counties", processed, total, created, skipped, errors, c.Name);
            }
        }

        if (batch.Count > 0)
            await FlushCountyBatchAsync(db, batch, run.Id, today, nowIso, leafCats, ct);

        run.TotalScraped = created * Math.Max(1, leafCats.Count);
        db.ScrapeRuns.Update(run);
        await db.SaveChangesAsync(ct);

        Report(progress, "Counties", total, total, created, skipped, errors);
        return (created, skipped);
    }

    private static async Task FlushCountyBatchAsync(
        AppDbContext db, List<(Jurisdiction J, string StateCode)> batch,
        int runId, string today, string nowIso, IReadOnlyList<TaxCategory> leafCats, CancellationToken ct)
    {
        db.Jurisdictions.AddRange(batch.Select(x => x.J));
        await db.SaveChangesAsync(ct);

        var rates = new List<TaxRate>(batch.Count * Math.Max(1, leafCats.Count));
        foreach (var (j, _) in batch)
        {
            if (leafCats.Count == 0)
            {
                rates.Add(new TaxRate { JurisdictionId = j.Id, Rate = 0m, RateType = "General", EffectiveDate = today, ScrapedAt = nowIso, ScrapeRunId = runId, RawValue = "0.000%", IsCurrent = true });
            }
            else
            {
                foreach (var cat in leafCats)
                    rates.Add(new TaxRate { JurisdictionId = j.Id, Rate = 0m, RateType = "General", EffectiveDate = today, ScrapedAt = nowIso, ScrapeRunId = runId, RawValue = "0.000%", IsCurrent = true, TaxCategoryId = cat.Id });
            }
        }
        db.TaxRates.AddRange(rates);
        await db.SaveChangesAsync(ct);
    }

    // ── City import ───────────────────────────────────────────────────────────

    private async Task<(int Created, int Skipped)> ImportCitiesAsync(
        AppDbContext db,
        List<GazPlace> places,
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

        var nowIso = DateTime.UtcNow.ToString("o");
        var today  = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var run    = new ScrapeRun
        {
            StartedAt = nowIso, CompletedAt = nowIso,
            Status = ScrapeStatus.Manual,
            TotalScraped = 0, ChangesDetected = 0, ErrorCount = 0
        };
        db.ScrapeRuns.Add(run);
        await db.SaveChangesAsync(ct);

        var leafCats = await db.TaxCategories.Where(c => c.IsLeaf).ToListAsync(ct);

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
                    // Find parent county via place→county relationship
                    int? parentCountyId = null;
                    if (placeCountyMap.TryGetValue(p.Fips, out var countyFips) &&
                        countyLookup.TryGetValue(countyFips, out var cj))
                    {
                        parentCountyId = cj.Id;
                    }

                    // Fall back: find any county in the same state
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
                await FlushCityBatchAsync(db, batch, run.Id, today, nowIso, leafCats, ct);
                batch.Clear();
                Report(progress, "Cities", processed, total, created, skipped, errors, p.Name);
            }
        }

        if (batch.Count > 0)
            await FlushCityBatchAsync(db, batch, run.Id, today, nowIso, leafCats, ct);

        run.TotalScraped = created * Math.Max(1, leafCats.Count);
        db.ScrapeRuns.Update(run);
        await db.SaveChangesAsync(ct);

        Report(progress, "Cities", total, total, created, skipped, errors);
        return (created, skipped);
    }

    private static async Task FlushCityBatchAsync(
        AppDbContext db, List<Jurisdiction> batch,
        int runId, string today, string nowIso, IReadOnlyList<TaxCategory> leafCats, CancellationToken ct)
    {
        db.Jurisdictions.AddRange(batch);
        await db.SaveChangesAsync(ct);

        var rates = new List<TaxRate>(batch.Count * Math.Max(1, leafCats.Count));
        foreach (var j in batch)
        {
            if (leafCats.Count == 0)
            {
                rates.Add(new TaxRate { JurisdictionId = j.Id, Rate = 0m, RateType = "General", EffectiveDate = today, ScrapedAt = nowIso, ScrapeRunId = runId, RawValue = "0.000%", IsCurrent = true });
            }
            else
            {
                foreach (var cat in leafCats)
                    rates.Add(new TaxRate { JurisdictionId = j.Id, Rate = 0m, RateType = "General", EffectiveDate = today, ScrapedAt = nowIso, ScrapeRunId = runId, RawValue = "0.000%", IsCurrent = true, TaxCategoryId = cat.Id });
            }
        }
        db.TaxRates.AddRange(rates);
        await db.SaveChangesAsync(ct);
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

    // ── ZCTA crosswalk parsers (shared column names with ZipImportService) ────

    /// <summary>Returns zcta(5) → countyFips(5) using largest AREALAND_PART.</summary>
    private static Dictionary<string, string> ParseZctaCountyMap(string content)
    {
        var best  = new Dictionary<string, (string CountyFips, long Area)>(StringComparer.Ordinal);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2) return new();

        var header  = lines[0].Split('|');
        int iZcta   = ColIdx(header, "GEOID_ZCTA5_20");
        int iCounty = ColIdx(header, "GEOID_COUNTY_20");
        int iArea   = ColIdx(header, "AREALAND_PART");
        if (iZcta < 0 || iCounty < 0) return new();

        for (int i = 1; i < lines.Length; i++)
        {
            var cols = lines[i].Split('|');
            if (cols.Length <= Math.Max(iZcta, iCounty)) continue;

            var zcta   = cols[iZcta].Trim();
            var county = cols[iCounty].Trim();
            long area  = iArea >= 0 && cols.Length > iArea && long.TryParse(cols[iArea].Trim(), out var a) ? a : 0;

            if (zcta.Length != 5 || county.Length != 5) continue;
            if (!best.TryGetValue(zcta, out var prev) || area > prev.Area)
                best[zcta] = (county, area);
        }
        return best.ToDictionary(kv => kv.Key, kv => kv.Value.CountyFips);
    }

    /// <summary>
    /// Joins the ZCTA-to-place crosswalk with the zcta→county map to derive
    /// placeFips(7) → countyFips(5) using the largest AREALAND_PART intersection.
    /// The ZCTA-to-place crosswalk file contains GEOID_PLACE_20 (7-digit place FIPS).
    /// </summary>
    private static Dictionary<string, string> BuildPlaceCountyFromZcta(
        string placeContent, Dictionary<string, string> zctaToCounty)
    {
        var best  = new Dictionary<string, (string CountyFips, long Area)>(StringComparer.Ordinal);
        var lines = placeContent.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2) return new();

        var header = lines[0].Split('|');
        int iZcta  = ColIdx(header, "GEOID_ZCTA5_20");
        int iPlace = ColIdx(header, "GEOID_PLACE_20");
        int iArea  = ColIdx(header, "AREALAND_PART");
        if (iZcta < 0 || iPlace < 0) return new(); // column absent — fallback stays active

        for (int i = 1; i < lines.Length; i++)
        {
            var cols = lines[i].Split('|');
            if (cols.Length <= Math.Max(iZcta, iPlace)) continue;

            var zcta  = cols[iZcta].Trim();
            var place = cols[iPlace].Trim().PadLeft(7, '0');
            long area = iArea >= 0 && cols.Length > iArea && long.TryParse(cols[iArea].Trim(), out var a) ? a : 0;

            if (zcta.Length != 5 || place.Length != 7) continue;
            if (!zctaToCounty.TryGetValue(zcta, out var countyFips)) continue;

            if (!best.TryGetValue(place, out var prev) || area > prev.Area)
                best[place] = (countyFips, area);
        }
        return best.ToDictionary(kv => kv.Key, kv => kv.Value.CountyFips);
    }

    // ── File parsers ──────────────────────────────────────────────────────────

    private sealed record GazCounty(string Fips, string Name, string StateFips, string StateCode);
    private sealed record GazPlace(string Fips, string Name, string StateFips, string StateCode);

    /// <summary>
    /// Gazetteer county file (pipe-delimited as of 2025, tab-delimited in earlier years).
    /// Header: USPS|GEOID|GEOIDFQ|ANSICODE|NAME|ALAND|AWATER|ALAND_SQMI|AWATER_SQMI|INTPTLAT|INTPTLONG
    /// GEOID = 5-digit county FIPS (e.g., "01001")
    /// </summary>
    private static List<GazCounty> ParseGazetteerCounties(string content)
    {
        var result = new List<GazCounty>(3200);
        var lines  = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2) return result;

        var delim  = lines[0].Contains('|') ? '|' : '\t';
        var header = lines[0].Split(delim);
        int iUsps  = ColIdx(header, "USPS");
        int iGeoid = ColIdx(header, "GEOID");
        int iName  = ColIdx(header, "NAME");
        if (iGeoid < 0 || iName < 0) return result;

        for (int i = 1; i < lines.Length; i++)
        {
            var cols = lines[i].Split(delim);
            if (cols.Length <= Math.Max(iGeoid, iName)) continue;

            var fips      = cols[iGeoid].Trim().PadLeft(5, '0');
            var name      = cols[iName].Trim();
            var stateFips = fips.Length >= 2 ? fips[..2] : "";
            var stateCode = iUsps >= 0 && cols.Length > iUsps
                          ? cols[iUsps].Trim()
                          : FipsToStateCode.GetValueOrDefault(stateFips, "");

            if (fips.Length == 5 && !string.IsNullOrEmpty(name))
                result.Add(new GazCounty(fips, name, stateFips, stateCode));
        }

        return result;
    }

    /// <summary>
    /// Gazetteer places file (pipe-delimited as of 2025, tab-delimited in earlier years).
    /// Header: USPS|GEOID|GEOIDFQ|ANSICODE|NAME|LSAD|FUNCSTAT|ALAND|AWATER|ALAND_SQMI|AWATER_SQMI|INTPTLAT|INTPTLONG
    /// GEOID = 7-digit place FIPS: 2-digit state + 5-digit place (e.g., "0100100")
    /// </summary>
    private static List<GazPlace> ParseGazetteerPlaces(string content)
    {
        var result = new List<GazPlace>(35000);
        var lines  = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2) return result;

        var delim  = lines[0].Contains('|') ? '|' : '\t';
        var header = lines[0].Split(delim);
        int iUsps  = ColIdx(header, "USPS");
        int iGeoid = ColIdx(header, "GEOID");
        int iName  = ColIdx(header, "NAME");
        if (iGeoid < 0 || iName < 0) return result;

        for (int i = 1; i < lines.Length; i++)
        {
            var cols = lines[i].Split(delim);
            if (cols.Length <= Math.Max(iGeoid, iName)) continue;

            var fips      = cols[iGeoid].Trim().PadLeft(7, '0');
            var name      = cols[iName].Trim();
            var stateFips = fips.Length >= 2 ? fips[..2] : "";
            var stateCode = iUsps >= 0 && cols.Length > iUsps
                          ? cols[iUsps].Trim()
                          : FipsToStateCode.GetValueOrDefault(stateFips, "");

            if (fips.Length == 7 && !string.IsNullOrEmpty(name))
                result.Add(new GazPlace(fips, name, stateFips, stateCode));
        }

        return result;
    }

    /// <summary>
    /// Pipe-delimited place-to-county relationship file.
    /// Header: GEOID_PLC_20 | GEOID_CNTY_20 | NAME_PLC_20 | NAMELSAD_CNTY_20 | ... | AREALAND_INT | ...
    /// Returns: placeFips(7) → primary countyFips(5) by largest land-area intersection.
    /// </summary>
    private static Dictionary<string, string> ParsePlaceCountyRel(string content)
    {
        var best  = new Dictionary<string, (string CountyFips, long Area)>(StringComparer.Ordinal);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2) return new();

        var header  = lines[0].Split('|');
        int iPlace  = ColIdx(header, "GEOID_PLC_20");
        int iCounty = ColIdx(header, "GEOID_CNTY_20");
        int iArea   = ColIdx(header, "AREALAND_INT");
        if (iPlace < 0 || iCounty < 0) return new();

        for (int i = 1; i < lines.Length; i++)
        {
            var cols = lines[i].Split('|');
            if (cols.Length <= Math.Max(iPlace, iCounty)) continue;

            var placeRaw  = cols[iPlace].Trim();
            var countyRaw = cols[iCounty].Trim();
            long area     = iArea >= 0 && cols.Length > iArea && long.TryParse(cols[iArea].Trim(), out var a) ? a : 0;

            // Normalize to bare FIPS (strip any "1600000US" or similar prefix)
            var placeFips  = ExtractFips(placeRaw,  7);
            var countyFips = ExtractFips(countyRaw, 5);

            if (placeFips == null || countyFips == null) continue;

            if (!best.TryGetValue(placeFips, out var prev) || area > prev.Area)
                best[placeFips] = (countyFips, area);
        }

        return best.ToDictionary(kv => kv.Key, kv => kv.Value.CountyFips);
    }

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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int ColIdx(string[] header, string name)
    {
        for (int i = 0; i < header.Length; i++)
            if (string.Equals(header[i].Trim(), name, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    /// <summary>
    /// Census GEOIDs sometimes include a "summary level" prefix (e.g., "0500000US01001").
    /// Strip everything before the last <length> digits.
    /// </summary>
    private static string? ExtractFips(string raw, int length)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length < length) return null;
        var fips = digits[^length..];
        return fips;
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
