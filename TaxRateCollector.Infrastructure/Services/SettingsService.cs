using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaxRateCollector.Infrastructure.Services;

/// <summary>
/// Application settings stored at %APPDATA%\MindAttic\TaxRateCollector\settings.json.
/// Injected as a singleton so any page can read or update preferences at runtime.
/// </summary>
public class AppSettings
{
    [JsonPropertyName("theme")] public string Theme { get; set; } = "light";
    [JsonPropertyName("font")] public string Font { get; set; } = "outfit";
    [JsonPropertyName("font_size")] public int FontSize { get; set; } = 14;
    [JsonPropertyName("usps_api_key")] public string UspsApiKey { get; set; } = "";
    [JsonPropertyName("default_update_frequency_days")] public int DefaultUpdateFrequencyDays { get; set; } = 90;
    [JsonPropertyName("evidence_auto_fetch")] public bool EvidenceAutoFetch { get; set; } = false;
    [JsonPropertyName("wayback_machine_fallback")] public bool WaybackMachineFallback { get; set; } = true;

    // ── Census data source URLs (admin-configurable so they can be updated when Census moves files) ──
    [JsonPropertyName("census_county_gaz_url")]
    public string CensusCountyGazUrl { get; set; } =
        "https://www2.census.gov/geo/docs/maps-data/data/gazetteer/2025_Gazetteer/2025_Gaz_counties_national.zip";

    [JsonPropertyName("census_place_gaz_url")]
    public string CensusPlaceGazUrl { get; set; } =
        "https://www2.census.gov/geo/docs/maps-data/data/gazetteer/2025_Gazetteer/2025_Gaz_place_national.zip";

    [JsonPropertyName("census_zcta_county_url")]
    public string CensusZctaCountyUrl { get; set; } =
        "https://www2.census.gov/geo/docs/maps-data/data/rel2020/zcta520/tab20_zcta520_county20_natl.txt";

    [JsonPropertyName("census_zcta_place_url")]
    public string CensusZctaPlaceUrl { get; set; } =
        "https://www2.census.gov/geo/docs/maps-data/data/rel2020/zcta520/tab20_zcta520_place20_natl.txt";
}

public class SettingsService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MindAttic", "TaxRateCollector");

    public static string EvidenceDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MindAttic", "TaxRateCollector", "evidence");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                Current = new AppSettings();
                Save();
                return;
            }
            var json = File.ReadAllText(SettingsPath);
            Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
        }
        catch
        {
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(Current, JsonOpts);
        File.WriteAllText(SettingsPath, json);
    }

    public void SetTheme(string theme)
    {
        Current.Theme = theme;
        Save();
    }
}
