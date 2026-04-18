using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Infrastructure.Data;
using TaxRateCollector.Infrastructure.Seeding;

namespace TaxRateCollector.UnitTests.SetupTests;

/// <summary>
/// Integration tests that verify seed data is correctly populated in SQL Server.
/// Requires a running SQL Server LocalDB with an up-to-date schema.
/// Run with: dotnet test --filter Category=Integration
/// </summary>
[TestFixture]
[Category("Integration")]
public class DatabasePopulationIntegrationTests
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

    private long Scalar(string sql)
    {
        using var cmd = conn!.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
    }

    // ── TaxCategory seeding ───────────────────────────────────────────────────

    [Test]
    public void TaxCategories_ArePopulated()
        => Assert.That(Scalar("SELECT COUNT(*) FROM TaxCategories"), Is.GreaterThan(0),
            "TaxCategories must be populated by TaxCategorySeeder or SstTaxonomyImportService");

    [Test]
    public void TaxCategories_HasExactlyTwoRoots()
        => Assert.That(Scalar("SELECT COUNT(*) FROM TaxCategories WHERE ParentId IS NULL"),
            Is.EqualTo(2), "Expected exactly 2 root categories: Goods and Services");

    [Test]
    public void TaxCategories_GoodsRootExists()
    {
        using var cmd = conn!.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM TaxCategories WHERE Name = 'Goods' AND ParentId IS NULL";
        Assert.That(Convert.ToInt64(cmd.ExecuteScalar()), Is.EqualTo(1));
    }

    [Test]
    public void TaxCategories_ServicesRootExists()
    {
        using var cmd = conn!.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM TaxCategories WHERE Name = 'Services' AND ParentId IS NULL";
        Assert.That(Convert.ToInt64(cmd.ExecuteScalar()), Is.EqualTo(1));
    }

    [Test]
    public void TaxCategories_AllLeaves_HaveParent()
        => Assert.That(Scalar("SELECT COUNT(*) FROM TaxCategories WHERE IsLeaf = 1 AND ParentId IS NULL"),
            Is.Zero, "No leaf category should be a root node");

    [Test]
    public void TaxCategories_AllTopLevelTypes_AreGoodsOrServices()
        => Assert.That(Scalar("SELECT COUNT(*) FROM TaxCategories WHERE TopLevelType NOT IN ('Goods','Services')"),
            Is.Zero, "All categories must have TopLevelType 'Goods' or 'Services'");

    [Test]
    public void TaxCategories_AllHaveNonEmptyNames()
        => Assert.That(Scalar("SELECT COUNT(*) FROM TaxCategories WHERE Name = '' OR Name IS NULL"),
            Is.Zero);

    // ── StateTaxProfile seeding ───────────────────────────────────────────────

    [Test]
    public void StateTaxProfiles_ArePopulated()
        => Assert.That(Scalar("SELECT COUNT(*) FROM StateTaxProfiles"), Is.GreaterThan(0),
            "StateTaxProfiles must be populated by StateTaxProfileSeeder");

    [Test]
    public void StateTaxProfiles_Has51Profiles()
        => Assert.That(Scalar("SELECT COUNT(*) FROM StateTaxProfiles"),
            Is.EqualTo(51), "Expected 51 profiles (50 states + DC)");

    [Test]
    public void StateTaxProfiles_AllHaveStateCodes()
        => Assert.That(Scalar("SELECT COUNT(*) FROM StateTaxProfiles WHERE StateCode IS NULL OR LEN(StateCode) <> 2"),
            Is.Zero, "All profiles must have a 2-character state code");

    [Test]
    public void StateTaxProfiles_AllHaveNonNegativeRates()
        => Assert.That(Scalar("SELECT COUNT(*) FROM StateTaxProfiles WHERE GeneralSalesTaxRate < 0"),
            Is.Zero, "All state tax profiles must have a non-negative GeneralSalesTaxRate");

    [Test]
    public void StateTaxProfiles_AllHaveStateName()
        => Assert.That(Scalar("SELECT COUNT(*) FROM StateTaxProfiles WHERE StateName IS NULL OR StateName = ''"),
            Is.Zero, "All profiles must have a state name");

    // ── Jurisdiction seeding ──────────────────────────────────────────────────

    [Test]
    public void Jurisdictions_HasUsCountry()
    {
        using var cmd = conn!.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Jurisdictions WHERE JurisdictionType = 'Country' AND FipsCode = 'US'";
        Assert.That(Convert.ToInt64(cmd.ExecuteScalar()), Is.EqualTo(1), "Must have exactly one US country row");
    }

    [Test]
    public void Jurisdictions_Has51States()
        => Assert.That(Scalar("SELECT COUNT(*) FROM Jurisdictions WHERE JurisdictionType = 'State'"),
            Is.EqualTo(51), "Expected 51 states (50 + DC)");

    // ── PricingConfig / PayPalConfig seeding ──────────────────────────────────

    [Test]
    public void PricingConfig_HasAtLeastOneRow()
        => Assert.That(Scalar("SELECT COUNT(*) FROM PricingConfigs"), Is.GreaterThanOrEqualTo(1),
            "At least one PricingConfig row must exist");

    [Test]
    public void PricingConfig_PricePerState_IsPositive()
        => Assert.That(Scalar("SELECT COUNT(*) FROM PricingConfigs WHERE PricePerState <= 0"),
            Is.Zero, "PricePerState must be positive");

    [Test]
    public void PayPalConfig_HasAtLeastOneRow()
        => Assert.That(Scalar("SELECT COUNT(*) FROM PayPalConfigs"), Is.GreaterThanOrEqualTo(1),
            "At least one PayPalConfig row must exist");

    // ── TaxCategorySeeder idempotency (against real DB) ───────────────────────

    [Test]
    public async Task TaxCategorySeeder_IsIdempotent_AgainstRealDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(TestDbConnection.ConnectionString)
            .Options;
        await using var db = new AppDbContext(opts);

        var before = await db.TaxCategories.CountAsync();
        await TaxCategorySeeder.SeedAsync(db);
        var after = await db.TaxCategories.CountAsync();

        Assert.That(after, Is.EqualTo(before),
            "TaxCategorySeeder should not add rows when table is already populated");
    }

    // ── StateTaxProfileSeeder idempotency ────────────────────────────────────

    [Test]
    public async Task StateTaxProfileSeeder_IsIdempotent_AgainstRealDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(TestDbConnection.ConnectionString)
            .Options;
        await using var db = new AppDbContext(opts);

        var before = await db.StateTaxProfiles.CountAsync();
        await StateTaxProfileSeeder.SeedAsync(db);
        var after = await db.StateTaxProfiles.CountAsync();

        Assert.That(after, Is.EqualTo(before),
            "StateTaxProfileSeeder should not add rows when table is already populated");
    }
}
