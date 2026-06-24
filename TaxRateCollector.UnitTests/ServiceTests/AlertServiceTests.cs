using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Infrastructure.Data;
using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.UnitTests.ServiceTests;

[TestFixture]
public class AlertServiceTests
{
    // SQLite in-memory (not the EF InMemory provider) so relational features the
    // service relies on — notably ExecuteUpdateAsync in AcknowledgeAllAsync — are
    // supported. The open connection is held by the factory to keep the
    // ":memory:" database alive for the lifetime of each test.
    private sealed class Factory(DbContextOptions<AppDbContext> opts, SqliteConnection conn)
        : IDbContextFactory<AppDbContext>
    {
        public SqliteConnection Connection { get; } = conn;
        public AppDbContext CreateDbContext() => new(opts);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(new AppDbContext(opts));
    }

    private static Factory MakeFactory()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .Options;
        using (var ctx = new AppDbContext(opts))
            ctx.Database.EnsureCreated();
        return new Factory(opts, conn);
    }

    private static async Task SeedChangeLogAsync(AppDbContext db,
        bool acknowledged1, bool acknowledged2)
    {
        var j = new Jurisdiction { JurisdictionName = "Test", FipsCode = "01", StateCode = "AL", JurisdictionType = JurisdictionType.State, IsActive = true };
        db.Jurisdictions.Add(j);
        await db.SaveChangesAsync();

        db.ChangeLog.Add(new ChangeLogEntry
        {
            JurisdictionId = j.Id, RateName = "Entry1",
            ChangeType = ChangeType.NewJurisdiction,
            DetectedAt = DateTime.UtcNow.ToString("o"),
            Acknowledged = acknowledged1,
        });
        db.ChangeLog.Add(new ChangeLogEntry
        {
            JurisdictionId = j.Id, RateName = "Entry2",
            ChangeType = ChangeType.RateChanged,
            DetectedAt = DateTime.UtcNow.ToString("o"),
            Acknowledged = acknowledged2,
        });
        await db.SaveChangesAsync();
    }

    [Test]
    public async Task GetUnacknowledgedAsync_ReturnsOnlyUnacknowledgedEntries()
    {
        var factory = MakeFactory();
        await using var db = factory.CreateDbContext();
        await SeedChangeLogAsync(db, acknowledged1: false, acknowledged2: true);

        var service = new AlertService(factory);
        var result = await service.GetUnacknowledgedAsync();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].RateName, Is.EqualTo("Entry1"));
    }

    [Test]
    public async Task GetUnacknowledgedAsync_ReturnsEmpty_WhenAllAcknowledged()
    {
        var factory = MakeFactory();
        await using var db = factory.CreateDbContext();
        await SeedChangeLogAsync(db, acknowledged1: true, acknowledged2: true);

        var service = new AlertService(factory);
        var result = await service.GetUnacknowledgedAsync();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task AcknowledgeAsync_MarksSpecificEntryAsAcknowledged()
    {
        var factory = MakeFactory();
        await using var db = factory.CreateDbContext();
        await SeedChangeLogAsync(db, acknowledged1: false, acknowledged2: false);

        var entry = await db.ChangeLog.FirstAsync(c => c.RateName == "Entry1");

        var service = new AlertService(factory);
        await service.AcknowledgeAsync(entry.Id);

        await using var verify = factory.CreateDbContext();
        var updated = await verify.ChangeLog.FindAsync(entry.Id);
        Assert.That(updated!.Acknowledged, Is.True);

        var other = await verify.ChangeLog.FirstAsync(c => c.RateName == "Entry2");
        Assert.That(other.Acknowledged, Is.False);
    }

    [Test]
    public async Task AcknowledgeAsync_WithNonExistentId_IsNoOp()
    {
        var factory = MakeFactory();
        await using var db = factory.CreateDbContext();
        await SeedChangeLogAsync(db, acknowledged1: false, acknowledged2: false);

        var service = new AlertService(factory);
        Assert.DoesNotThrowAsync(async () => await service.AcknowledgeAsync(99999));
    }

    [Test]
    public async Task AcknowledgeAllAsync_MarksAllEntriesAcknowledged()
    {
        var factory = MakeFactory();
        await using var db = factory.CreateDbContext();
        await SeedChangeLogAsync(db, acknowledged1: false, acknowledged2: false);

        var service = new AlertService(factory);
        await service.AcknowledgeAllAsync();

        await using var verify = factory.CreateDbContext();
        var unacked = await verify.ChangeLog.Where(c => !c.Acknowledged).CountAsync();
        Assert.That(unacked, Is.EqualTo(0));
    }

    [Test]
    public async Task AcknowledgeAllAsync_IsNoOp_WhenNoneExist()
    {
        var factory = MakeFactory();
        var service = new AlertService(factory);
        Assert.DoesNotThrowAsync(async () => await service.AcknowledgeAllAsync());
    }
}
