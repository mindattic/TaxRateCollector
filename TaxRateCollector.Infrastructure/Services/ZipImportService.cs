using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.Infrastructure.Services;

/// <summary>
/// Imports all US ZIP codes from the Census Bureau's free ZCTA relationship files
/// and optionally enriches city names via the USPS CityStateLookup Web Tools API.
///
/// Data flow:
///   1. Download Census ZCTA-to-County crosswalk  → ZIP + County FIPS + County Name
///   2. Download Census ZCTA-to-Place crosswalk   → ZIP + primary city/place name
///   3. (Optional) USPS CityStateLookup API       → validate/override city names
///   4. Join datasets → build ZipCodeRecord per ZCTA
///   5. Link each record to existing State/County/City Jurisdiction rows
///   6. Bulk-insert new records (idempotent — existing ZIPs are skipped)
///
/// Tax-law basis:
///   Destination-based sourcing: sales tax is determined by the ship-to address ZIP.
///   Each ZIP maps to exactly one State, one primary County, and one primary City
///   (where "primary" = largest land-area intersection per Census data).
///   The combined tax rate = StateRate + CountyRate + CityRate for that ZIP,
///   modified by the ProductCategory rules for the transaction.
/// </summary>
public sealed class ZipImportService : IZipImportService
{
    // URLs come from AppSettings (admin-configurable in the Settings page)

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MindAttic", "TaxRateCollector", "cache");

    private static readonly string CountyCacheFile = Path.Combine(CacheDir, "census_zcta_county.txt");
    private static readonly string PlaceCacheFile  = Path.Combine(CacheDir, "census_zcta_place.txt");

    // ── US State FIPS → 2-letter abbreviation ────────────────────────────────
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

    public ZipImportService(
        IDbContextFactory<AppDbContext> dbFactory,
        IHttpClientFactory httpFactory,
        SettingsService settings)
    {
        this.dbFactory  = dbFactory;
        this.httpFactory = httpFactory;
        this.settings   = settings;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<int> GetImportedCountAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ZipCodes.CountAsync(ct);
    }

    public void ClearCache()
    {
        if (File.Exists(CountyCacheFile)) File.Delete(CountyCacheFile);
        if (File.Exists(PlaceCacheFile))  File.Delete(PlaceCacheFile);
    }

    public async Task<ZipImportResult> ImportAsync(
        IProgress<ZipImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        Directory.CreateDirectory(CacheDir);

        // 1. Download Census crosswalk files (cached after first run)
        var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromMinutes(5);

        await EnsureCachedAsync(http, settings.Current.CensusZctaCountyUrl, CountyCacheFile, ct);
        await EnsureCachedAsync(http, settings.Current.CensusZctaPlaceUrl,  PlaceCacheFile,  ct);

        // 2. Parse crosswalk files
        var countyMap = ParseCountyCrosswalk(await File.ReadAllTextAsync(CountyCacheFile, ct));
        var placeMap  = ParsePlaceCrosswalk(await File.ReadAllTextAsync(PlaceCacheFile, ct));

        // 3. Build full ZCTA list (union of both files)
        var allZctas = countyMap.Keys.Union(placeMap.Keys).OrderBy(z => z).ToList();

        // 4. Skip already-imported ZIPs
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.ZipCodes.Select(z => z.ZipCode).ToHashSetAsync(ct);
        var newZctas = allZctas.Where(z => !existing.Contains(z)).ToList();

        int total = newZctas.Count, processed = 0, imported = 0, skipped = 0, errors = 0;
        progress?.Report(new ZipImportProgress(0, total, 0, 0, 0));

        // 5. Pre-load jurisdiction lookup tables for linking
        var stateJurisdictions = await db.Jurisdictions
            .Where(j => j.JurisdictionType == JurisdictionType.State)
            .Select(j => new { j.Id, j.FipsCode })
            .ToDictionaryAsync(j => j.FipsCode, ct);

        var countyJurisdictions = await db.Jurisdictions
            .Where(j => j.JurisdictionType == JurisdictionType.County)
            .Select(j => new { j.Id, j.FipsCode })
            .ToDictionaryAsync(j => j.FipsCode, ct);

        var cityRows = await db.Jurisdictions
            .Where(j => j.JurisdictionType == JurisdictionType.City)
            .Select(j => new { j.Id, j.StateCode, j.JurisdictionName })
            .ToListAsync(ct);

        // City lookup: (stateCode, normalizedName) → Id (first match wins for duplicates)
        var cityLookup = cityRows
            .GroupBy(j => (j.StateCode, Normalize(j.JurisdictionName)))
            .ToDictionary(g => g.Key, g => g.First().Id);

        // 6. Optional USPS enrichment for city names
        Dictionary<string, string>? uspsCities = null;
        if (!string.IsNullOrWhiteSpace(settings.Current.UspsApiKey))
        {
            progress?.Report(new ZipImportProgress(0, total, 0, 0, 0, "Fetching USPS city names…"));
            uspsCities = await FetchUspsCitiesAsync(http, newZctas, settings.Current.UspsApiKey, ct);
        }

        // 7. Build records in batches of 500
        var batch = new List<ZipCodeRecord>(500);
        var nowIso = DateTime.UtcNow.ToString("o");

        foreach (var zcta in newZctas)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (!countyMap.TryGetValue(zcta, out var countyInfo))
                {
                    skipped++;
                    processed++;
                    continue;
                }

                var stateFips  = countyInfo.CountyFips.Length >= 2 ? countyInfo.CountyFips[..2] : "";
                var stateCode  = FipsToStateCode.GetValueOrDefault(stateFips, "");
                var cityName   = (uspsCities?.TryGetValue(zcta, out var uc) == true ? uc : null)
                              ?? (placeMap.TryGetValue(zcta, out var pc) ? pc : "");
                var source     = (uspsCities?.ContainsKey(zcta) == true) ? "USPS+Census" : "Census";

                var record = new ZipCodeRecord
                {
                    ZipCode            = zcta,
                    StateCode          = stateCode,
                    StateFips          = stateFips,
                    CountyFips         = countyInfo.CountyFips,
                    CountyName         = countyInfo.CountyName,
                    PrimaryCity        = cityName,
                    StateJurisdictionId  = stateJurisdictions.TryGetValue(stateFips, out var sj) ? sj.Id : null,
                    CountyJurisdictionId = countyJurisdictions.TryGetValue(countyInfo.CountyFips, out var cj) ? cj.Id : null,
                    CityJurisdictionId   = cityLookup.TryGetValue((stateCode, Normalize(cityName)), out var cid) ? cid : null,
                    ImportedAt         = nowIso,
                    Source             = source,
                };

                batch.Add(record);
                imported++;
            }
            catch (Exception)
            {
                errors++;
            }

