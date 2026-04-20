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
            "20260418155815_AddSourceDocumentOriginalFileName",
            "20260418161325_AddSourceDocumentFileExtension",
            "20260418200000_AddSubscribedCategories",
            "20260419164144_RefactorTaxRateLaws",
            "20260419170109_AddScrapeRunProgress",
            "20260419171915_AddTaxRateNeedsReview",
            "20260419180430_SchemaComplianceFixes",
            "20260419182631_RealWorldAccuracyFixes",
            "20260419184235_TaxCalculationAccuracy",
            "20260419200000_AddScrapeRunPauseResume",
            "20260420021229_AddJurisdictionUspsValidation",
            "20260420032321_AddChangeLogDescription",
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
        string[] required =
        [
            // Identity
            "Id", "JurisdictionId", "ScrapeRunId",
            // Classification
            "Name", "TaxType", "TaxCategoryId", "ProductCategory",
            // Rate value
            "Rate", "RateBasis", "Unit",
            // Applicability
            "SaleContext", "RemittancePoint", "IsIncludedInPrice", "IsCompound",
            "StatutoryReference", "Conditions",
            // ABV range
            "MinAbv", "MaxAbv",
            // Transaction bracket
            "MinTaxableAmount", "MaxTaxableAmount",
            // Per-unit cap
            "FlatCapPerUnit",
            // Cap eligibility
            "CountsTowardLocalCap",
            // Timing
            "EffectiveDate", "ExpirationDate",
            "IsTemporary", "IsRecurring",
            "AdjustmentFrequency", "AdjustmentMechanism",
            // Audit
            "ScrapedAt", "RawEvidence", "IsCurrent", "NeedsReview",
        ];
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
    public void TaxRates_HasPerCategoryIndex()
    {
        var count = Scalar(
            "SELECT COUNT(*) FROM sys.indexes " +
            "WHERE name = 'IX_TaxRates_JurisdictionId_CategoryId_Current' " +
            "AND object_id = OBJECT_ID('TaxRates')");
        Assert.That(count, Is.EqualTo(1),
            "Index IX_TaxRates_JurisdictionId_CategoryId_Current is missing.");
    }

    [Test]
    public void TaxRates_HasProductCategoryIndex()
    {
        var count = Scalar(
            "SELECT COUNT(*) FROM sys.indexes " +
            "WHERE name = 'IX_TaxRates_JurisdictionId_ProductCategory_TaxType_Current' " +
            "AND object_id = OBJECT_ID('TaxRates')");
        Assert.That(count, Is.EqualTo(1),
            "Index IX_TaxRates_JurisdictionId_ProductCategory_TaxType_Current is missing.");
    }

    // ── ScrapeRuns schema ─────────────────────────────────────────────────────

    [Test]
    public void ScrapeRuns_HasAllBaselineColumns()
    {
        var cols = TableColumns("ScrapeRuns");
        string[] required =
        [
            "Id", "StartedAt", "CompletedAt", "Status",
            "TotalScraped", "ChangesDetected", "ErrorCount",
            "TotalCount", "ProcessedCount", "LastProcessedJurisdictionId",
        ];
        var missing = required.Where(c => !cols.Contains(c)).ToList();
        Assert.That(missing, Is.Empty,
            $"ScrapeRuns is missing columns: {string.Join(", ", missing)}");
    }

    // ── ChangeLog schema ──────────────────────────────────────────────────────

    [Test]
    public void ChangeLog_TableExists()
    {
        var count = Scalar(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ChangeLog'");
        Assert.That(count, Is.EqualTo(1), "ChangeLog table must exist.");
    }

    [Test]
    public void ChangeLog_HasAllBaselineColumns()
    {
        var cols = TableColumns("ChangeLog");
        string[] required =
        [
            "Id", "JurisdictionId", "TaxRateId", "RateName",
            "ChangeType", "OldRate", "NewRate", "DetectedAt", "Acknowledged",
            "ChangeDescription",
        ];
        var missing = required.Where(c => !cols.Contains(c)).ToList();
        Assert.That(missing, Is.Empty,
            $"ChangeLog is missing columns: {string.Join(", ", missing)}");
    }

    // ── SourceDocuments schema ────────────────────────────────────────────────

    [Test]
    public void SourceDocuments_HasAllBaselineColumns()
    {
        var cols = TableColumns("SourceDocuments");
        string[] required =
        [
            "Id", "TaxRateId", "SourceUrl", "MimeType", "FetchedAt",
            "ContentHash", "FileName", "OriginalFileName", "EvidenceType",
            "RawContent", "IsActive",
        ];
        var missing = required.Where(c => !cols.Contains(c)).ToList();
        Assert.That(missing, Is.Empty,
            $"SourceDocuments is missing columns: {string.Join(", ", missing)}");
    }

    // ── Jurisdictions schema ──────────────────────────────────────────────────

    [Test]
    public void Jurisdictions_HasAllBaselineColumns()
    {
        var cols = TableColumns("Jurisdictions");
        string[] required =
        [
            "Id", "ParentId", "JurisdictionType", "JurisdictionName",
            "FipsCode", "StateCode", "IsActive", "IsHomeRuleAdministered",
            "UspsValidated", "UspsValidatedAt",
        ];
        var missing = required.Where(c => !cols.Contains(c)).ToList();
        Assert.That(missing, Is.Empty,
            $"Jurisdictions is missing columns: {string.Join(", ", missing)}");
    }

    // ── StateTaxProfiles schema ───────────────────────────────────────────────

    [Test]
    public void StateTaxProfiles_TableExists()
    {
        var count = Scalar(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'StateTaxProfiles'");
        Assert.That(count, Is.EqualTo(1), "StateTaxProfiles table must exist.");
    }

    [Test]
    public void StateTaxProfiles_HasAllBaselineColumns()
    {
        var cols = TableColumns("StateTaxProfiles");
        string[] required =
        [
            "Id", "StateCode", "StateName", "IsSstMember",
            "GeneralSalesTaxRate", "LocalTaxAuthorityType",
            "IntrastateSourcingRule", "HasLocalRateCap", "LocalRateCap",
            "EconomicNexusThresholdAmount", "EconomicNexusThresholdTransactions",
            "StateRevenueAgencyName", "StateRevenueUrl", "Notes", "UpdatedAt",
        ];
        var missing = required.Where(c => !cols.Contains(c)).ToList();
        Assert.That(missing, Is.Empty,
            $"StateTaxProfiles is missing columns: {string.Join(", ", missing)}");
    }

    // ── StateCategoryRules schema ─────────────────────────────────────────────

    [Test]
    public void StateCategoryRules_TableExists()
    {
        var count = Scalar(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'StateCategoryRules'");
        Assert.That(count, Is.EqualTo(1), "StateCategoryRules table must exist.");
    }

    [Test]
    public void StateCategoryRules_HasAllBaselineColumns()
    {
        var cols = TableColumns("StateCategoryRules");
        string[] required =
        [
            "Id", "StateTaxProfileId", "TaxCategoryId", "Taxability",
            "StateRate", "LocalRateApplies", "StatutoryReference",
            "SourceUrl", "Notes", "EffectiveDate",
        ];
        var missing = required.Where(c => !cols.Contains(c)).ToList();
        Assert.That(missing, Is.Empty,
            $"StateCategoryRules is missing columns: {string.Join(", ", missing)}");
    }

    // ── Subscriptions schema ──────────────────────────────────────────────────

    [Test]
    public void SubscribedCategories_TableExists()
    {
        var count = Scalar(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SubscribedCategories'");
        Assert.That(count, Is.EqualTo(1), "SubscribedCategories table must exist.");
    }
}
