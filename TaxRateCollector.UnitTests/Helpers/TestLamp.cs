using MindAttic.Vault.Credentials;
using MindAttic.Vault.Paths;
using MindAttic.Vault.Settings;
using TaxRateCollector.Infrastructure.Services;

/// <summary>
/// Assembly-wide (global namespace, so it wraps every fixture): temp MindAttic Vault roots and a temp shared-LLM
/// keyring, so no test reads or writes the real %APPDATA% (settings.json, evidence, caches, keys) (LAMP, TRC-T29).
/// </summary>
[SetUpFixture]
public sealed class TestVaultRoots
{
    public static string Root { get; private set; } = "";

    [OneTimeSetUp]
    public void SetUp()
    {
        Root = Path.Combine(Path.GetTempPath(), "TrcVault-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(VaultPaths.RoamingRootEnvVar, Path.Combine(Root, "roaming"));
        Environment.SetEnvironmentVariable(VaultPaths.LocalRootEnvVar, Path.Combine(Root, "local"));
        Environment.SetEnvironmentVariable(LlmCredentialStore.DirectoryEnvVar, Path.Combine(Root, "llm"));
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(VaultPaths.RoamingRootEnvVar, null);
        Environment.SetEnvironmentVariable(VaultPaths.LocalRootEnvVar, null);
        Environment.SetEnvironmentVariable(LlmCredentialStore.DirectoryEnvVar, null);
        try { Directory.Delete(Root, recursive: true); } catch { }
    }
}

namespace TaxRateCollector.UnitTests.Helpers
{
    /// <summary>TaxRateCollector settings and keys over throwaway folders under <see cref="TestVaultRoots.Root"/>.</summary>
    public static class TestLamp
    {
        public static string NewDir(string kind) =>
            Directory.CreateDirectory(Path.Combine(TestVaultRoots.Root, kind + "-" + Guid.NewGuid().ToString("N"))).FullName;

        public static JsonSettingsStore<AppSettings> Store() => new(NewDir("settings"));

        public static NamedKeyStore KeyStore()
        {
            var root = NewDir("keys");
            return new NamedKeyStore(root, new KeyAssignmentStore(Path.Combine(root, KeyAssignmentStore.Bucket)));
        }

        public static KeySelector Keys(NamedKeyStore? store = null, KeyHealthStore? health = null) =>
            new(new FileKeySource(store ?? KeyStore()), health, classifier: SettingsService.ClassifyLegionFailure);

        /// <summary>A settings service over a temp store with one Claude key (none when blank) and optional USPS key.</summary>
        public static SettingsService Service(string? claudeKey = null, string? uspsKey = null, Action<AppSettings>? change = null)
        {
            var keys = KeyStore();
            if (!string.IsNullOrWhiteSpace(claudeKey)) keys.Add("claude", "key1", claudeKey);
            if (!string.IsNullOrWhiteSpace(uspsKey)) keys.Add("usps", "key1", uspsKey);
            var store = Store();
            if (change is not null) store.Update(change);
            return new SettingsService(store, null, Keys(keys));
        }
    }
}
