using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using MindAttic.Vault.Credentials;
using MindAttic.Vault.Paths;
using MindAttic.Vault.Settings;
using MindAttic.Vault.Settings.Manifest;
using TaxRateCollector.Core.Options;

namespace TaxRateCollector.Infrastructure.Services;

/// <summary>
/// TaxRateCollector's app-level settings (LAMP, TRC-T29) in <c>%APPDATA%\MindAttic\TaxRateCollector\settings.json</c>
/// — the same file and the same snake_case names as before, so the existing file loads as is — edited only in
/// MindAttic.Vault.Desktop. Property initializers are the defaults TaxRateCollector used before LAMP. Keys (Claude,
/// USPS) are never here: they live in Vault's shared named lists.
/// </summary>
[SettingsApp(AppId, "TaxRateCollector", Icon = "receipt",
    Description = "Collects US sales and excise tax rates from state sources, with evidence and review.")]
[UsesCredential("claude", Purpose = "AI extraction of tax rates from raw source pages")]
[UsesCredential("usps", Purpose = "Optional: USPS CityStateLookup to confirm ZIP city names")]
public class AppSettings
{
    /// <summary>Settings app id: TaxRateCollector's MindAttic folder name.</summary>
    public const string AppId = "TaxRateCollector";

    // ── Appearance ───────────────────────────────────────────────────────────

    [Setting("Theme", Group = "Appearance", Order = 1, Help = "Color theme for the whole site.")]
    [SettingChoices("light", "dark", "spring", "summer", "autumn", "winter", "tutor", "llm", "samurai")]
    [JsonPropertyName("theme"), ConfigurationKeyName("theme")] public string Theme { get; set; } = "light";

    [Setting("Font", Group = "Appearance", Order = 2, Help = "Typeface for the whole site.")]
    [SettingChoices("outfit", "roboto", "lato", "calibri", "arial", "times")]
    [JsonPropertyName("font"), ConfigurationKeyName("font")] public string Font { get; set; } = "outfit";

    [Setting("Font size (px)", Group = "Appearance", Order = 3, Help = "Base text size.")]
    [SettingRange(8, 32)]
    [JsonPropertyName("font_size"), ConfigurationKeyName("font_size")] public int FontSize { get; set; } = 14;

    // ── Scraping ─────────────────────────────────────────────────────────────

    [Setting("Re-scrape interval (days)", Group = "Scraping", Order = 1, Help = "How often a jurisdiction's rates are re-checked.")]
    [SettingRange(1, 365)]
    [JsonPropertyName("default_update_frequency_days"), ConfigurationKeyName("default_update_frequency_days")]
    public int DefaultUpdateFrequencyDays { get; set; } = 90;

    [Setting("Auto-fetch evidence PDFs", Group = "Scraping", Order = 2, Help = "Download source documents as evidence while scraping.")]
    [JsonPropertyName("evidence_auto_fetch"), ConfigurationKeyName("evidence_auto_fetch")] public bool EvidenceAutoFetch { get; set; } = false;

    /// <summary>
    /// When true (default), scraped rates go live immediately.
    /// When false, rates are queued for an Administrator or Approver to review before going live.
    /// </summary>
    [Setting("Auto-approve scraped rates", Group = "Scraping", Order = 3,
        Help = "On: scraped rates go live immediately. Off: they queue for an Administrator or Approver to review.")]
    [JsonPropertyName("auto_approve"), ConfigurationKeyName("auto_approve")] public bool AutoApprove { get; set; } = true;

    [Setting("Wayback Machine fallback", Group = "Scraping", Order = 4, Help = "Use the Internet Archive when a source URL returns 404.")]
    [JsonPropertyName("wayback_machine_fallback"), ConfigurationKeyName("wayback_machine_fallback")] public bool WaybackMachineFallback { get; set; } = true;

    /// <summary>
    /// When true, HTML evidence is saved as a zip bundling the page and all linked
    /// CSS/script/image assets. When false (default), only the raw HTML is zipped.
    /// </summary>
    [Setting("Full-page capture for HTML evidence", Group = "Scraping", Order = 5,
        Help = "Off: raw HTML only (fast). On: bundles the page's CSS, images and scripts into the evidence zip.")]
    [JsonPropertyName("full_page_capture"), ConfigurationKeyName("full_page_capture")] public bool FullPageCapture { get; set; } = false;

