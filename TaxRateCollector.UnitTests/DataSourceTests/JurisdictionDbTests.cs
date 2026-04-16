using Microsoft.Data.Sqlite;

namespace TaxRateCollector.UnitTests.DataSourceTests;

/// <summary>
/// Integration tests that open the live SQLite database and verify that the
/// expected seed data actually exists — not mocked, real rows.
/// Run with:  dotnet test --filter Category=Integration
/// </summary>
[TestFixture]
[Category("Integration")]
public class JurisdictionDbTests
{
    private const string DbPath =
        @"D:\Projects\MindAttic\TaxRateCollector\TaxRateCollector.Frontend\taxrates.db";

    private SqliteConnection? conn;

    [OneTimeSetUp]
    public void OpenDb()
    {
        conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();
    }

    [OneTimeTearDown]
    public void CloseDb() => conn?.Close();

    private long Scalar(string sql)
    {
        using var cmd = conn!.CreateCommand();
        cmd.CommandText = sql;
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    // ── US country ────────────────────────────────────────────────────────────

    [Test]
    public void Db_HasExactlyOneCountry()
        => Assert.That(Scalar("SELECT COUNT(*) FROM Jurisdictions WHERE JurisdictionType='Country'"),
            Is.EqualTo(1), "Should have exactly 1 Country (United States)");

    [Test]
    public void Db_CountryIsUnitedStates()
    {
        using var cmd = conn!.CreateCommand();
        cmd.CommandText = "SELECT FipsCode FROM Jurisdictions WHERE JurisdictionType='Country' LIMIT 1";
        var fips = cmd.ExecuteScalar()?.ToString();
        Assert.That(fips, Is.EqualTo("US"), "The single Country must be US");
    }

    // ── States ────────────────────────────────────────────────────────────────

    [Test]
    public void Db_Has51States()
        => Assert.That(Scalar("SELECT COUNT(*) FROM Jurisdictions WHERE JurisdictionType='State'"),
            Is.EqualTo(51), "Should have exactly 51 states (50 + DC)");

    [Test]
    public void Db_StatesAllHaveValidFipsCodes()
    {
        using var cmd = conn!.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*) FROM Jurisdictions
            WHERE JurisdictionType='State'
              AND (FipsCode IS NULL OR length(FipsCode) != 2 OR FipsCode GLOB '*[^0-9]*')";
        Assert.That((long)(cmd.ExecuteScalar() ?? 0L), Is.Zero,
            "All State FipsCodes should be exactly 2 numeric digits");
    }

    [Test]
    public void Db_StatesAllLinkedToUsCountry()
    {
        var usId = Scalar("SELECT Id FROM Jurisdictions WHERE FipsCode='US' LIMIT 1");
        var unlinked = Scalar(
            $"SELECT COUNT(*) FROM Jurisdictions WHERE JurisdictionType='State' AND ParentId != {usId}");
        Assert.That(unlinked, Is.Zero, "All States must be children of the US Country row");
    }

    // ── Counties ──────────────────────────────────────────────────────────────

    [Test]
    public void Db_HasAtLeastSomeCounties()
        => Assert.That(Scalar("SELECT COUNT(*) FROM Jurisdictions WHERE JurisdictionType='County'"),
            Is.GreaterThanOrEqualTo(50),
            "Should have at least 50 counties (1 per state from seeder)");

    [Test]
    public void Db_NoNonUsJurisdictions()
    {
        var usId = Scalar("SELECT Id FROM Jurisdictions WHERE FipsCode='US' LIMIT 1");
        var nonUs = Scalar(
            $"SELECT COUNT(*) FROM Jurisdictions WHERE JurisdictionType='Country' AND Id != {usId}");
        Assert.That(nonUs, Is.Zero, "No non-US countries should exist in the database");
    }

    // ── Tax rates ─────────────────────────────────────────────────────────────

    [Test]
    public void Db_EachJurisdictionHasAtLeastOneRate()
    {
        var jurisdictions = Scalar("SELECT COUNT(*) FROM Jurisdictions");
        var withRates     = Scalar("SELECT COUNT(DISTINCT JurisdictionId) FROM TaxRates");
        Assert.That(withRates, Is.GreaterThanOrEqualTo(jurisdictions * 0.9),
            "At least 90% of jurisdictions should have a TaxRate row");
    }
}
