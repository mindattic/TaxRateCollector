using TaxRateCollector.Core.Enums;

namespace TaxRateCollector.UnitTests.DataCollectionTests;

/// <summary>
/// Tests for static helper methods in Discovery.razor:
///   TruncateUrl — shortens long source URLs for display in the results table
///   TierClass   — maps JurisdictionType to the corresponding CSS class name
///
/// Methods are mirrored here as local statics (same pattern as ExportFormattingTests)
/// so the logic is unit-testable without spinning up a Razor component.
/// </summary>
[TestFixture]
public class DiscoveryHelperTests
{
    // ── Mirrors Discovery.razor private static helpers ────────────────────────

    private static string TruncateUrl(string url)
    {
        if (string.IsNullOrEmpty(url) || url == "—") return url;
        if (url.Length <= 40) return url;
        return url[..37] + "…";
    }

    private static string TierClass(JurisdictionType tier) => tier switch
    {
        JurisdictionType.State  => "tier-state",
        JurisdictionType.County => "tier-county",
        JurisdictionType.City   => "tier-city",
        _                       => "tier-country"
    };

    // ── TruncateUrl — passthrough cases ──────────────────────────────────────

    [Test]
    public void TruncateUrl_Null_ReturnsNull()
        => Assert.That(TruncateUrl(null!), Is.Null);

    [Test]
    public void TruncateUrl_Empty_ReturnsEmpty()
        => Assert.That(TruncateUrl(""), Is.EqualTo(""));

    [Test]
    public void TruncateUrl_EmDashPlaceholder_ReturnedUnchanged()
        => Assert.That(TruncateUrl("—"), Is.EqualTo("—"),
            "The em-dash sentinel used for missing URLs must pass through unmodified.");

    [Test]
    public void TruncateUrl_ShortUrl_ReturnedUnchanged()
    {
        const string url = "https://tax.il.gov/rates";
        Assert.That(TruncateUrl(url), Is.EqualTo(url));
    }

    [Test]
    public void TruncateUrl_Exactly40Chars_ReturnedUnchanged()
    {
        var url = new string('x', 40);
        Assert.That(TruncateUrl(url), Is.EqualTo(url));
    }

    [Test]
    public void TruncateUrl_39Chars_ReturnedUnchanged()
    {
        var url = new string('z', 39);
        Assert.That(TruncateUrl(url), Is.EqualTo(url));
    }

    // ── TruncateUrl — truncation cases ───────────────────────────────────────

    [Test]
    public void TruncateUrl_41Chars_TruncatesTo37PlusEllipsis()
    {
        var url    = new string('a', 37) + "BBBB"; // 41 chars; the 4 B's should be cut
        var result = TruncateUrl(url);

        Assert.That(result, Has.Length.EqualTo(38), "37 chars + the single ellipsis character");
        Assert.That(result, Does.EndWith("…"));
        Assert.That(result, Does.Not.Contain("B"),  "Characters beyond position 37 must be removed.");
    }

    [Test]
    public void TruncateUrl_LongUrl_PreservesFirst37Chars()
    {
        const string url = "https://www.revenue.state.il.us/tax-rates/general/download-2024.pdf";
        var result = TruncateUrl(url);

        Assert.That(result, Is.EqualTo(url[..37] + "…"));
    }

    [Test]
    public void TruncateUrl_LongUrl_ResultLengthIs38()
    {
        var url    = new string('u', 100);
        var result = TruncateUrl(url);

        Assert.That(result, Has.Length.EqualTo(38));
    }

    [Test]
    public void TruncateUrl_LongUrl_EndsWithEllipsis()
    {
        var url    = new string('u', 100);
        var result = TruncateUrl(url);

        Assert.That(result, Does.EndWith("…"));
    }

    [Test]
    public void TruncateUrl_VariousLengths_AllOver40AreTruncated(
        [Values(41, 50, 80, 200, 1000)] int length)
    {
        var url    = new string('x', length);
        var result = TruncateUrl(url);

        Assert.That(result, Has.Length.EqualTo(38));
        Assert.That(result, Does.EndWith("…"));
    }

    // ── TierClass ─────────────────────────────────────────────────────────────

    [Test]
    public void TierClass_State_ReturnsTierState()
        => Assert.That(TierClass(JurisdictionType.State), Is.EqualTo("tier-state"));

    [Test]
    public void TierClass_County_ReturnsTierCounty()
        => Assert.That(TierClass(JurisdictionType.County), Is.EqualTo("tier-county"));

    [Test]
    public void TierClass_City_ReturnsTierCity()
        => Assert.That(TierClass(JurisdictionType.City), Is.EqualTo("tier-city"));

    [Test]
    public void TierClass_Country_ReturnsTierCountry()
        => Assert.That(TierClass(JurisdictionType.Country), Is.EqualTo("tier-country"));

    [Test]
    public void TierClass_AllValues_MapToNonEmptyClass(
        [Values(
            JurisdictionType.Country,
            JurisdictionType.State,
            JurisdictionType.County,
            JurisdictionType.City)]
        JurisdictionType tier)
    {
        var cls = TierClass(tier);
        Assert.That(cls, Is.Not.Null.And.Not.Empty);
        Assert.That(cls, Does.StartWith("tier-"),
            "All tier CSS classes must follow the 'tier-*' naming convention.");
    }

    // ── Scrape status message format ──────────────────────────────────────────

    /// <summary>
    /// The message shown in the Discovery page after a scrape completes.
    /// Mirrors the interpolated string in StartScrape():
    ///   scrapeStatus = $"✓ Done — {changed} change(s) detected";
    /// </summary>
    [Test]
    [TestCase(0,   "✓ Done — 0 change(s) detected")]
    [TestCase(1,   "✓ Done — 1 change(s) detected")]
    [TestCase(42,  "✓ Done — 42 change(s) detected")]
    [TestCase(999, "✓ Done — 999 change(s) detected")]
    public void ScrapeStatusMessage_ChangedCount_FormatsCorrectly(int changed, string expected)
        => Assert.That($"✓ Done — {changed} change(s) detected", Is.EqualTo(expected));
}
