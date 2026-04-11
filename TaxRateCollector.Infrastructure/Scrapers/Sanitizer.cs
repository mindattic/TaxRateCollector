namespace TaxRateCollector.Infrastructure.Scrapers;

public static class RateSanitizer
{
    public static decimal? Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var cleaned = raw.Trim().TrimEnd('%').Trim();

        if (!decimal.TryParse(cleaned, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
            return null;

        if (value > 1m) value /= 100m;

        return value is > 0m and < 0.20m ? value : null;
    }
}
