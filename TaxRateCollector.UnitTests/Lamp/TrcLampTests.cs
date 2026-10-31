using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MindAttic.Vault.Credentials;
using MindAttic.Vault.Launch;
using MindAttic.Vault.Paths;
using MindAttic.Vault.Settings;
using MindAttic.Vault.Settings.Manifest;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Options;
using TaxRateCollector.Infrastructure.Services;
using TaxRateCollector.UnitTests.Helpers;

namespace TaxRateCollector.UnitTests.Lamp;

/// <summary>
/// LAMP (TRC-T29) app-level checks: manifest, defaults, the one-time upgrade of the existing settings.json (keys out
/// into Vault's named lists without changing which key is used, backup kept), deployed configuration, Claude key
/// failover, the cog, and the old settings UI being gone.
/// </summary>
[TestFixture, NonParallelizable]
public class TrcLampTests
{
    private static readonly string Prefix = SettingsManifest.PrefixFor(AppSettings.AppId);
    private static string RepoRoot => TrcSettingTests.RepoRoot;

    // ---- Manifest / defaults ---------------------------------------------------------------

    [Test]
    public void Manifest_builds_with_every_field_labeled_grouped_and_helped_and_no_secrets()
    {
        var m = SettingsManifestBuilder.Build<AppSettings>();
        Assert.Multiple(() =>
        {
            Assert.That(m.AppId, Is.EqualTo("TaxRateCollector"));
            foreach (var f in m.Fields)
            {
                Assert.That(f.Label, Is.Not.Empty, f.Name);
                Assert.That(f.Help, Is.Not.Null.And.Not.Empty, f.Name);
                Assert.That(f.Kind, Is.Not.EqualTo(SettingKind.Unknown), f.Name);
                Assert.That(f.Name, Is.EqualTo($"{Prefix}:{f.Property}"));
                Assert.That(f.Property, Does.Not.Contain("api_key"));
            }
        });
        Assert.That(m.Fields.Select(f => f.Group).Distinct(), Is.EquivalentTo(new[] { "Appearance", "Scraping", "Claude extraction", "Data sources" }));
        Assert.That(m.Credentials.Select(c => c.ProviderId), Is.EquivalentTo(new[] { "claude", "usps" }));
    }

    [Test]
    public void Fresh_machine_seeds_todays_defaults_and_the_manifest()
    {
        var store = TestLamp.Store();
        new SettingsService(store, null, TestLamp.Keys()).Load();
        var r = JsonNode.Parse(File.ReadAllText(store.FilePath))!;
        Assert.Multiple(() =>
        {
            Assert.That((string)r["theme"]!, Is.EqualTo("light"));
            Assert.That((string)r["font"]!, Is.EqualTo("outfit"));
            Assert.That((int)r["font_size"]!, Is.EqualTo(14));
            Assert.That((int)r["default_update_frequency_days"]!, Is.EqualTo(90));
            Assert.That((bool)r["evidence_auto_fetch"]!, Is.False);
            Assert.That((bool)r["auto_approve"]!, Is.True);
            Assert.That((bool)r["wayback_machine_fallback"]!, Is.True);
            Assert.That((bool)r["full_page_capture"]!, Is.False);
            Assert.That((string)r["claude_model"]!, Is.EqualTo(new AnthropicOptions().Model));
            Assert.That((int)r["claude_max_tokens"]!, Is.EqualTo(4096));
            Assert.That((int)r["claude_max_content_chars"]!, Is.EqualTo(60000));
            Assert.That((string)r["census_county_gaz_url"]!, Does.StartWith("https://www2.census.gov/"));
            Assert.That(r.AsObject().ContainsKey("anthropic_api_key"), Is.False);
            Assert.That(r.AsObject().ContainsKey("usps_api_key"), Is.False);
        });
        Assert.That(File.Exists(Path.Combine(store.Directory, SettingsManifest.FileName)), Is.True);
    }

    [Test]
    public void Settings_stay_in_the_existing_TaxRateCollector_file_with_the_same_names()
    {
        var store = JsonSettingsStore<AppSettings>.ForSettingsApp();
        Assert.That(store.FilePath, Is.EqualTo(Path.Combine(VaultPaths.RoamingBucket("TaxRateCollector"), "settings.json")));
        Assert.That(SettingsService.EvidenceDirectory, Is.EqualTo(Path.Combine(VaultPaths.RoamingBucket("TaxRateCollector"), "evidence")));
        Assert.That(SettingsService.CacheDirectory, Is.EqualTo(Path.Combine(VaultPaths.RoamingBucket("TaxRateCollector"), "cache")));
    }

    [Test]
    public void Write_settings_manifest_switch_seeds_and_exits()
    {
        var output = new StringWriter();
        Assert.That(SettingsManifestCommand.TryRun<AppSettings>(new[] { "--write-settings-manifest" }, output), Is.True);
        Assert.That(File.Exists(output.ToString().Trim()), Is.True);
        Assert.That(File.ReadAllText(Path.Combine(RepoRoot, "TaxRateCollector.Blazor", "Program.cs")),
            Does.Contain("SettingsManifestCommand.TryRun<AppSettings>(args)"));
    }

