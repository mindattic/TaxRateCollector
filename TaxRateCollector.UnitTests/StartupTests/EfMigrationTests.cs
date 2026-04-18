using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.UnitTests.StartupTests;

/// <summary>
/// Verifies the EF Core migration state is consistent against SQL Server LocalDB.
/// Run with: dotnet test --filter Category=Integration
/// </summary>
[TestFixture]
[Category("Integration")]
public class EfMigrationTests
{
    private AppDbContext CreateDbContext()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(TestDbConnection.ConnectionString)
            .Options;
        return new AppDbContext(opts);
    }

    [Test]
    public async Task NoPendingMigrations()
    {
        await using var db = CreateDbContext();
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        Assert.That(pending, Is.Empty,
            $"Pending migrations: {string.Join(", ", pending)}. Run 'dotnet ef database update'.");
    }

    [Test]
    public async Task AllCodeMigrationsAreInHistory()
    {
        await using var db = CreateDbContext();
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToHashSet();
        var inCode  = db.Database.GetMigrations().ToList();
        var missing = inCode.Where(m => !applied.Contains(m)).ToList();
        Assert.That(missing, Is.Empty,
            $"Migrations in code but missing from __EFMigrationsHistory: {string.Join(", ", missing)}");
    }

    [Test]
    public async Task NoOrphanedMigrationHistoryEntries()
    {
        await using var db = CreateDbContext();
        var applied  = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        var inCode   = db.Database.GetMigrations().ToHashSet();
        var orphaned = applied.Where(m => !inCode.Contains(m)).ToList();
        Assert.That(orphaned, Is.Empty,
            $"__EFMigrationsHistory has entries with no matching class: {string.Join(", ", orphaned)}");
    }

    [Test]
    public async Task EfModelColumnsMatchActualDbSchema()
    {
        await using var db = CreateDbContext();

        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();

        // Build actual column map from INFORMATION_SCHEMA
        var actualColumns = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var table = reader.GetString(0);
                var col   = reader.GetString(1);
                if (!actualColumns.TryGetValue(table, out var set))
                    actualColumns[table] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                set.Add(col);
            }
        }

        var mismatches = new List<string>();
        foreach (var entityType in db.Model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (tableName is null) continue;

            if (!actualColumns.TryGetValue(tableName, out var dbCols))
            {
                mismatches.Add($"Table '{tableName}' expected by EF model does not exist in DB");
                continue;
            }

            foreach (var prop in entityType.GetProperties())
            {
                var storeId = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(tableName);
                var colName = prop.GetColumnName(storeId);
                if (colName is null) continue;
                if (!dbCols.Contains(colName))
                    mismatches.Add($"{tableName}.{colName} (EF '{prop.Name}') is missing from DB");
            }
        }

        Assert.That(mismatches, Is.Empty,
            "EF model expects columns missing from DB:\n" + string.Join("\n", mismatches));
    }
}