    // ── Claude extraction ────────────────────────────────────────────────────

    [Setting("Claude model", Group = "Claude extraction", Order = 1, Help = "Model used to extract rate laws from source pages.")]
    [JsonPropertyName("claude_model"), ConfigurationKeyName("claude_model")] public string ClaudeModel { get; set; } = new AnthropicOptions().Model;

    [Setting("Max response tokens", Group = "Claude extraction", Order = 2, Help = "Token cap for each extraction response.")]
    [SettingRange(256, 64000)]
    [JsonPropertyName("claude_max_tokens"), ConfigurationKeyName("claude_max_tokens")] public int ClaudeMaxTokens { get; set; } = new AnthropicOptions().MaxTokens;

    [Setting("Max source characters", Group = "Claude extraction", Order = 3, Help = "Longest slice of a source page sent to Claude per request.")]
    [SettingRange(1000, 1_000_000)]
    [JsonPropertyName("claude_max_content_chars"), ConfigurationKeyName("claude_max_content_chars")]
    public int ClaudeMaxContentChars { get; set; } = new AnthropicOptions().MaxContentChars;

    // ── Census data source URLs ───────────────────────────────────────────────

    [Setting("County Gazetteer (ZIP)", Group = "Data sources", Order = 1, Help = "~3,200 US counties with FIPS codes (Census Bureau).")]
    [UrlSetting]
    [JsonPropertyName("census_county_gaz_url"), ConfigurationKeyName("census_county_gaz_url")]
    public string CensusCountyGazUrl { get; set; } =
        "https://www2.census.gov/geo/docs/maps-data/data/gazetteer/2025_Gazetteer/2025_Gaz_counties_national.zip";

    [Setting("Place Gazetteer (ZIP)", Group = "Data sources", Order = 2, Help = "~30,000 incorporated cities and places (Census Bureau).")]
    [UrlSetting]
    [JsonPropertyName("census_place_gaz_url"), ConfigurationKeyName("census_place_gaz_url")]
    public string CensusPlaceGazUrl { get; set; } =
        "https://www2.census.gov/geo/docs/maps-data/data/gazetteer/2025_Gazetteer/2025_Gaz_place_national.zip";

    [Setting("ZCTA to county crosswalk (TXT)", Group = "Data sources", Order = 3, Help = "Maps ZIP codes to counties by land area.")]
    [UrlSetting]
    [JsonPropertyName("census_zcta_county_url"), ConfigurationKeyName("census_zcta_county_url")]
    public string CensusZctaCountyUrl { get; set; } =
        "https://www2.census.gov/geo/docs/maps-data/data/rel2020/zcta520/tab20_zcta520_county20_natl.txt";

    [Setting("ZCTA to place crosswalk (TXT)", Group = "Data sources", Order = 4, Help = "Maps ZIP codes to cities by land area.")]
    [UrlSetting]
    [JsonPropertyName("census_zcta_place_url"), ConfigurationKeyName("census_zcta_place_url")]
    public string CensusZctaPlaceUrl { get; set; } =
        "https://www2.census.gov/geo/docs/maps-data/data/rel2020/zcta520/tab20_zcta520_place20_natl.txt";

    // ── Streamlined Sales Tax (SST) source URLs ───────────────────────────────
    // Update these when the SST Governing Board publishes a new amendment to the SSUTA.

    [Setting("SSUTA Agreement PDF", Group = "Data sources", Order = 5, Help = "Appendix C is the authoritative product/service taxonomy.")]
    [UrlSetting]
    [JsonPropertyName("sst_agreement_url"), ConfigurationKeyName("sst_agreement_url")]
    public string SstAgreementUrl { get; set; } =
        "https://www.streamlinedsalestax.org/docs/default-source/agreement/ssuta/ssuta-as-amended-through-12-20-24-with-hyperlinks-and-compiler-notes-at-end-clean-for-posting.pdf";

    [Setting("SST Taxability Matrix", Group = "Data sources", Order = 6, Help = "SST Governing Board taxability matrix (web).")]
    [UrlSetting]
    [JsonPropertyName("sst_taxability_matrix_url"), ConfigurationKeyName("sst_taxability_matrix_url")]
    public string SstTaxabilityMatrixUrl { get; set; } =
        "https://sst.streamlinedsalestax.org/TM";