    // ---- The one-time upgrade --------------------------------------------------------------

    [Test]
    public void Upgrade_moves_the_keys_into_Vault_keeps_the_shared_key_first_keeps_a_backup_and_runs_once()
    {
        var store = TestLamp.Store();
        var original = """{"theme":"dark","font_size":16,"usps_api_key":"usps-secret","anthropic_api_key":"sk-old-app-key","auto_approve":false}""";
        File.WriteAllText(store.FilePath, original);
        var keys = TestLamp.KeyStore();
        keys.Add("claude", "shared", "sk-shared");

        Assert.That(SettingsService.Upgrade(store, null, keys), Is.True);

        var after = File.ReadAllText(store.FilePath);
        Assert.That(after, Does.Not.Contain("usps-secret").And.Not.Contain("sk-old-app-key"), "no keys left in settings.json");
        Assert.That(File.ReadAllText(Path.Combine(store.Directory, SettingsService.BackupFileName)), Is.EqualTo(original), "rollback copy, untouched");
        Assert.That(store.Load().Theme, Is.EqualTo("dark"));
        Assert.That(store.Load().AutoApprove, Is.False);

        var selector = TestLamp.Keys(keys);
        Assert.That(selector.GetActiveKey("claude", "TaxRateCollector")?.Value, Is.EqualTo("sk-shared"),
            "the shared key stays first, as it won over settings.json before");
        Assert.That(keys.GetKeys("claude").Select(k => k.Value), Does.Contain("sk-old-app-key"), "kept as a fallback");
        Assert.That(selector.GetActiveKey("usps", "TaxRateCollector")?.Value, Is.EqualTo("usps-secret"));

        Assert.That(SettingsService.Upgrade(store, null, keys), Is.False, "runs once");
    }

