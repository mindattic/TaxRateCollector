using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.UnitTests.ServiceTests;

[TestFixture]
public class DiscoveryServiceTests
{
    private DiscoveryService svc = null!;

    [SetUp]
    public void Setup()
        => svc = new DiscoveryService(new HttpClient());

    // ── No SourceUrl — Skipped path ───────────────────────────────────────────

    [Test]
    public async Task DiscoverAsync_EmptySourceUrl_StatusIsSkipped()
    {
        var j = MakeJurisdiction(sourceUrl: "");
        var r = await svc.DiscoverAsync(j);
        Assert.That(r.Status, Is.EqualTo("Skipped"));
    }

    [Test]
    public async Task DiscoverAsync_WhitespaceSourceUrl_StatusIsSkipped()
    {
        var j = MakeJurisdiction(sourceUrl: "   ");
        var r = await svc.DiscoverAsync(j);
        Assert.That(r.Status, Is.EqualTo("Skipped"));
    }

    [Test]
    public async Task DiscoverAsync_NoSourceUrl_SourceUsedIsDash()
    {
        var j = MakeJurisdiction(sourceUrl: "");
        var r = await svc.DiscoverAsync(j);
        Assert.That(r.SourceUsed, Is.EqualTo("—"));
    }

    [Test]
    public async Task DiscoverAsync_NoSourceUrl_NotesMentionsAddingSourceUrl()
    {
        var j = MakeJurisdiction(sourceUrl: "");
        var r = await svc.DiscoverAsync(j);
        Assert.That(r.Notes, Does.Contain("SourceUrl").IgnoreCase);
    }

    // ── Jurisdiction fields propagated ────────────────────────────────────────

    [Test]
    public async Task DiscoverAsync_SetsJurisdictionId()
    {
        var j = MakeJurisdiction(id: 42, sourceUrl: "");
        var r = await svc.DiscoverAsync(j);
        Assert.That(r.JurisdictionId, Is.EqualTo(42));
    }

    [Test]
    public async Task DiscoverAsync_SetsJurisdictionName()
    {
        var j = MakeJurisdiction(name: "Los Angeles", sourceUrl: "");
        var r = await svc.DiscoverAsync(j);
        Assert.That(r.JurisdictionName, Is.EqualTo("Los Angeles"));
    }

    [Test]
    public async Task DiscoverAsync_SetsStateCode()
    {
        var j = MakeJurisdiction(stateCode: "CA", sourceUrl: "");
        var r = await svc.DiscoverAsync(j);
        Assert.That(r.StateCode, Is.EqualTo("CA"));
    }

    [Test]
    public async Task DiscoverAsync_SetsTier()
    {
        var j = MakeJurisdiction(type: JurisdictionType.County, sourceUrl: "");
        var r = await svc.DiscoverAsync(j);
        Assert.That(r.Tier, Is.EqualTo(JurisdictionType.County));
    }

    [Test]
    public async Task DiscoverAsync_ProcessedAtIsRecent()
    {
        var before = DateTime.UtcNow.AddSeconds(-2);
        var j = MakeJurisdiction(sourceUrl: "");
        var r = await svc.DiscoverAsync(j);
        Assert.That(r.ProcessedAt, Is.GreaterThan(before));
    }

    // ── Source type detection ─────────────────────────────────────────────────

    [Test]
    public async Task DiscoverAsync_PdfUrl_NotesContainsPdf()
    {
        var j = MakeJurisdiction(sourceUrl: "https://tax.state.gov/rates.pdf");
        var r = await svc.DiscoverAsync(j);
        Assert.That(r.Notes, Does.Contain("PDF"));
    }

    [Test]
    public async Task DiscoverAsync_ApiUrl_NotesContainsRestApi()
    {
        var j = MakeJurisdiction(sourceUrl: "https://data.state.gov/api/v1/rates");
        var r = await svc.DiscoverAsync(j);
        Assert.That(r.Notes, Does.Contain("REST API"));
    }

    [Test]
    public async Task DiscoverAsync_NewsUrl_NotesContainsNews()
    {
        var j = MakeJurisdiction(sourceUrl: "https://gov.il/news/tax-rate-updates");
        var r = await svc.DiscoverAsync(j);
        Assert.That(r.Notes, Does.Contain("News"));
    }

    [Test]
    public async Task DiscoverAsync_PressUrl_NotesContainsNews()
    {
        var j = MakeJurisdiction(sourceUrl: "https://gov.il/press/tax-2024");
        var r = await svc.DiscoverAsync(j);
        Assert.That(r.Notes, Does.Contain("News"));
    }

    [Test]
    public async Task DiscoverAsync_GenericUrl_NotesContainsWebPage()
    {
        var j = MakeJurisdiction(sourceUrl: "https://tax.il.gov/rates");
        var r = await svc.DiscoverAsync(j);
        Assert.That(r.Notes, Does.Contain("Web Page"));
    }

    [Test]
    public async Task DiscoverAsync_WithSourceUrl_StatusIsNotFound()
    {
        var j = MakeJurisdiction(sourceUrl: "https://tax.il.gov/rates");
        var r = await svc.DiscoverAsync(j);
        Assert.That(r.Status, Is.EqualTo("NotFound"));
    }

    [Test]
    public async Task DiscoverAsync_WithSourceUrl_SourceUsedIsTheUrl()
    {
        const string url = "https://tax.il.gov/rates";
        var j = MakeJurisdiction(sourceUrl: url);
        var r = await svc.DiscoverAsync(j);
        Assert.That(r.SourceUsed, Is.EqualTo(url));
    }

    // ── Cancellation ──────────────────────────────────────────────────────────

    [Test]
    public void DiscoverAsync_CancelledToken_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var j = MakeJurisdiction(sourceUrl: "");
        Assert.That(async () => await svc.DiscoverAsync(j, cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    // ── Helper ────────────────────────────────────────────────────────────────

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
}
