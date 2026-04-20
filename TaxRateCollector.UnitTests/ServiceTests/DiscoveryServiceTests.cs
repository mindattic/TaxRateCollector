using Microsoft.Extensions.Logging.Abstractions;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Infrastructure.Services;
using TaxRateCollector.UnitTests.Helpers;

namespace TaxRateCollector.UnitTests.ServiceTests;

[TestFixture]
public class DiscoveryServiceTests
{
    private const string LiveUrl    = "https://tax.example.gov/rates";
    private const string DeadUrl    = "https://dead.example.gov/rates";
    private const string ArchiveUrl = "https://web.archive.org/web/20230601000000/https://dead.example.gov/rates";

    private static string WaybackApiUrl(string url)
        => $"https://archive.org/wayback/available?url={Uri.EscapeDataString(url)}";

    private static string WaybackFoundJson(string snapshotUrl) =>
        $@"{{""url"":""..."",""archived_snapshots"":{{""closest"":{{""available"":true,""url"":""{snapshotUrl}"",""status"":""200"",""timestamp"":""20230601000000""}}}}}}";

    private const string WaybackNotFoundJson =
        @"{""url"":""..."",""archived_snapshots"":{}}";

    private static DiscoveryService MakeSvc(FakeHttpMessageHandler handler, bool waybackEnabled = false)
    {
        var settings = new SettingsService();
        settings.Current.WaybackMachineFallback = waybackEnabled;
        return new DiscoveryService(
            new HttpClient(handler),
            settings,
            NullLogger<DiscoveryService>.Instance);
    }

    private static Jurisdiction MakeJurisdiction(
        int id = 1,
        string name = "Test",
        string stateCode = "TX",
        JurisdictionType type = JurisdictionType.State,
        string sourceUrl = "")
        => new()
        {
            Id               = id,
            JurisdictionName = name,
            StateCode        = stateCode,
            JurisdictionType = type,
            SourceUrl        = sourceUrl,
        };

    // ── Skipped path (no SourceUrl) ────────────────────────────────────────────

    [Test]
    public async Task EmptySourceUrl_StatusIsSkipped()
    {
        var r = await MakeSvc(new FakeHttpMessageHandler()).DiscoverAsync(MakeJurisdiction(sourceUrl: ""));
        Assert.That(r.Status, Is.EqualTo("Skipped"));
    }

    [Test]
    public async Task WhitespaceSourceUrl_StatusIsSkipped()
    {
        var r = await MakeSvc(new FakeHttpMessageHandler()).DiscoverAsync(MakeJurisdiction(sourceUrl: "   "));
        Assert.That(r.Status, Is.EqualTo("Skipped"));
    }

    [Test]
    public async Task NoSourceUrl_SourceUsedIsDash()
    {
        var r = await MakeSvc(new FakeHttpMessageHandler()).DiscoverAsync(MakeJurisdiction(sourceUrl: ""));
        Assert.That(r.SourceUsed, Is.EqualTo("—"));
    }

    [Test]
    public async Task NoSourceUrl_NotesMentionsSourceUrl()
    {
        var r = await MakeSvc(new FakeHttpMessageHandler()).DiscoverAsync(MakeJurisdiction(sourceUrl: ""));
        Assert.That(r.Notes, Does.Contain("SourceUrl").IgnoreCase);
    }

    // ── Metadata propagation ──────────────────────────────────────────────────

    [Test]
    public async Task DiscoverAsync_SetsJurisdictionId()
    {
        var r = await MakeSvc(new FakeHttpMessageHandler()).DiscoverAsync(MakeJurisdiction(id: 42, sourceUrl: ""));
        Assert.That(r.JurisdictionId, Is.EqualTo(42));
    }

    [Test]
    public async Task DiscoverAsync_SetsJurisdictionName()
    {
        var r = await MakeSvc(new FakeHttpMessageHandler()).DiscoverAsync(MakeJurisdiction(name: "Los Angeles", sourceUrl: ""));
        Assert.That(r.JurisdictionName, Is.EqualTo("Los Angeles"));
    }

    [Test]
    public async Task DiscoverAsync_SetsStateCode()
    {
        var r = await MakeSvc(new FakeHttpMessageHandler()).DiscoverAsync(MakeJurisdiction(stateCode: "CA", sourceUrl: ""));
        Assert.That(r.StateCode, Is.EqualTo("CA"));
    }

    [Test]
    public async Task DiscoverAsync_SetsTier()
    {
        var r = await MakeSvc(new FakeHttpMessageHandler()).DiscoverAsync(MakeJurisdiction(type: JurisdictionType.County, sourceUrl: ""));
        Assert.That(r.Tier, Is.EqualTo(JurisdictionType.County));
    }

