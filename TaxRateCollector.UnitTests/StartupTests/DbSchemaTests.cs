using Microsoft.Data.SqlClient;

namespace TaxRateCollector.UnitTests.StartupTests;

/// <summary>
/// Raw SQL Server schema checks against LocalDB.
/// Catches migration gaps, missing columns, and missing indexes.
/// Run with: dotnet test --filter Category=Integration
/// </summary>
[TestFixture]
[Category("Integration")]
public class DbSchemaTests
{
    private SqlConnection? conn;

    [OneTimeSetUp]
    public void OpenDb()
    {
        conn = new SqlConnection(TestDbConnection.ConnectionString);
        conn.Open();
    }

    [OneTimeTearDown]
    public void CloseDb() => conn?.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private long Scalar(string sql)
    {
        using var cmd = conn!.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
    }

    private HashSet<string> TableColumns(string table)
    {
        var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn!.CreateCommand();
        cmd.CommandText = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @t";
        cmd.Parameters.AddWithValue("@t", table);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            cols.Add(reader.GetString(0));
        return cols;
    }

    private bool MigrationApplied(string migrationId)
        => Scalar($"SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '{migrationId}'") > 0;

    // ── Migration history ─────────────────────────────────────────────────────

    [Test]
    public void AllExpectedMigrationsAreInHistory()
    {
        string[] expected =
        [
            "20260418044705_InitialCreate",
            "20260418050115_AddPerCategoryRateIndex",
        ];

        var missing = expected.Where(m => !MigrationApplied(m)).ToList();
        Assert.That(missing, Is.Empty,
            "Migrations absent from __EFMigrationsHistory:\n" + string.Join("\n", missing));
    }

    // ── TaxRates schema ───────────────────────────────────────────────────────

    [Test]
    public void TaxRates_HasTaxCategoryIdColumn()
    {
        var cols = TableColumns("TaxRates");
        Assert.That(cols, Contains.Item("TaxCategoryId"),
            "TaxRates.TaxCategoryId column is missing.");
    }

    [Test]
    public void TaxRates_HasAllBaselineColumns()
    {
        var cols = TableColumns("TaxRates");
        string[] required = ["Id", "JurisdictionId", "Rate", "RateType", "EffectiveDate",
                              "ScrapedAt", "ScrapeRunId", "RawValue", "IsCurrent", "TaxCategoryId"];
        var missing = required.Where(c => !cols.Contains(c)).ToList();
        Assert.That(missing, Is.Empty,
            $"TaxRates is missing columns: {string.Join(", ", missing)}");
    }

    // ── TaxCategories ─────────────────────────────────────────────────────────

    [Test]
    public void TaxCategories_TableExists()
    {
        var count = Scalar(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TaxCategories'");
        Assert.That(count, Is.EqualTo(1), "TaxCategories table must exist.");
    }

    // ── Index existence ───────────────────────────────────────────────────────

    [Test]
    public void TaxRates_HasPerCategoryUniqueIndex()
    {
        var count = Scalar(
            "SELECT COUNT(*) FROM sys.indexes " +
            "WHERE name = 'IX_TaxRates_JurisdictionId_TaxCategoryId_Current' " +
            "AND object_id = OBJECT_ID('TaxRates')");
        Assert.That(count, Is.EqualTo(1),
            "Filtered unique index IX_TaxRates_JurisdictionId_TaxCategoryId_Current is missing.");
    }

    // ── Jurisdictions schema ──────────────────────────────────────────────────

    [Test]
    public void Jurisdictions_HasAllBaselineColumns()
    {
        var cols = TableColumns("Jurisdictions");
        string[] required = ["Id", "ParentId", "JurisdictionType", "JurisdictionName",
                              "FipsCode", "StateCode", "IsActive"];
        var missing = required.Where(c => !cols.Contains(c)).ToList();
        Assert.That(missing, Is.Empty,
            $"Jurisdictions is missing columns: {string.Join(", ", missing)}");
    }
}