    [Setting("SST member states", Group = "Data sources", Order = 7, Help = "SST member states and news page (web).")]
    [UrlSetting]
    [JsonPropertyName("sst_member_states_url"), ConfigurationKeyName("sst_member_states_url")]
    public string SstMemberStatesUrl { get; set; } =
        "https://www.streamlinedsalestax.org/about-us/state-information";

    /// <summary>The Claude extraction options these settings describe.</summary>
    public AnthropicOptions ToAnthropicOptions() =>
        new() { Model = ClaudeModel, MaxTokens = ClaudeMaxTokens, MaxContentChars = ClaudeMaxContentChars };
}

/// <summary>
/// Reads TaxRateCollector's settings and keys (LAMP, TRC-T29). <see cref="Current"/> is the deployed configuration
/// (<c>MindAttic:Settings:TaxRateCollector</c>) when present, else the Vault file, reloaded when Vault.Desktop saves it.
/// There is no settings editor in TaxRateCollector.
/// </summary>
public class SettingsService
{
    /// <summary>The pre-LAMP fields that held keys; moved into Vault's named lists, then removed from settings.json.</summary>
    public const string LegacyUspsKeyField = "usps_api_key";
    public const string LegacyAnthropicKeyField = "anthropic_api_key";

    /// <summary>Copy of settings.json taken before the one-time upgrade removes its key fields (the rollback).</summary>
    public const string BackupFileName = "settings.pre-lamp.json";

    /// <summary>Name the imported keys get in Vault's shared lists.</summary>
    public const string ImportedKeyName = "TaxRateCollector (imported)";

    private readonly ISettingsStore<AppSettings> store;
    private readonly IConfiguration? configuration;
    private AppSettings current = new();
    private DateTime loadedStamp = DateTime.MinValue;

    /// <summary>Production: the Vault file for this user, local keys.</summary>
    public SettingsService() : this(null, null, null) { }

    public SettingsService(ISettingsStore<AppSettings>? store, IConfiguration? configuration = null, KeySelector? keys = null)
    {
        this.store = store ?? JsonSettingsStore<AppSettings>.ForSettingsApp();
        this.configuration = configuration;
        Keys = keys ?? KeySelector.ForCurrentUser(ClassifyLegionFailure);
    }

    /// <summary>
    /// Evidence files beside the settings (<c>%APPDATA%\MindAttic\TaxRateCollector\evidence</c>), resolved through
    /// VaultPaths so the test roots apply.
    /// </summary>
    public static string EvidenceDirectory => Path.Combine(VaultPaths.RoamingBucket(AppSettings.AppId), "evidence");

    /// <summary>Downloaded source caches (<c>%APPDATA%\MindAttic\TaxRateCollector\cache</c>).</summary>
    public static string CacheDirectory => Path.Combine(VaultPaths.RoamingBucket(AppSettings.AppId), "cache");

    /// <summary>The key lists TaxRateCollector draws from (Claude, USPS).</summary>
    public KeySelector Keys { get; }

    /// <summary>The settings store (diagnostics and tests).</summary>
    public ISettingsStore<AppSettings> Store => store;

    /// <summary>
    /// The settings in effect. The in-memory instance is kept until the file changes on disk (a Vault.Desktop
    /// save), then reloaded; deployed configuration always wins.
    /// </summary>
    public AppSettings Current
    {
        get
        {
            var deployed = FromConfiguration(configuration);
            if (deployed is not null) return deployed;
            var stamp = Stamp();
            if (stamp != loadedStamp)
            {
                current = store.Load();
                loadedStamp = stamp;
            }
            return current;
        }
    }

    private DateTime Stamp()
    {
        try { return File.Exists(store.FilePath) ? File.GetLastWriteTimeUtc(store.FilePath) : DateTime.MinValue; }
        catch { return DateTime.MinValue; }
    }

    /// <summary>Deployed values (<c>MindAttic:Settings:TaxRateCollector</c>), or null.</summary>
    public static AppSettings? FromConfiguration(IConfiguration? configuration)
    {
        var section = configuration?.GetSection(SettingsManifest.PrefixFor(AppSettings.AppId));
        return section is not null && section.Exists() ? section.Get<AppSettings>() ?? new AppSettings() : null;
    }

