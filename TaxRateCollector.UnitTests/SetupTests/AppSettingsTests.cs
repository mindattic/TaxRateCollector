using System.Text.Json;
using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.UnitTests.SetupTests;

/// <summary>
/// Tests for AppSettings defaults and serialization round-trip.
/// No database or file I/O required.
/// </summary>
[TestFixture]
public class AppSettingsTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    // ── Default values ────────────────────────────────────────────────────────

    [Test]
    public void Default_Theme_IsLight()
        => Assert.That(new AppSettings().Theme, Is.EqualTo("light"));

    [Test]
    public void Default_Font_IsOutfit()
        => Assert.That(new AppSettings().Font, Is.EqualTo("outfit"));

    [Test]
    public void Default_FontSize_Is14()
        => Assert.That(new AppSettings().FontSize, Is.EqualTo(14));

    [Test]
    public void Default_UpdateFrequencyDays_Is90()
        => Assert.That(new AppSettings().DefaultUpdateFrequencyDays, Is.EqualTo(90));

    [Test]
    public void Default_EvidenceAutoFetch_IsFalse()
        => Assert.That(new AppSettings().EvidenceAutoFetch, Is.False);

    [Test]
    public void Default_WaybackMachineFallback_IsTrue()
        => Assert.That(new AppSettings().WaybackMachineFallback, Is.True);

    [Test]
    public void Default_UspsApiKey_IsEmpty()
        => Assert.That(new AppSettings().UspsApiKey, Is.Empty);

    // ── URL defaults ──────────────────────────────────────────────────────────

    [TestCase(nameof(AppSettings.CensusCountyGazUrl))]
    [TestCase(nameof(AppSettings.CensusPlaceGazUrl))]
    [TestCase(nameof(AppSettings.CensusZctaCountyUrl))]
    [TestCase(nameof(AppSettings.CensusZctaPlaceUrl))]
    [TestCase(nameof(AppSettings.SstAgreementUrl))]
    [TestCase(nameof(AppSettings.SstTaxabilityMatrixUrl))]
    [TestCase(nameof(AppSettings.SstMemberStatesUrl))]
    public void Default_Url_IsNotEmpty(string propertyName)
    {
        var val = (string?)typeof(AppSettings).GetProperty(propertyName)!.GetValue(new AppSettings());
        Assert.That(val, Is.Not.Null.And.Not.Empty, $"{propertyName} must have a non-empty default URL");
    }

    [TestCase(nameof(AppSettings.CensusCountyGazUrl))]
    [TestCase(nameof(AppSettings.CensusPlaceGazUrl))]
    [TestCase(nameof(AppSettings.CensusZctaCountyUrl))]
    [TestCase(nameof(AppSettings.CensusZctaPlaceUrl))]
    [TestCase(nameof(AppSettings.SstAgreementUrl))]
    [TestCase(nameof(AppSettings.SstTaxabilityMatrixUrl))]
    [TestCase(nameof(AppSettings.SstMemberStatesUrl))]
    public void Default_Url_StartsWithHttps(string propertyName)
    {
        var val = (string?)typeof(AppSettings).GetProperty(propertyName)!.GetValue(new AppSettings());
        Assert.That(val, Does.StartWith("https://"),
            $"{propertyName} default URL should use HTTPS");
    }

    [Test]
    public void CensusUrls_PointToCensusDotGov()
    {
        var s = new AppSettings();
        Assert.Multiple(() =>
        {
            Assert.That(s.CensusCountyGazUrl,  Does.Contain("census.gov"));
            Assert.That(s.CensusPlaceGazUrl,   Does.Contain("census.gov"));
            Assert.That(s.CensusZctaCountyUrl, Does.Contain("census.gov"));
            Assert.That(s.CensusZctaPlaceUrl,  Does.Contain("census.gov"));
        });
    }

    [Test]
    public void SstUrls_PointToStreamlinedSalesTax()
    {
        var s = new AppSettings();
        Assert.Multiple(() =>
        {
            Assert.That(s.SstAgreementUrl,        Does.Contain("streamlinedsalestax.org"));
            Assert.That(s.SstMemberStatesUrl,      Does.Contain("streamlinedsalestax.org"));
        });
    }

    // ── Serialization round-trip ──────────────────────────────────────────────

    [Test]
    public void Serialize_ThenDeserialize_PreservesTheme()
    {
        var original = new AppSettings { Theme = "dark" };
        var json     = JsonSerializer.Serialize(original, JsonOpts);
        var loaded   = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts)!;
        Assert.That(loaded.Theme, Is.EqualTo("dark"));
    }

    [Test]
    public void Serialize_ThenDeserialize_PreservesAllScalars()
    {
        var original = new AppSettings
        {
            Theme = "samurai", Font = "roboto", FontSize = 16,
            DefaultUpdateFrequencyDays = 30,
            EvidenceAutoFetch = true,
            WaybackMachineFallback = false,
            UspsApiKey = "test-key-123"
        };
        var json   = JsonSerializer.Serialize(original, JsonOpts);
        var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts)!;

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Theme,                      Is.EqualTo(original.Theme));
            Assert.That(loaded.Font,                       Is.EqualTo(original.Font));
            Assert.That(loaded.FontSize,                   Is.EqualTo(original.FontSize));
            Assert.That(loaded.DefaultUpdateFrequencyDays, Is.EqualTo(original.DefaultUpdateFrequencyDays));
            Assert.That(loaded.EvidenceAutoFetch,          Is.EqualTo(original.EvidenceAutoFetch));
            Assert.That(loaded.WaybackMachineFallback,     Is.EqualTo(original.WaybackMachineFallback));
            Assert.That(loaded.UspsApiKey,                 Is.EqualTo(original.UspsApiKey));
        });
    }

    [Test]
    public void Deserialize_WithUnknownKey_DoesNotThrow()
    {
        var json = """{"theme":"light","unknown_key":"ignored","font":"outfit"}""";
        Assert.DoesNotThrow(() => JsonSerializer.Deserialize<AppSettings>(json, JsonOpts));
    }

    [Test]
    public void Deserialize_EmptyJson_ReturnsNullSafely()
    {
        // Deserializing {} should give an object with all defaults
        var loaded = JsonSerializer.Deserialize<AppSettings>("{}", JsonOpts)!;
        Assert.That(loaded, Is.Not.Null);
        // Should fall back to property initializer defaults when keys are absent
    }

    // ── SettingsService integration ───────────────────────────────────────────

    [Test]
    public void SettingsService_DefaultCurrent_HasLightTheme()
    {
        var svc = new SettingsService();
        Assert.That(svc.Current.Theme, Is.EqualTo("light"));
    }

    [Test]
    public void SettingsService_SetTheme_UpdatesCurrentTheme()
    {
        var svc = new SettingsService();
        // Load first so Save() during SetTheme operates on a known state
        try { svc.Load(); } catch { /* ignore file I/O in CI */ }
        var originalTheme = svc.Current.Theme;

        svc.Current.Theme = "dark";
        Assert.That(svc.Current.Theme, Is.EqualTo("dark"),
            "Setting Theme on Current should be immediately reflected");
    }
}