    [Test]
    public void Upgrade_imports_the_hosts_Anthropic_options()
    {
        var store = TestLamp.Store();
        File.WriteAllText(store.FilePath, """{"theme":"light"}""");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Anthropic:Model"] = "claude-sonnet-4-6", ["Anthropic:MaxTokens"] = "4096", ["Anthropic:MaxContentChars"] = "60000",
        }).Build();
        SettingsService.Upgrade(store, config, TestLamp.KeyStore());
        Assert.That(store.Load().ClaudeModel, Is.EqualTo("claude-sonnet-4-6"));
        Assert.That(File.ReadAllText(Path.Combine(RepoRoot, "TaxRateCollector.Blazor", "appsettings.Development.json")), Does.Not.Contain("\"Anthropic\""),
            "the old Anthropic section is gone from appsettings once imported");
    }

    [Test]
    public void Deployed_configuration_wins_over_the_local_file()
    {
        var store = TestLamp.Store();
        store.Save(new AppSettings { AutoApprove = true });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{Prefix}:auto_approve"] = "false",
            [$"{Prefix}:claude_model"] = "claude-x",
        }).Build();
        var svc = new SettingsService(store, config, TestLamp.Keys());
        Assert.That(svc.Current.AutoApprove, Is.False);
        Assert.That(svc.Current.ClaudeModel, Is.EqualTo("claude-x"));
    }

    // ---- Keys ------------------------------------------------------------------------------

    private sealed class KeyedClaude : HttpMessageHandler
    {
        public List<string> Keys { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var key = request.Headers.TryGetValues("x-api-key", out var v) ? v.Single() : "";
            Keys.Add(key);
            return Task.FromResult(key == "sk-bad"
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("""{"error":{"type":"authentication_error"}}""", Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"content":[{"type":"text","text":"[]"}]}""", Encoding.UTF8, "application/json") });
        }
    }

    [Test]
    public async Task Claude_extraction_fails_over_to_the_second_key_and_flags_the_first()
    {
        var keys = TestLamp.KeyStore();
        keys.Add("claude", "first", "sk-bad");
        keys.Add("claude", "second", "sk-good");
        var health = new KeyHealthStore(TestLamp.NewDir("health"));
        var svc = new SettingsService(TestLamp.Store(), null, TestLamp.Keys(keys, health));
        var http = new KeyedClaude();
        var extractor = new ClaudeRateLawExtractor(new MindAttic.Legion.LegionClient(new HttpClient(http)),
            Options.Create(svc.Current.ToAnthropicOptions()), svc, NullLogger<ClaudeRateLawExtractor>.Instance);

        await extractor.ExtractAsync(new Jurisdiction { Id = 1, StateCode = "IL", JurisdictionName = "Illinois", JurisdictionType = JurisdictionType.State, FipsCode = "17" },
            "Rate: 6.25%", "text/plain", "https://test.gov");

        Assert.That(http.Keys.Distinct(), Is.EqualTo(new[] { "sk-bad", "sk-good" }));
        Assert.That(health.Get("claude", "first").Failed, Is.True);
    }

    [Test]
    public void Startup_import_moves_an_old_taxratecollector_scoped_key_into_the_shared_list_as_its_active_key()
    {
        var root = TestLamp.NewDir("import");
        NamedKeyStore Keys() => new(root, new KeyAssignmentStore(Path.Combine(root, KeyAssignmentStore.Bucket)));
#pragma warning disable CS0618 // the pre-LAMP per-app copy is exactly what this test recreates
        new AppScopedCredentialStore("taxratecollector", new LlmCredentialStore(Path.Combine(root, LlmCredentialStore.Bucket)))
            .SetKey("claude", "sk-trc-own");
#pragma warning restore CS0618
        Keys().Add("claude", "shared", "sk-shared");

        KeyImporter.ImportAppScopedFor(AppSettings.AppId, Keys());

        Assert.That(TestLamp.Keys(Keys()).GetActiveKey("claude", "TaxRateCollector")?.Value, Is.EqualTo("sk-trc-own"));
        foreach (var program in new[] { "TaxRateCollector.Blazor", "TaxRateCollector.Worker" })
            Assert.That(File.ReadAllText(Path.Combine(RepoRoot, program, "Program.cs")), Does.Contain("KeyImporter.ImportAppScopedFor(AppSettings.AppId)"), program);
    }

    [Test]
    public void No_per_app_key_store_or_key_field_is_left()
    {
        var offenders = new[] { "TaxRateCollector.Blazor", "TaxRateCollector.Infrastructure", "TaxRateCollector.Worker", "TaxRateCollector.Core" }
            .SelectMany(p => Directory.EnumerateFiles(Path.Combine(RepoRoot, p), "*.*", SearchOption.AllDirectories))
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".razor"))
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f) is var t && (t.Contains("AppScopedCredentialStore") || t.Contains("AnthropicApiKey")
                        || t.Contains("UspsApiKey") || t.Contains("sk-ant-") || t.Contains("USPS Web Tools API key") || t.Contains("VaultConfiguration =")))
            .Select(Path.GetFileName);
        Assert.That(offenders, Is.Empty);
    }

    // ---- Cog / old UI ------------------------------------------------------------------------

    private sealed class FakeLauncher : IVaultDesktopLauncher
    {
        public bool Running { get; init; } = true;
        public List<VaultOpenRequest> Requests { get; } = new();
        public bool TrySend(VaultOpenRequest request) { Requests.Add(request); return Running; }
        public bool TryStart(VaultOpenRequest request) => false;
    }

    private static T WithLauncher<T>(FakeLauncher fake, Func<T> body)
    {
        var original = VaultSettings.Launcher;
        VaultSettings.Launcher = fake;
        try { return body(); }
        finally { VaultSettings.Launcher = original; }
    }

    [Test]
    public void Settings_cog_opens_Vault_at_the_TaxRateCollector_tab_and_can_deep_link()
    {
        var fake = new FakeLauncher();
        Assert.That(WithLauncher(fake, () => SettingsCog.Open()).Opened, Is.True);
        WithLauncher(fake, () => SettingsCog.Open(SettingsCog.FullName("auto_approve")));
        Assert.That(fake.Requests.Select(r => r.AppId), Is.All.EqualTo("TaxRateCollector"));
        Assert.That(fake.Requests[1].Setting, Is.EqualTo($"{Prefix}:auto_approve"));
        Assert.That(SettingsCog.Link(), Is.EqualTo("mindattic-vault://open?app=TaxRateCollector"));
        Assert.That(WithLauncher(new FakeLauncher { Running = false }, () => SettingsCog.Open()).Message, Does.Contain("Vault link"));
    }

    [Test]
    public void Menu_and_setup_call_VaultSettings_and_the_old_settings_UI_is_gone()
    {
        var layout = File.ReadAllText(Path.Combine(RepoRoot, "TaxRateCollector.Blazor", "Components", "Layout", "MainLayout.razor"));
        Assert.That(layout, Does.Contain("SettingsCog.Open()").And.Contain("SettingsCog.Link()").And.Not.Contain("href=\"/settings\""));
        var setup = File.ReadAllText(Path.Combine(RepoRoot, "TaxRateCollector.Blazor", "Components", "Pages", "Setup.razor"));
        Assert.That(setup, Does.Contain("SettingsCog.Open(").And.Not.Contain("@bind=\"settings.Current").And.Not.Contain("SaveConfig"));
        Assert.That(File.Exists(Path.Combine(RepoRoot, "TaxRateCollector.Blazor", "Components", "Pages", "Settings.razor")), Is.False);
        Assert.That(typeof(SettingsService).GetMethod("Save"), Is.Null);
        Assert.That(typeof(SettingsService).GetMethod("SetTheme"), Is.Null);
    }
}
