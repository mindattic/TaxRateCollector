using Microsoft.EntityFrameworkCore;
using Serilog.Core;
using Serilog.Events;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.Blazor.Logging;

/// <summary>
/// Serilog sink that writes log events to the LogEntries table via EF Core.
/// Uses a Func&lt;IServiceProvider?&gt; so it can be configured before the DI container
/// is built — the factory is resolved lazily on first write.
/// </summary>
public sealed class EfCoreSink : ILogEventSink
{
    private readonly Func<IServiceProvider?> providerFactory;
    private readonly IFormatProvider? formatProvider;

    /// <summary>
    /// The <paramref name="providerFactory"/> is invoked on every <see cref="Emit"/>
    /// call so logging can be wired before <c>WebApplication.Build()</c> completes;
    /// it returns null until the container is ready, in which case the event is dropped.
    /// </summary>
    public EfCoreSink(Func<IServiceProvider?> providerFactory, IFormatProvider? formatProvider = null)
    {
        this.providerFactory = providerFactory;
        this.formatProvider = formatProvider;
    }

    /// <summary>
    /// Persists a single log event to the LogEntries table. Failures (DI not yet
    /// ready, DB unavailable, etc.) are swallowed because a sink must never throw.
    /// </summary>
    public void Emit(LogEvent logEvent)
    {
        var provider = providerFactory();
        if (provider is null) return;

        try
        {
            var factory = provider.GetService(typeof(IDbContextFactory<AppDbContext>))
                          as IDbContextFactory<AppDbContext>;
            if (factory is null) return;

            using var db = factory.CreateDbContext();

            logEvent.Properties.TryGetValue("SourceContext", out var sourceCtx);

            var entry = new LogEntry
            {
                Timestamp = logEvent.Timestamp.UtcDateTime,
                Level = logEvent.Level.ToString(),
                Message = logEvent.RenderMessage(formatProvider),
                Exception = logEvent.Exception?.ToString(),
                SourceContext = sourceCtx?.ToString()?.Trim('"'),
                Properties = logEvent.Properties.Count > 0
                    ? System.Text.Json.JsonSerializer.Serialize(
                        logEvent.Properties.ToDictionary(k => k.Key, v => v.Value.ToString()))
                    : null
            };

            db.LogEntries.Add(entry);
            db.SaveChanges();
        }
        catch
        {
            // Never throw from a sink — silently drop on failure
        }
    }
}
