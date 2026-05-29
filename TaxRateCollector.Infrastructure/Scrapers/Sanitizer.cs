namespace TaxRateCollector.Infrastructure.Scrapers;

public static class RateSanitizer
{
    public static decimal? Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var trimmed = raw.Trim();
        var wasPercent = trimmed.EndsWith('%');
        var cleaned = trimmed.TrimEnd('%').Trim();

        if (!decimal.TryParse(cleaned, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
            return null;

        // A token tagged with '%' is always a percent (e.g. "1%" → 0.01, "0.5%" → 0.005).
        // An untagged value > 1 is assumed to be a whole-number percent (e.g. "7.5" → 0.075).
        if (wasPercent || value > 1m) value /= 100m;

        return value is > 0m and < 0.20m ? value : null;
    }
}
