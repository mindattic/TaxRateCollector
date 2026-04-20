using System.Threading.Channels;

namespace TaxRateCollector.Infrastructure.Services;

/// <summary>
/// Singleton control plane for the scrape background worker.
/// The UI enqueues jobs and requests pauses through this class;
/// the worker reads from it and updates running state back into it.
/// </summary>
public sealed class ScrapeJobCoordinator
{
    // Capacity 1: a second Start/Resume while one is queued is silently dropped.
    private readonly Channel<ScrapeJob> _channel = Channel.CreateBounded<ScrapeJob>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    private CancellationTokenSource? _pauseCts;

    public bool IsRunning { get; private set; }

    public bool TryEnqueueStart()
        => _channel.Writer.TryWrite(new ScrapeJob(ScrapeJobType.StartFull));

    public bool TryEnqueueResume(int runId)
        => _channel.Writer.TryWrite(new ScrapeJob(ScrapeJobType.Resume, runId));

    public void RequestPause() => _pauseCts?.Cancel();

    internal ChannelReader<ScrapeJob> Reader => _channel.Reader;

    internal void SetRunning(bool running, CancellationTokenSource? pauseCts)
    {
        IsRunning = running;
        _pauseCts = pauseCts;
    }
}

public enum ScrapeJobType { StartFull, Resume }
public record ScrapeJob(ScrapeJobType Type, int? RunId = null);
