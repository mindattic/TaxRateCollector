namespace TaxRateCollector.Core.Constants;

/// <summary>Jurisdiction hierarchy tiers, ordered broadest to most specific.</summary>
public static class JurisdictionTier
{
    public const string Country = "country";
    public const string State = "state";
    public const string County = "county";
    public const string City = "city";
}

/// <summary>How a jurisdiction's tax rate data was obtained.</summary>
public static class TaxSourceType
{
    public const string Api = "api";
    public const string Pdf = "pdf";
    public const string Csv = "csv";
    public const string Website = "website";
    public const string Manual = "manual";
}

/// <summary>Tax rate categories.</summary>
public static class TaxCategory
{
    public const string Sales = "sales";
    public const string Use = "use";
    public const string Excise = "excise";
    public const string Vat = "vat";
    public const string Gst = "gst";
}

/// <summary>How a tax rate is applied.</summary>
public static class TaxRateType
{
    public const string Percentage = "percentage";
    public const string Flat = "flat";
}