    /// <summary>
    /// Startup: the one-time LAMP upgrade (see <see cref="Upgrade"/>), seeding defaults and writing the manifest.
    /// Skipped when the deployed configuration supplies the settings.
    /// </summary>
    public void Load()
    {
        if (FromConfiguration(configuration) is null)
        {
            Upgrade(store, configuration, NamedKeyStoreOrNull());
            store.LoadOrSeed();
        }
        loadedStamp = DateTime.MinValue;   // re-read on next access
    }

    private static NamedKeyStore? NamedKeyStoreOrNull()
    {
        try { return NamedKeyStore.ForRoamingRoot(); }
        catch { return null; }
    }

    /// <summary>
    /// One-time upgrade of a pre-LAMP settings.json (it has no <c>claude_model</c> yet): its key fields move into
    /// Vault's shared named lists without changing which key TaxRateCollector uses (the Claude one is added after the
    /// shared keys, as the last fallback it already was; the USPS one becomes TaxRateCollector's USPS key), the
    /// <c>Anthropic</c> options the host configured are imported, a copy of the original file is kept as
    /// <see cref="BackupFileName"/>, and the file is saved without the key fields. Returns true when it ran.
    /// </summary>
    public static bool Upgrade(ISettingsStore<AppSettings> store, IConfiguration? configuration, NamedKeyStore? keys)
    {
        if (!store.Exists()) return false;
        JsonObject raw;
        try { raw = JsonNode.Parse(File.ReadAllText(store.FilePath))?.AsObject() ?? new JsonObject(); }
        catch { return false; }
        if (raw.ContainsKey("claude_model")) return false;

        var backup = Path.Combine(Path.GetDirectoryName(store.FilePath)!, BackupFileName);
        if (!File.Exists(backup)) File.Copy(store.FilePath, backup);

        if (keys is not null)
        {
            var importer = new KeyImporter(keys);
            if (Text(raw, LegacyAnthropicKeyField) is { Length: > 0 } claude)
                importer.ImportValue("claude", ImportedKeyName, claude, appId: null);
            if (Text(raw, LegacyUspsKeyField) is { Length: > 0 } usps)
                importer.ImportValue("usps", ImportedKeyName, usps, AppSettings.AppId);
        }

        var settings = store.Load();
        var anthropic = configuration?.GetSection(AnthropicOptions.Section);
        if (anthropic is not null && anthropic.Exists())
        {
            var o = anthropic.Get<AnthropicOptions>() ?? new AnthropicOptions();
            settings.ClaudeModel = o.Model;
            settings.ClaudeMaxTokens = o.MaxTokens;
            settings.ClaudeMaxContentChars = o.MaxContentChars;
        }
        store.Save(settings);
        return true;
    }

    private static string? Text(JsonObject o, string name) =>
        o[name] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;

    /// <summary>The key TaxRateCollector uses for <paramref name="providerId"/> right now, or null.</summary>
    public string? GetKey(string providerId) => Keys.GetActiveKey(providerId, AppSettings.AppId)?.Value;

    /// <summary>A 401/403/402/429 anywhere in Legion's exception chain means the key was refused.</summary>
    public static KeyFailureException? ClassifyLegionFailure(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is HttpRequestException { StatusCode: { } status } && KeySelector.Classify(status) is { } kind)
                return new KeyFailureException(kind, $"HTTP {(int)status}");
        return null;
    }
}

/// <summary>TaxRateCollector's Settings cog: opens MindAttic.Vault.Desktop at its tab (LAMP, TRC-T29).</summary>
public static class SettingsCog
{
    public sealed record Result(bool Opened, string Message);

    public static Result Open(string? setting = null) =>
        MindAttic.Vault.Launch.VaultSettings.Open(AppSettings.AppId, setting)
            ? new Result(true, "Opened TaxRateCollector's settings in MindAttic Vault.")
            : new Result(false, "MindAttic Vault isn't installed or couldn't start on the server, so settings can't be opened here. " +
                                "Use the Vault link, or install MindAttic.Vault.Desktop (or set MINDATTIC_VAULT_DESKTOP_EXE).");

    /// <summary>The <c>mindattic-vault://</c> link for a browser (and for a deployed site).</summary>
    public static string Link(string? setting = null) => MindAttic.Vault.Launch.VaultSettings.Link(AppSettings.AppId, setting);

    /// <summary>A setting's full name, e.g. <c>MindAttic:Settings:TaxRateCollector:auto_approve</c>.</summary>
    public static string FullName(string jsonName) => $"{SettingsManifest.PrefixFor(AppSettings.AppId)}:{jsonName}";
}