    [Test]
    public async Task DiscoverAsync_ProcessedAtIsRecent()
    {
        var before = DateTime.UtcNow.AddSeconds(-2);
        var r = await MakeSvc(new FakeHttpMessageHandler()).DiscoverAsync(MakeJurisdiction(sourceUrl: ""));
        Assert.That(r.ProcessedAt, Is.GreaterThan(before));
    }

    // ── Live URL (2xx) → Found ────────────────────────────────────────────────

    [Test]
    public async Task LiveUrl_Returns200_StatusIsFound()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(LiveUrl, "<html>rates</html>");
        var r = await MakeSvc(handler).DiscoverAsync(MakeJurisdiction(sourceUrl: LiveUrl));
        Assert.That(r.Status, Is.EqualTo("Found"));
    }

    [Test]
    public async Task LiveUrl_Returns200_SourceUsedIsTheUrl()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(LiveUrl, "<html>rates</html>");
        var r = await MakeSvc(handler).DiscoverAsync(MakeJurisdiction(sourceUrl: LiveUrl));
        Assert.That(r.SourceUsed, Is.EqualTo(LiveUrl));
    }

    // ── Dead URL, Wayback disabled → NotFound ─────────────────────────────────

    [Test]
    public async Task DeadUrl_WaybackDisabled_StatusIsNotFound()
    {
        var r = await MakeSvc(new FakeHttpMessageHandler(), waybackEnabled: false)
            .DiscoverAsync(MakeJurisdiction(sourceUrl: DeadUrl));
        Assert.That(r.Status, Is.EqualTo("NotFound"));
    }

    [Test]
    public async Task DeadUrl_WaybackDisabled_SourceUsedIsOriginalUrl()
    {
        var r = await MakeSvc(new FakeHttpMessageHandler(), waybackEnabled: false)
            .DiscoverAsync(MakeJurisdiction(sourceUrl: DeadUrl));
        Assert.That(r.SourceUsed, Is.EqualTo(DeadUrl));
    }

    [Test]
    public async Task DeadUrl_WaybackDisabled_NotesDoNotMentionArchive()
    {
        var r = await MakeSvc(new FakeHttpMessageHandler(), waybackEnabled: false)
            .DiscoverAsync(MakeJurisdiction(sourceUrl: DeadUrl));
        Assert.That(r.Notes, Does.Not.Contain("archive").IgnoreCase);
    }

    // ── Dead URL + Wayback enabled + snapshot found → WaybackFallback ────────

    [Test]
    public async Task DeadUrl_WaybackEnabled_SnapshotFound_StatusIsWaybackFallback()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(WaybackApiUrl(DeadUrl), WaybackFoundJson(ArchiveUrl), "application/json");
        var r = await MakeSvc(handler, waybackEnabled: true)
            .DiscoverAsync(MakeJurisdiction(sourceUrl: DeadUrl));
        Assert.That(r.Status, Is.EqualTo("WaybackFallback"));
    }

    [Test]
    public async Task DeadUrl_WaybackEnabled_SnapshotFound_SourceUsedIsArchiveUrl()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(WaybackApiUrl(DeadUrl), WaybackFoundJson(ArchiveUrl), "application/json");
        var r = await MakeSvc(handler, waybackEnabled: true)
            .DiscoverAsync(MakeJurisdiction(sourceUrl: DeadUrl));
        Assert.That(r.SourceUsed, Is.EqualTo(ArchiveUrl));
    }

    // ── Dead URL + Wayback enabled + no snapshot → NotFound ──────────────────

    [Test]
    public async Task DeadUrl_WaybackEnabled_NoSnapshot_StatusIsNotFound()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(WaybackApiUrl(DeadUrl), WaybackNotFoundJson, "application/json");
        var r = await MakeSvc(handler, waybackEnabled: true)
            .DiscoverAsync(MakeJurisdiction(sourceUrl: DeadUrl));
        Assert.That(r.Status, Is.EqualTo("NotFound"));
    }

    [Test]
    public async Task DeadUrl_WaybackEnabled_NoSnapshot_NotesMentionsArchive()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(WaybackApiUrl(DeadUrl), WaybackNotFoundJson, "application/json");
        var r = await MakeSvc(handler, waybackEnabled: true)
            .DiscoverAsync(MakeJurisdiction(sourceUrl: DeadUrl));
        Assert.That(r.Notes, Does.Contain("archive").IgnoreCase);
    }
}
