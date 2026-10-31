using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MindAttic.Vault.Settings;
using MindAttic.Vault.Settings.Manifest;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Infrastructure.Services;
using TaxRateCollector.UnitTests.Helpers;

namespace TaxRateCollector.UnitTests.Lamp;

/// <summary>
/// LAMP (TRC-T29): one test per TaxRateCollector setting, each end to end — the value in the pre-LAMP
/// settings.json (or, for the Claude extraction options, the host's <c>Anthropic</c> configuration) survives the
/// one-time upgrade under its manifest name, reloads unchanged, and is the value the consuming code uses.
/// </summary>
[TestFixture, NonParallelizable]
public class TrcSettingTests
{
    private static readonly string Prefix = SettingsManifest.PrefixFor(AppSettings.AppId);

    /// <summary>A pre-LAMP settings.json (no claude_model) with <paramref name="fields"/>, upgraded by SettingsService.Load.</summary>
    private static (SettingsService Service, JsonSettingsStore<AppSettings> Store) Upgrade(JsonObject fields, IConfiguration? config = null)
    {
        var store = TestLamp.Store();
        File.WriteAllText(store.FilePath, fields.ToJsonString());
        var svc = new SettingsService(store, config, TestLamp.Keys());
        svc.Load();
        return (svc, store);
    }

    private static AppSettings AssertStoredUnderManifestName(JsonSettingsStore<AppSettings> store, string jsonName, JsonNode expected)
    {
        Assert.That(SettingsManifestBuilder.Build<AppSettings>().Find($"{Prefix}:{jsonName}"), Is.Not.Null, $"manifest has {Prefix}:{jsonName}");
        var json = JsonNode.Parse(File.ReadAllText(store.FilePath))!;
        Assert.That(json[jsonName]?.ToJsonString(), Is.EqualTo(expected.ToJsonString()), $"settings.json {jsonName}");
        return store.Load();
    }