            processed++;

            if (batch.Count >= 500)
            {
                db.ZipCodes.AddRange(batch);
                await db.SaveChangesAsync(ct);
                batch.Clear();
                progress?.Report(new ZipImportProgress(processed, total, imported, skipped, errors, zcta));
            }
        }

        if (batch.Count > 0)
        {
            db.ZipCodes.AddRange(batch);
            await db.SaveChangesAsync(ct);
        }

        sw.Stop();
        progress?.Report(new ZipImportProgress(processed, total, imported, skipped, errors));
        return new ZipImportResult(total, imported, skipped, errors, sw.Elapsed);
    }

    // ── Census file download ──────────────────────────────────────────────────

    private static async Task EnsureCachedAsync(HttpClient http, string url, string localPath, CancellationToken ct)
    {
        if (File.Exists(localPath)) return;
        var content = await http.GetStringAsync(url, ct);
        await File.WriteAllTextAsync(localPath, content, ct);
    }

    // ── Census ZCTA-to-County parser ─────────────────────────────────────────
    // File format (pipe-delimited, header row):
    //   OID_ZCTA5_20|GEOID_ZCTA5_20|...|GEOID_COUNTY_20|NAMELSAD_COUNTY_20|...|AREALAND_PART|AREAWATER_PART
    // Each ZCTA may appear multiple times (once per intersecting county).
    // We pick the county with the largest AREALAND_PART (primary county by land area).

    private static Dictionary<string, (string CountyFips, string CountyName)> ParseCountyCrosswalk(string content)
    {
        var best = new Dictionary<string, (string Fips, string Name, long Area)>(StringComparer.Ordinal);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2) return new();

        var header = lines[0].Split('|');
        int iZcta    = ColIdx(header, "GEOID_ZCTA5_20");
        int iCounty  = ColIdx(header, "GEOID_COUNTY_20");
        int iName    = ColIdx(header, "NAMELSAD_COUNTY_20");
        int iArea    = ColIdx(header, "AREALAND_PART");
        if (iZcta < 0 || iCounty < 0) return new();

        for (int i = 1; i < lines.Length; i++)
        {
            var cols = lines[i].Split('|');
            if (cols.Length <= Math.Max(iZcta, iCounty)) continue;

            var zcta      = cols[iZcta].Trim();
            var countyFips = cols[iCounty].Trim();
            var name      = iName >= 0 && cols.Length > iName ? cols[iName].Trim() : "";
            long area     = iArea >= 0 && cols.Length > iArea && long.TryParse(cols[iArea].Trim(), out var a) ? a : 0;

            if (zcta.Length != 5 || countyFips.Length != 5) continue;

            if (!best.TryGetValue(zcta, out var prev) || area > prev.Area)
                best[zcta] = (countyFips, name, area);
        }

        return best.ToDictionary(kv => kv.Key, kv => (kv.Value.Fips, kv.Value.Name));
    }

    // ── Census ZCTA-to-Place parser ───────────────────────────────────────────
    // File format: OID_ZCTA5_20|GEOID_ZCTA5_20|...|NAMELSAD_PLACE_20|...|AREALAND_PART|...
    // We pick the place with the largest AREALAND_PART and strip Census place suffixes.

    private static Dictionary<string, string> ParsePlaceCrosswalk(string content)
    {
        var best = new Dictionary<string, (string Name, long Area)>(StringComparer.Ordinal);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2) return new();

        var header = lines[0].Split('|');
        int iZcta  = ColIdx(header, "GEOID_ZCTA5_20");
        int iName  = ColIdx(header, "NAMELSAD_PLACE_20");
        int iArea  = ColIdx(header, "AREALAND_PART");
        if (iZcta < 0 || iName < 0) return new();

        for (int i = 1; i < lines.Length; i++)
        {
            var cols = lines[i].Split('|');
            if (cols.Length <= Math.Max(iZcta, iName)) continue;

            var zcta  = cols[iZcta].Trim();
            var name  = cols[iName].Trim();
            long area = iArea >= 0 && cols.Length > iArea && long.TryParse(cols[iArea].Trim(), out var a) ? a : 0;

            if (zcta.Length != 5 || string.IsNullOrEmpty(name)) continue;

            var clean = StripPlaceSuffix(name);

            if (!best.TryGetValue(zcta, out var prev) || area > prev.Area)
                best[zcta] = (clean, area);
        }

        return best.ToDictionary(kv => kv.Key, kv => kv.Value.Name);
    }

    // ── USPS CityStateLookup ──────────────────────────────────────────────────
    // API: https://secure.shippingapis.com/ShippingAPI.dll?API=CityStateLookup&XML=...
    // Up to 5 ZIP codes per request; returns City + State (or Error element).

    private static async Task<Dictionary<string, string>> FetchUspsCitiesAsync(
        HttpClient http, List<string> zips, string apiKey, CancellationToken ct)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var chunks = zips.Chunk(5).ToList();

        foreach (var chunk in chunks)
        {
            ct.ThrowIfCancellationRequested();

            var xml = new StringBuilder($"<CityStateLookupRequest USERID=\"{apiKey}\">");
            for (int i = 0; i < chunk.Length; i++)
                xml.Append($"<ZipCode ID=\"{i}\"><Zip5>{chunk[i]}</Zip5></ZipCode>");
            xml.Append("</CityStateLookupRequest>");

            var url = $"https://secure.shippingapis.com/ShippingAPI.dll?API=CityStateLookup&XML={Uri.EscapeDataString(xml.ToString())}";

            try
            {
                var response = await http.GetStringAsync(url, ct);
                var doc = XDocument.Parse(response);
                foreach (var el in doc.Descendants("ZipCode"))
                {
                    if (el.Element("Error") != null) continue;
                    var zip5 = el.Element("Zip5")?.Value;
                    var city = el.Element("City")?.Value;
                    if (!string.IsNullOrEmpty(zip5) && !string.IsNullOrEmpty(city))
                        result[zip5] = city;
                }
            }
            catch { /* skip failed batches — fall back to Census place names */ }

            // ~20 req/sec ceiling — be polite to USPS servers
            await Task.Delay(50, ct);
        }

        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int ColIdx(string[] header, string name)
    {
        for (int i = 0; i < header.Length; i++)
            if (string.Equals(header[i].Trim(), name, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    // Strips Census place suffixes such as " city", " town", " CDP", " borough", etc.
    private static readonly string[] PlaceSuffixes =
    [
        " city", " town", " CDP", " borough", " village", " township",
        " municipality", " comunidad", " zona urbana"
    ];

    private static string StripPlaceSuffix(string name)
    {
        foreach (var s in PlaceSuffixes)
            if (name.EndsWith(s, StringComparison.OrdinalIgnoreCase))
                return name[..^s.Length].Trim();
        return name;
    }

    // Normalizes city names for lookup matching (remove punctuation, lowercase).
    private static string Normalize(string s) =>
        new string(s.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray()).Trim();
}
