using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using MindAttic.Legion;

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
    [JsonPropertyName("anthropic_api_key")] public string AnthropicApiKey { get; set; } = "";
    [JsonPropertyName("default_update_frequency_days")] public int DefaultUpdateFrequencyDays { get; set; } = 90;
    [JsonPropertyName("evidence_auto_fetch")] public bool EvidenceAutoFetch { get; set; } = false;

    /// <summary>
    /// When true (default), scraped rates go live immediately.
    /// When false, rates are queued for an Administrator or Approver to review before going live.
    /// </summary>
    [JsonPropertyName("auto_approve")] public bool AutoApprove { get; set; } = true;
    [JsonPropertyName("wayback_machine_fallback")] public bool WaybackMachineFallback { get; set; } = true;
    /// <summary>
    /// When true, HTML evidence is saved as a zip bundling the page and all linked
    /// CSS/script/image assets. When false (default), only the raw HTML is zipped.
    /// </summary>
    [JsonPropertyName("full_page_capture")] public bool FullPageCapture { get; set; } = false;

    // ── Census data source URLs ───────────────────────────────────────────────
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

    // ── Streamlined Sales Tax (SST) source URLs ───────────────────────────────
    // Update these when the SST Governing Board publishes a new amendment to the SSUTA.
    [JsonPropertyName("sst_agreement_url")]
    public string SstAgreementUrl { get; set; } =
        "https://www.streamlinedsalestax.org/docs/default-source/agreement/ssuta/ssuta-as-amended-through-12-20-24-with-hyperlinks-and-compiler-notes-at-end-clean-for-posting.pdf";

    [JsonPropertyName("sst_taxability_matrix_url")]
    public string SstTaxabilityMatrixUrl { get; set; } =
        "https://sst.streamlinedsalestax.org/TM";

    [JsonPropertyName("sst_member_states_url")]
    public string SstMemberStatesUrl { get; set; } =
        "https://www.streamlinedsalestax.org/about-us/state-information";

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
        var settingsExisted = File.Exists(SettingsPath);
        try
        {
            if (settingsExisted)
            {
                var json = File.ReadAllText(SettingsPath);
                Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
            }
            else
            {
                Current = new AppSettings();
            }
        }
        catch
        {
            Current = new AppSettings();
        }

        // First-run save BEFORE any cloud overlay so secrets resolved from
        // IConfiguration never get persisted to disk (settings.json should
        // only contain user-typed defaults, never values lifted from User
        // Secrets / App Service / Key Vault).
        if (!settingsExisted) Save();

        // Resolution chain (highest priority first):
        //   1. VaultConfiguration["MindAttic:Vault:LLM:claude:apiKey"] —
        //      User Secrets / App Service Application Settings / Azure Key Vault.
        //   2. %APPDATA%\MindAttic\LLM\providers.json — shared across every
        //      MindAttic app via MindAttic.Legion / Vault file store.
        //   3. Per-app settings.json (Current.AnthropicApiKey) — fallback only.
        // The cloud-native value is held in-memory only — never persisted back
        // to settings.json (Save() runs ABOVE this overlay).
        var fromConfig = VaultConfiguration?["MindAttic:Vault:LLM:claude:apiKey"];
        if (!string.IsNullOrWhiteSpace(fromConfig))
        {
            Current.AnthropicApiKey = fromConfig.Trim();
        }
        else
        {
            var centralAnthropic = MindAtticCredentialStore.GetKey("claude");
            if (!string.IsNullOrWhiteSpace(centralAnthropic))
            {
                Current.AnthropicApiKey = centralAnthropic;
            }
            else if (!string.IsNullOrWhiteSpace(Current.AnthropicApiKey))
            {
                // First-run migration: lift the existing per-app key into the shared store
                // so other MindAttic apps pick it up automatically.
                MindAtticCredentialStore.SetKey("claude", Current.AnthropicApiKey);
            }
        }
    }

    /// <summary>
    /// Optional cloud-native configuration source. Set once at host startup via
    /// <c>SettingsService.VaultConfiguration = builder.Configuration</c>. When set,
    /// the LLM key resolution chain consults <c>MindAttic:Vault:LLM:claude:apiKey</c>
    /// first (User Secrets / App Service Application Settings / Azure Key Vault)
    /// before falling back to the file-based MindAttic store and the legacy
    /// per-app settings.json field.
    /// </summary>
    public static IConfiguration? VaultConfiguration { get; set; }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDir);

        // If the current Anthropic key was loaded from IConfiguration (User Secrets,
        // App Service Application Settings, or Azure Key Vault), don't persist it to
        // settings.json or mirror it into the file store — IConfiguration is the
        // source of truth in cloud deployments and we shouldn't echo secrets back
        // to disk.
        var fromConfig = VaultConfiguration?["MindAttic:Vault:LLM:claude:apiKey"];
        var overlaidFromConfig = !string.IsNullOrWhiteSpace(fromConfig)
            && string.Equals(Current.AnthropicApiKey, fromConfig.Trim(), StringComparison.Ordinal);

        var preservedKey = Current.AnthropicApiKey;
        if (overlaidFromConfig) Current.AnthropicApiKey = "";

        var json = JsonSerializer.Serialize(Current, JsonOpts);
        File.WriteAllText(SettingsPath, json);

        if (overlaidFromConfig) Current.AnthropicApiKey = preservedKey;

        // Mirror the Anthropic key into the shared MindAttic.Legion LLM store so a
        // change made via this app is immediately visible to every other MindAttic
        // app — but ONLY if the user typed it here. Cloud-resolved values stay in
        // their original source (User Secrets / App Service / Key Vault).
        if (!overlaidFromConfig && !string.IsNullOrWhiteSpace(Current.AnthropicApiKey))
        {
            MindAtticCredentialStore.SetKey("claude", Current.AnthropicApiKey);
        }
    }

    public void SetTheme(string theme)
    {
        Current.Theme = theme;
        Save();
    }
}
