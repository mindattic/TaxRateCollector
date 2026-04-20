namespace TaxRateCollector.Infrastructure.Services;

internal static class ZipCrosswalkParser
{
    // ── Census ZCTA-to-County ─────────────────────────────────────────────────
    // Pipe-delimited, header row includes GEOID_ZCTA5_20, GEOID_COUNTY_20,
    // NAMELSAD_COUNTY_20, AREALAND_PART. Each ZCTA may appear multiple times
    // (once per intersecting county); we pick the county with the largest area.

    internal static Dictionary<string, (string CountyFips, string CountyName)> ParseCountyCrosswalk(string content)
    {
        var best = new Dictionary<string, (string Fips, string Name, long Area)>(StringComparer.Ordinal);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2) return new();

        var header = lines[0].Split('|');
        int iZcta   = ColIdx(header, "GEOID_ZCTA5_20");
        int iCounty = ColIdx(header, "GEOID_COUNTY_20");
        int iName   = ColIdx(header, "NAMELSAD_COUNTY_20");
        int iArea   = ColIdx(header, "AREALAND_PART");
        if (iZcta < 0 || iCounty < 0) return new();

        for (int i = 1; i < lines.Length; i++)
        {
            var cols = lines[i].Split('|');
            if (cols.Length <= Math.Max(iZcta, iCounty)) continue;

            var zcta       = cols[iZcta].Trim();
            var countyFips = cols[iCounty].Trim();
            var name       = iName >= 0 && cols.Length > iName ? cols[iName].Trim() : "";
            long area      = iArea >= 0 && cols.Length > iArea && long.TryParse(cols[iArea].Trim(), out var a) ? a : 0;

            if (zcta.Length != 5 || countyFips.Length != 5) continue;

            if (!best.TryGetValue(zcta, out var prev) || area > prev.Area)
                best[zcta] = (countyFips, name, area);
        }

        return best.ToDictionary(kv => kv.Key, kv => (kv.Value.Fips, kv.Value.Name));
    }

    // ── Census ZCTA-to-Place ──────────────────────────────────────────────────
    // Same pipe format. We pick the place with the largest area and strip
    // Census place-type suffixes (city, town, CDP, …).

    internal static Dictionary<string, string> ParsePlaceCrosswalk(string content)
    {
        var best = new Dictionary<string, (string Name, long Area)>(StringComparer.Ordinal);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2) return new();

        var header = lines[0].Split('|');
        int iZcta = ColIdx(header, "GEOID_ZCTA5_20");
        int iName = ColIdx(header, "NAMELSAD_PLACE_20");
        int iArea = ColIdx(header, "AREALAND_PART");
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    internal static int ColIdx(string[] header, string name)
    {
        for (int i = 0; i < header.Length; i++)
            if (string.Equals(header[i].Trim(), name, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    private static readonly string[] PlaceSuffixes =
    [
        " city", " town", " CDP", " borough", " village", " township",
        " municipality", " comunidad", " zona urbana"
    ];

    internal static string StripPlaceSuffix(string name)
    {
        foreach (var s in PlaceSuffixes)
            if (name.EndsWith(s, StringComparison.OrdinalIgnoreCase))
                return name[..^s.Length].Trim();
        return name;
    }
}
