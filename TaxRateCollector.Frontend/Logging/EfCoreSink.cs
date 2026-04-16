using Microsoft.EntityFrameworkCore;
using Serilog.Core;
using Serilog.Events;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.Frontend.Logging;

/// <summary>
/// Serilog sink that writes log events to the LogEntries table via EF Core.
/// Uses a Func&lt;IServiceProvider?&gt; so it can be configured before the DI container
/// is built — the factory is resolved lazily on first write.
/// </summary>
public sealed class EfCoreSink : ILogEventSink
{
    private readonly Func<IServiceProvider?> _providerFactory;
    private readonly IFormatProvider? _formatProvider;

    public EfCoreSink(Func<IServiceProvider?> providerFactory, IFormatProvider? formatProvider = null)
    {
        _providerFactory = providerFactory;
        _formatProvider = formatProvider;
    }

    public void Emit(LogEvent logEvent)
    {
        var provider = _providerFactory();
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
                Message = logEvent.RenderMessage(_formatProvider),
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