    private static string Source(params string[] path) => File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(path).ToArray()));

    internal static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxRateCollector.slnx"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("TaxRateCollector.slnx not found.");
        }
    }

    // ---- guard --------------------------------------------------------------------------

    [Test]
    public void EverySettingHasItsOwnTest()
    {
        var byJson = typeof(AppSettings).GetProperties()
            .Where(p => p.GetCustomAttribute<JsonPropertyNameAttribute>() is not null)
            .ToDictionary(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name, p => p.Name);
        var tested = GetType().GetMethods().Select(m => m.Name).Where(n => n.StartsWith("Setting_")).ToList();
        var fields = SettingsManifestBuilder.Build<AppSettings>().Fields;
        Assert.That(fields, Has.Count.EqualTo(18));
        foreach (var f in fields)
            Assert.That(tested.Count(t => t.StartsWith($"Setting_{byJson[f.Property]}_")), Is.EqualTo(1), f.Property);
    }

    // ---- Appearance ---------------------------------------------------------------------

    [Test]
    public void Setting_Theme_SurvivesTheUpgradeAndIsAppliedByTheLayout()
    {
        var (svc, store) = Upgrade(new JsonObject { ["theme"] = "samurai" });
        Assert.That(AssertStoredUnderManifestName(store, "theme", "samurai").Theme, Is.EqualTo("samurai"));
        Assert.That(svc.Current.Theme, Is.EqualTo("samurai"));
        Assert.That(Source("TaxRateCollector.Blazor", "Components", "Layout", "MainLayout.razor"), Does.Contain("\"applyTheme\", s.Theme"));
    }

    [Test]
    public void Setting_Font_SurvivesTheUpgradeAndIsAppliedByTheLayout()
    {
        var (svc, store) = Upgrade(new JsonObject { ["font"] = "lato" });
        Assert.That(AssertStoredUnderManifestName(store, "font", "lato").Font, Is.EqualTo("lato"));
        Assert.That(svc.Current.Font, Is.EqualTo("lato"));
        Assert.That(Source("TaxRateCollector.Blazor", "Components", "Layout", "MainLayout.razor"), Does.Contain("\"applyFont\", s.Font, s.FontSize"));
    }

    [Test]
    public void Setting_FontSize_SurvivesTheUpgradeAndIsAppliedByTheLayout()
    {
        var (svc, store) = Upgrade(new JsonObject { ["font_size"] = 16 });
        Assert.That(AssertStoredUnderManifestName(store, "font_size", 16).FontSize, Is.EqualTo(16));
        Assert.That(svc.Current.FontSize, Is.EqualTo(16));
    }

    // ---- Scraping -----------------------------------------------------------------------

    [Test]
    public void Setting_DefaultUpdateFrequencyDays_SurvivesTheUpgradeAndIsShownOnSetup()
    {
        var (svc, store) = Upgrade(new JsonObject { ["default_update_frequency_days"] = 30 });
        Assert.That(AssertStoredUnderManifestName(store, "default_update_frequency_days", 30).DefaultUpdateFrequencyDays, Is.EqualTo(30));
        Assert.That(svc.Current.DefaultUpdateFrequencyDays, Is.EqualTo(30));
        Assert.That(Source("TaxRateCollector.Blazor", "Components", "Pages", "Setup.razor"), Does.Contain("settings.Current.DefaultUpdateFrequencyDays"));
    }

    [Test]
    public void Setting_EvidenceAutoFetch_SurvivesTheUpgradeAndIsShownOnSetup()
    {
        var (svc, store) = Upgrade(new JsonObject { ["evidence_auto_fetch"] = true });
        Assert.That(AssertStoredUnderManifestName(store, "evidence_auto_fetch", true).EvidenceAutoFetch, Is.True);
        Assert.That(svc.Current.EvidenceAutoFetch, Is.True);
        Assert.That(Source("TaxRateCollector.Blazor", "Components", "Pages", "Setup.razor"), Does.Contain("settings.Current.EvidenceAutoFetch"));
    }

    [Test]
    public void Setting_AutoApprove_SurvivesTheUpgradeAndDecidesWhetherScrapedRatesNeedReview()
    {
        var (svc, store) = Upgrade(new JsonObject { ["auto_approve"] = false });
        Assert.That(AssertStoredUnderManifestName(store, "auto_approve", false).AutoApprove, Is.False);
        Assert.That(svc.Current.AutoApprove, Is.False);
        Assert.That(Source("TaxRateCollector.Blazor", "Program.cs"), Does.Contain("needsReview: !scrapeSettings.Current.AutoApprove"));
        Assert.That(Source("TaxRateCollector.Blazor", "Components", "Pages", "Setup.razor"), Does.Contain("!settings.Current.AutoApprove"));
    }

    [Test]
    public async Task Setting_WaybackMachineFallback_SurvivesTheUpgradeAndIsUsedByDiscovery()
    {
        var (svc, store) = Upgrade(new JsonObject { ["wayback_machine_fallback"] = false });
        Assert.That(AssertStoredUnderManifestName(store, "wayback_machine_fallback", false).WaybackMachineFallback, Is.False);

        var discovery = new DiscoveryService(new HttpClient(new FailingHandler()), svc, NullLogger<DiscoveryService>.Instance);
        var off = await discovery.DiscoverAsync(new Jurisdiction { Id = 1, JurisdictionName = "X", StateCode = "TX", JurisdictionType = JurisdictionType.State, SourceUrl = "https://example.invalid/a" });
        Assert.That(off.Notes, Is.EqualTo("URL unreachable"), "no archive lookup when off");

        store.Update(s => s.WaybackMachineFallback = true);   // saved in Vault
        var on = await discovery.DiscoverAsync(new Jurisdiction { Id = 1, JurisdictionName = "X", StateCode = "TX", JurisdictionType = JurisdictionType.State, SourceUrl = "https://example.invalid/a" });
        Assert.That(on.Notes, Does.Contain("no archive snapshot"), "archive consulted when on");
    }

    [Test]
    public void Setting_FullPageCapture_SurvivesTheUpgradeAndIsShownOnSetup()
    {
        var (svc, store) = Upgrade(new JsonObject { ["full_page_capture"] = true });
        Assert.That(AssertStoredUnderManifestName(store, "full_page_capture", true).FullPageCapture, Is.True);
        Assert.That(svc.Current.FullPageCapture, Is.True);
        Assert.That(Source("TaxRateCollector.Blazor", "Components", "Pages", "Setup.razor"), Does.Contain("settings.Current.FullPageCapture"));
    }

    // ---- Claude extraction (imported from the host's Anthropic configuration) -------------

    private static IConfiguration Anthropic(string model, int maxTokens, int maxChars) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Anthropic:Model"] = model,
            ["Anthropic:MaxTokens"] = maxTokens.ToString(),
            ["Anthropic:MaxContentChars"] = maxChars.ToString(),
        }).Build();

    private static async Task<string> ExtractionRequest(SettingsService svc, string content = "Rate: 6.25%")
    {
        var http = new CapturingHttpHandler("""{"content":[{"type":"text","text":"[]"}]}""");
        var extractor = new ClaudeRateLawExtractor(new MindAttic.Legion.LegionClient(new HttpClient(http)),
            Options.Create(svc.Current.ToAnthropicOptions()), svc, NullLogger<ClaudeRateLawExtractor>.Instance);
        await extractor.ExtractAsync(new Jurisdiction { Id = 1, StateCode = "IL", JurisdictionName = "Illinois", JurisdictionType = JurisdictionType.State, FipsCode = "17" },
            content, "text/plain", "https://test.gov");
        return http.LastRequestBody ?? "";
    }

    private static (SettingsService Service, JsonSettingsStore<AppSettings> Store) UpgradeWithKey(IConfiguration config)
    {
        var store = TestLamp.Store();
        File.WriteAllText(store.FilePath, """{"theme":"light"}""");
        var keys = TestLamp.KeyStore();
        keys.Add("claude", "key1", "sk-test");
        var svc = new SettingsService(store, config, TestLamp.Keys(keys));
        svc.Load();
        return (svc, store);
    }

    [Test]
    public async Task Setting_ClaudeModel_IsImportedFromConfigurationAndSentToClaude()
    {
        var (svc, store) = UpgradeWithKey(Anthropic("claude-sonnet-4-6", 4096, 60000));
        Assert.That(AssertStoredUnderManifestName(store, "claude_model", "claude-sonnet-4-6").ClaudeModel, Is.EqualTo("claude-sonnet-4-6"));
        Assert.That(await ExtractionRequest(svc), Does.Contain("\"claude-sonnet-4-6\""));
    }

    [Test]
    public async Task Setting_ClaudeMaxTokens_IsImportedFromConfigurationAndSentToClaude()
    {
        var (svc, store) = UpgradeWithKey(Anthropic("m", 2345, 60000));
        Assert.That(AssertStoredUnderManifestName(store, "claude_max_tokens", 2345).ClaudeMaxTokens, Is.EqualTo(2345));
        Assert.That(await ExtractionRequest(svc), Does.Contain("2345"));
    }

    [Test]
    public async Task Setting_ClaudeMaxContentChars_IsImportedFromConfigurationAndTruncatesTheSource()
    {
        var (svc, store) = UpgradeWithKey(Anthropic("m", 4096, 1000));
        Assert.That(AssertStoredUnderManifestName(store, "claude_max_content_chars", 1000).ClaudeMaxContentChars, Is.EqualTo(1000));
        var body = await ExtractionRequest(svc, new string('q', 5000));
        Assert.That(body.Count(c => c == 'q'), Is.LessThanOrEqualTo(1000 + 50));
    }

    // ---- Data sources ---------------------------------------------------------------------

    private static void AssertUrl(string jsonName, Func<AppSettings, string> read, params string[] consumers)
    {
        var url = $"https://example.test/{jsonName}.zip";
        var (svc, store) = Upgrade(new JsonObject { [jsonName] = url });
        Assert.That(read(AssertStoredUnderManifestName(store, jsonName, url)), Is.EqualTo(url));
        Assert.That(read(svc.Current), Is.EqualTo(url));
        var prop = typeof(AppSettings).GetProperties().Single(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name == jsonName).Name;
        foreach (var file in consumers)
            Assert.That(Source("TaxRateCollector.Infrastructure", "Services", file), Does.Contain($"Current.{prop}"), file);
        Assert.That(Source("TaxRateCollector.Blazor", "Components", "Pages", "Setup.razor"), Does.Contain($"settings.Current.{prop}"), "Setup checks it");
    }

    [Test]
    public void Setting_CensusCountyGazUrl_SurvivesTheUpgradeAndIsDownloadedByTheImporter() =>
        AssertUrl("census_county_gaz_url", s => s.CensusCountyGazUrl, "CensusJurisdictionImportService.cs");

    [Test]
    public void Setting_CensusPlaceGazUrl_SurvivesTheUpgradeAndIsDownloadedByTheImporter() =>
        AssertUrl("census_place_gaz_url", s => s.CensusPlaceGazUrl, "CensusJurisdictionImportService.cs");

    [Test]
    public void Setting_CensusZctaCountyUrl_SurvivesTheUpgradeAndIsDownloadedByTheImporters() =>
        AssertUrl("census_zcta_county_url", s => s.CensusZctaCountyUrl, "CensusJurisdictionImportService.cs", "ZipImportService.cs");

    [Test]
    public void Setting_CensusZctaPlaceUrl_SurvivesTheUpgradeAndIsDownloadedByTheImporters() =>
        AssertUrl("census_zcta_place_url", s => s.CensusZctaPlaceUrl, "CensusJurisdictionImportService.cs", "ZipImportService.cs");

    [Test]
    public void Setting_SstAgreementUrl_SurvivesTheUpgradeAndIsUsedByTheTaxonomyImport() =>
        AssertUrl("sst_agreement_url", s => s.SstAgreementUrl, "SstTaxonomyImportService.cs");

    [Test]
    public void Setting_SstTaxabilityMatrixUrl_SurvivesTheUpgradeAndIsCheckedOnSetup() =>
        AssertUrl("sst_taxability_matrix_url", s => s.SstTaxabilityMatrixUrl);

    [Test]
    public void Setting_SstMemberStatesUrl_SurvivesTheUpgradeAndIsCheckedOnSetup() =>
        AssertUrl("sst_member_states_url", s => s.SstMemberStatesUrl);

    // ---- fakes ------------------------------------------------------------------------------

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"archived_snapshots":{}}""", Encoding.UTF8, "application/json") });
    }

    private sealed class CapturingHttpHandler(string body) : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
