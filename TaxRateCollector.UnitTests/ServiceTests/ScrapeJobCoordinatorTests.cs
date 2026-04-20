using System.Threading.Channels;
using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.UnitTests.ServiceTests;

[TestFixture]
public class ScrapeJobCoordinatorTests
{
    [Test]
    public void IsRunning_DefaultsToFalse()
    {
        var coordinator = new ScrapeJobCoordinator();
        Assert.That(coordinator.IsRunning, Is.False);
    }

    [Test]
    public void TryEnqueueStart_ReturnsTrue_WhenChannelEmpty()
    {
        var coordinator = new ScrapeJobCoordinator();
        Assert.That(coordinator.TryEnqueueStart(), Is.True);
    }

    [Test]
    public async Task TryEnqueueStart_DropsSecondWrite_WhenChannelFull()
    {
        // DropWrite mode: TryWrite returns true but the item is silently dropped.
        // Verify by consuming the first item and confirming no second item is available.
        var coordinator = new ScrapeJobCoordinator();
        coordinator.TryEnqueueStart();           // fill slot 1
        coordinator.TryEnqueueStart();           // DropWrite: second item is dropped

        await coordinator.Reader.ReadAsync();    // consume the one item in the channel
        Assert.That(coordinator.Reader.TryRead(out _), Is.False,
            "Channel should be empty after consuming the single item; second write was dropped.");
    }

    [Test]
    public void TryEnqueueResume_ReturnsTrue_WhenChannelEmpty()
    {
        var coordinator = new ScrapeJobCoordinator();
        Assert.That(coordinator.TryEnqueueResume(42), Is.True);
    }

    [Test]
    public async Task TryEnqueueResume_WritesResumeJob_WithCorrectRunId()
    {
        var coordinator = new ScrapeJobCoordinator();
        coordinator.TryEnqueueResume(99);

        var job = await coordinator.Reader.ReadAsync();
        Assert.That(job.Type, Is.EqualTo(ScrapeJobType.Resume));
        Assert.That(job.RunId, Is.EqualTo(99));
    }

    [Test]
    public async Task TryEnqueueStart_WritesStartFullJob()
    {
        var coordinator = new ScrapeJobCoordinator();
        coordinator.TryEnqueueStart();

        var job = await coordinator.Reader.ReadAsync();
        Assert.That(job.Type, Is.EqualTo(ScrapeJobType.StartFull));
    }

    [Test]
    public void RequestPause_IsNoOp_WhenNoCtsSet()
    {
        var coordinator = new ScrapeJobCoordinator();
        Assert.DoesNotThrow(() => coordinator.RequestPause());
    }

    [Test]
    public void RequestPause_CancelsCts_AfterSetRunning()
    {
        var coordinator = new ScrapeJobCoordinator();
        using var cts = new CancellationTokenSource();

        coordinator.SetRunning(running: true, pauseCts: cts);
        coordinator.RequestPause();

        Assert.That(cts.IsCancellationRequested, Is.True);
    }

    [Test]
    public void SetRunning_True_SetsIsRunning()
    {
        var coordinator = new ScrapeJobCoordinator();
        coordinator.SetRunning(running: true, pauseCts: null);
        Assert.That(coordinator.IsRunning, Is.True);
    }

    [Test]
    public void SetRunning_False_ClearsIsRunning()
    {
        var coordinator = new ScrapeJobCoordinator();
        coordinator.SetRunning(running: true, pauseCts: null);
        coordinator.SetRunning(running: false, pauseCts: null);
        Assert.That(coordinator.IsRunning, Is.False);
    }

    [Test]
    public void RequestPause_AfterSetRunningFalse_IsNoOp()
    {
        var coordinator = new ScrapeJobCoordinator();
        using var cts = new CancellationTokenSource();

        coordinator.SetRunning(running: true, pauseCts: cts);
        coordinator.SetRunning(running: false, pauseCts: null); // clear CTS
        // Should not throw even though CTS was replaced with null
        Assert.DoesNotThrow(() => coordinator.RequestPause());
    }
}
