namespace TaxRateCollector.Infrastructure.Services;

internal static class CensusGazetteerParser
{
    internal sealed record GazCounty(string Fips, string Name, string StateFips, string StateCode);
    internal sealed record GazPlace(string Fips, string Name, string StateFips, string StateCode);

    // ── ZCTA-to-county (pipe-delimited crosswalk) ─────────────────────────────
    // Returns: ZCTA → primary county FIPS (largest AREALAND_PART intersection).

    internal static Dictionary<string, string> ParseZctaCountyMap(string content)
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

    // ── Place-to-county via ZCTA join ─────────────────────────────────────────
    // Joins ZCTA-to-place crosswalk with the zctaToCounty map to derive
    // placeFips(7) → countyFips(5) via largest AREALAND_PART ZCTA intersection.

    internal static Dictionary<string, string> BuildPlaceCountyFromZcta(
        string placeContent, Dictionary<string, string> zctaToCounty)
    {
        var best  = new Dictionary<string, (string CountyFips, long Area)>(StringComparer.Ordinal);
        var lines = placeContent.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2) return new();

        var header = lines[0].Split('|');
        int iZcta  = ColIdx(header, "GEOID_ZCTA5_20");
        int iPlace = ColIdx(header, "GEOID_PLACE_20");
        int iArea  = ColIdx(header, "AREALAND_PART");
        if (iZcta < 0 || iPlace < 0) return new();

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

    // ── Gazetteer county file ─────────────────────────────────────────────────
    // Supports both pipe-delimited (2025+) and tab-delimited (earlier years).
    // Header: USPS|GEOID|GEOIDFQ|ANSICODE|NAME|ALAND|...

    internal static List<GazCounty> ParseGazetteerCounties(string content,
        IReadOnlyDictionary<string, string> fipsToStateCode)
    {
        var result = new List<GazCounty>();
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
                          : fipsToStateCode.GetValueOrDefault(stateFips, "");

            if (fips.Length == 5 && !string.IsNullOrEmpty(name))
                result.Add(new GazCounty(fips, name, stateFips, stateCode));
        }

        return result;
    }

    // ── Gazetteer places file ─────────────────────────────────────────────────
    // Header: USPS|GEOID|GEOIDFQ|ANSICODE|NAME|LSAD|FUNCSTAT|ALAND|...
    // GEOID = 7-digit place FIPS.

    internal static List<GazPlace> ParseGazetteerPlaces(string content,
        IReadOnlyDictionary<string, string> fipsToStateCode)
    {
        var result = new List<GazPlace>();
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
                          : fipsToStateCode.GetValueOrDefault(stateFips, "");

            if (fips.Length == 7 && !string.IsNullOrEmpty(name))
                result.Add(new GazPlace(fips, name, stateFips, stateCode));
        }

        return result;
    }

    // ── Place-to-county relationship file ─────────────────────────────────────
    // Header: GEOID_PLC_20|GEOID_CNTY_20|...|AREALAND_INT|...
    // Returns: placeFips(7) → primary countyFips(5).

    internal static Dictionary<string, string> ParsePlaceCountyRel(string content)
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

            var placeFips  = ExtractFips(placeRaw,  7);
            var countyFips = ExtractFips(countyRaw, 5);

            if (placeFips == null || countyFips == null) continue;

            if (!best.TryGetValue(placeFips, out var prev) || area > prev.Area)
                best[placeFips] = (countyFips, area);
        }

        return best.ToDictionary(kv => kv.Key, kv => kv.Value.CountyFips);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    internal static int ColIdx(string[] header, string name)
    {
        for (int i = 0; i < header.Length; i++)
            if (string.Equals(header[i].Trim(), name, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    // Census GEOIDs sometimes include a summary-level prefix (e.g., "0500000US01001").
    // Strip everything before the last <length> digits.
    internal static string? ExtractFips(string raw, int length)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length < length) return null;
        return digits[^length..];
    }
}
