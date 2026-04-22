using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.UnitTests.LoggingTests;

/// <summary>
/// Tests for the LogEntry entity: persistence, filtering by level and timestamp,
/// message search, and bulk deletion (used by the Logs page "Clear old logs" feature).
/// </summary>
[TestFixture]
public class LogEntryTests
{
    private static AppDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(opts);
    }

    private static LogEntry MakeEntry(string level, string message, DateTime? timestamp = null,
        string? sourceContext = null, string? exception = null) => new()
    {
        Timestamp = timestamp ?? DateTime.UtcNow,
        Level = level,
        Message = message,
        SourceContext = sourceContext,
        Exception = exception
    };

    // ── Persistence ───────────────────────────────────────────────────────────

    [Test]
    public async Task LogEntry_Persist_AllFieldsSaved()
    {
        await using var db = CreateDb();
        var ts = new DateTime(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc);

        db.LogEntries.Add(new LogEntry
        {
            Timestamp = ts,
            Level = "Information",
            Message = "Application started",
            SourceContext = "TaxRateCollector.Blazor.Program",
            Exception = null,
            Properties = """{"RequestPath":"/"}"""
        });
        await db.SaveChangesAsync();

        var loaded = await db.LogEntries.FirstAsync();
        Assert.That(loaded.Timestamp, Is.EqualTo(ts));
        Assert.That(loaded.Level, Is.EqualTo("Information"));
        Assert.That(loaded.Message, Is.EqualTo("Application started"));
        Assert.That(loaded.SourceContext, Is.EqualTo("TaxRateCollector.Blazor.Program"));
        Assert.That(loaded.Properties, Does.Contain("RequestPath"));
    }

    [Test]
    public async Task LogEntry_WithException_ExceptionFieldPopulated()
    {
        await using var db = CreateDb();

        db.LogEntries.Add(MakeEntry("Error", "Unhandled exception",
            exception: "System.InvalidOperationException: test\n  at Program.Main()"));
        await db.SaveChangesAsync();

        var loaded = await db.LogEntries.FirstAsync();
        Assert.That(loaded.Exception, Is.Not.Null);
        Assert.That(loaded.Exception, Does.Contain("InvalidOperationException"));
    }

    // ── Level filtering ───────────────────────────────────────────────────────

    [Test]
    public async Task FilterByLevel_Information_ReturnsOnlyInfoEntries()
    {
        await using var db = CreateDb();

        db.LogEntries.AddRange(
            MakeEntry("Information", "Info 1"),
            MakeEntry("Information", "Info 2"),
            MakeEntry("Warning",     "Warn 1"),
            MakeEntry("Error",       "Error 1")
        );
        await db.SaveChangesAsync();

        var infoEntries = await db.LogEntries
            .Where(l => l.Level == "Information")
            .ToListAsync();

        Assert.That(infoEntries, Has.Count.EqualTo(2));
        Assert.That(infoEntries.All(l => l.Level == "Information"), Is.True);
    }

    [Test]
    public async Task FilterByLevel_Error_ReturnsOnlyErrors()
    {
        await using var db = CreateDb();

        db.LogEntries.AddRange(
            MakeEntry("Information", "startup"),
            MakeEntry("Warning",     "slow query"),
            MakeEntry("Error",       "db failed"),
            MakeEntry("Fatal",       "crash")
        );
        await db.SaveChangesAsync();

        var errors = await db.LogEntries.Where(l => l.Level == "Error").ToListAsync();
        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(errors[0].Message, Is.EqualTo("db failed"));
    }

    [Test]
    public async Task FilterByLevel_All_ReturnsEveryEntry()
    {
        await using var db = CreateDb();

        foreach (var level in new[] { "Verbose", "Debug", "Information", "Warning", "Error", "Fatal" })
            db.LogEntries.Add(MakeEntry(level, $"{level} message"));

        await db.SaveChangesAsync();

        var all = await db.LogEntries.ToListAsync();
        Assert.That(all, Has.Count.EqualTo(6));
    }

    // ── Message search ────────────────────────────────────────────────────────

    [Test]
    public async Task SearchMessage_Keyword_ReturnsMatches()
    {
        await using var db = CreateDb();

        db.LogEntries.AddRange(
            MakeEntry("Information", "PayPal mock mode — returning fake order MOCK-abc"),
            MakeEntry("Information", "User logged in"),
            MakeEntry("Error",       "PayPal CaptureOrder failed for REAL-xyz"),
            MakeEntry("Warning",     "Slow scrape for Illinois")
        );
        await db.SaveChangesAsync();

        var paypalLogs = await db.LogEntries
            .Where(l => l.Message != null && l.Message.Contains("PayPal"))
            .ToListAsync();

        Assert.That(paypalLogs, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task SearchSourceContext_Keyword_ReturnsMatches()
    {
        await using var db = CreateDb();

        db.LogEntries.AddRange(
            MakeEntry("Information", "msg1", sourceContext: "TaxRateCollector.Infrastructure.Services.PayPalService"),
            MakeEntry("Information", "msg2", sourceContext: "TaxRateCollector.Blazor.Pages.Subscribe"),
            MakeEntry("Warning",     "msg3", sourceContext: "TaxRateCollector.Infrastructure.Services.PayPalService")
        );
        await db.SaveChangesAsync();

        var paypalLogs = await db.LogEntries
            .Where(l => l.SourceContext != null && l.SourceContext.Contains("PayPalService"))
            .ToListAsync();

        Assert.That(paypalLogs, Has.Count.EqualTo(2));
    }

    // ── Timestamp ordering ────────────────────────────────────────────────────

    [Test]
    public async Task OrderByTimestampDescending_MostRecentFirst()
    {
        await using var db = CreateDb();
        var now = DateTime.UtcNow;

        db.LogEntries.AddRange(
            MakeEntry("Information", "oldest",  now.AddHours(-3)),
            MakeEntry("Information", "middle",  now.AddHours(-1)),
            MakeEntry("Information", "newest",  now)
        );
        await db.SaveChangesAsync();

        var ordered = await db.LogEntries
            .OrderByDescending(l => l.Timestamp)
            .Select(l => l.Message)
            .ToListAsync();

        Assert.That(ordered[0], Is.EqualTo("newest"));
        Assert.That(ordered[1], Is.EqualTo("middle"));
        Assert.That(ordered[2], Is.EqualTo("oldest"));
    }

    // ── Pagination ────────────────────────────────────────────────────────────

    [Test]
    public async Task Pagination_PageSizeOf50_ReturnsCorrectSlice()
    {
        await using var db = CreateDb();
        var now = DateTime.UtcNow;

        for (var i = 0; i < 75; i++)
            db.LogEntries.Add(MakeEntry("Information", $"Message {i}", now.AddSeconds(-i)));

        await db.SaveChangesAsync();

        const int pageSize = 50;
        var page0 = await db.LogEntries
            .OrderByDescending(l => l.Timestamp)
            .Skip(0)
            .Take(pageSize)
            .ToListAsync();

        var page1 = await db.LogEntries
            .OrderByDescending(l => l.Timestamp)
            .Skip(pageSize)
            .Take(pageSize)
            .ToListAsync();

        Assert.That(page0, Has.Count.EqualTo(50));
        Assert.That(page1, Has.Count.EqualTo(25));
    }

    // ── Bulk deletion (Clear old logs) ────────────────────────────────────────

    [Test]
    public async Task ClearOldLogs_DeletesEntriesOlderThan7Days()
    {
        await using var db = CreateDb();
        var now = DateTime.UtcNow;
        var cutoff = now.AddDays(-7);

        db.LogEntries.AddRange(
            MakeEntry("Information", "recent 1", now.AddDays(-1)),
            MakeEntry("Information", "recent 2", now.AddDays(-3)),
            MakeEntry("Warning",     "old 1",    now.AddDays(-8)),
            MakeEntry("Error",       "old 2",    now.AddDays(-30)),
            MakeEntry("Fatal",       "old 3",    now.AddDays(-365))
        );
        await db.SaveChangesAsync();

        // Simulate the "Clear > 7 days" operation
        var toDelete = await db.LogEntries.Where(l => l.Timestamp < cutoff).ToListAsync();
        db.LogEntries.RemoveRange(toDelete);
        await db.SaveChangesAsync();

        var remaining = await db.LogEntries.ToListAsync();
        Assert.That(remaining, Has.Count.EqualTo(2));
        Assert.That(remaining.All(l => l.Timestamp >= cutoff), Is.True);
    }

    [Test]
    public async Task ClearOldLogs_NothingOld_DeletesNothing()
    {
        await using var db = CreateDb();
        var now = DateTime.UtcNow;

        db.LogEntries.AddRange(
            MakeEntry("Information", "recent 1", now.AddHours(-1)),
            MakeEntry("Information", "recent 2", now.AddDays(-2))
        );
        await db.SaveChangesAsync();

        var cutoff = now.AddDays(-7);
        var toDelete = await db.LogEntries.Where(l => l.Timestamp < cutoff).ToListAsync();
        db.LogEntries.RemoveRange(toDelete);
        await db.SaveChangesAsync();

        Assert.That(await db.LogEntries.CountAsync(), Is.EqualTo(2));
    }

    // ── LogEntry defaults ─────────────────────────────────────────────────────

    [Test]
    public void LogEntry_NewInstance_FieldsAreDefaultEmpty()
    {
        var entry = new LogEntry();
        Assert.That(entry.Level, Is.EqualTo(""));
        Assert.That(entry.Message, Is.EqualTo(""));
        Assert.That(entry.Exception, Is.Null);
        Assert.That(entry.Properties, Is.Null);
        Assert.That(entry.SourceContext, Is.Null);
    }

    // ── Level-based filtering in combination with search ─────────────────────

    [Test]
    public async Task FilterLevelAndSearch_CombinedQuery_ReturnsCorrectResults()
    {
        await using var db = CreateDb();

        db.LogEntries.AddRange(
            MakeEntry("Warning", "PayPal timeout", sourceContext: "PayPalService"),
            MakeEntry("Error",   "PayPal auth failed", sourceContext: "PayPalService"),
            MakeEntry("Warning", "Slow DB query", sourceContext: "AppDbContext"),
            MakeEntry("Information", "PayPal mock mode", sourceContext: "PayPalService")
        );
        await db.SaveChangesAsync();

        // Filter: level = Warning AND message contains "PayPal"
        var results = await db.LogEntries
            .Where(l => l.Level == "Warning" && l.Message != null && l.Message.Contains("PayPal"))
            .ToListAsync();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Message, Is.EqualTo("PayPal timeout"));
    }
}
