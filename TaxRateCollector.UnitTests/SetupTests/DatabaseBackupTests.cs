using System.Diagnostics;

namespace TaxRateCollector.UnitTests.SetupTests;

/// <summary>
/// Tests for database backup utilities.
/// ParseConnectionString tests are pure unit tests.
/// SqlPackageAvailable is an integration test requiring a dev machine.
/// Run integration tests with: dotnet test --filter Category=Integration
/// </summary>
[TestFixture]
public class DatabaseBackupTests
{
    // Mirrors the inline parsing logic in Settings.razor CreateBackup().
    private static (string Server, string Database) ParseConnectionString(string connStr)
    {
        string server = "", database = "";
        foreach (var part in connStr.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv  = part.Split('=', 2);
            if (kv.Length < 2) continue;
            var key = kv[0].Trim();
            var val = kv[1].Trim();
            if (key.Equals("Server",         StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Data Source",    StringComparison.OrdinalIgnoreCase))
                server = val;
            else if (key.Equals("Database",       StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("Initial Catalog", StringComparison.OrdinalIgnoreCase))
                database = val;
        }
        return (server, database);
    }

    // ── Connection string parsing ─────────────────────────────────────────────

    [Test]
    public void Parse_LocalDbConnectionString_ExtractsServerAndDatabase()
    {
        var (server, db) = ParseConnectionString(
            @"Server=(localdb)\MSSQLLocalDB;Database=TaxRateCollector;Trusted_Connection=True;MultipleActiveResultSets=True;");
        Assert.Multiple(() =>
        {
            Assert.That(server,   Is.EqualTo(@"(localdb)\MSSQLLocalDB"));
            Assert.That(db,       Is.EqualTo("TaxRateCollector"));
        });
    }

    [Test]
    public void Parse_DataSourceKeyword_IsRecognised()
    {
        var (server, _) = ParseConnectionString("Data Source=myServer;Initial Catalog=myDb;");
        Assert.That(server, Is.EqualTo("myServer"));
    }

    [Test]
    public void Parse_InitialCatalogKeyword_IsRecognised()
    {
        var (_, db) = ParseConnectionString("Server=myServer;Initial Catalog=myDb;");
        Assert.That(db, Is.EqualTo("myDb"));
    }

    [Test]
    public void Parse_EmptyString_ReturnsEmptyServerAndDatabase()
    {
        var (server, db) = ParseConnectionString("");
        Assert.Multiple(() =>
        {
            Assert.That(server, Is.Empty);
            Assert.That(db,     Is.Empty);
        });
    }

    [Test]
    public void Parse_KeysAreCaseInsensitive()
    {
        var (server, db) = ParseConnectionString("SERVER=myServer;DATABASE=myDb;");
        Assert.Multiple(() =>
        {
            Assert.That(server, Is.EqualTo("myServer"));
            Assert.That(db,     Is.EqualTo("myDb"));
        });
    }

    [Test]
    public void Parse_TrailingSemicolon_IsHandled()
    {
        var (server, db) = ParseConnectionString("Server=s;Database=d;");
        Assert.Multiple(() =>
        {
            Assert.That(server, Is.EqualTo("s"));
            Assert.That(db,     Is.EqualTo("d"));
        });
    }

    [Test]
    public void Parse_TestConnectionString_IsValid()
    {
        var (server, db) = ParseConnectionString(TestDbConnection.ConnectionString);
        Assert.Multiple(() =>
        {
            Assert.That(server, Is.Not.Empty, "Connection string must specify a server");
            Assert.That(db,     Is.Not.Empty, "Connection string must specify a database");
        });
    }

    // ── sqlpackage availability (integration) ─────────────────────────────────

    [Test]
    [Category("Integration")]
    public void SqlPackage_IsAvailableOnPath()
    {
        using var proc = new Process();
        proc.StartInfo = new ProcessStartInfo
        {
            FileName               = "sqlpackage",
            Arguments              = "/version",
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        bool started;
        try   { started = proc.Start(); }
        catch { started = false; }

        Assert.That(started, Is.True,
            "sqlpackage must be installed and on PATH. Install with: dotnet tool install -g microsoft.sqlpackage");

        proc.WaitForExit(5000);
        Assert.That(proc.ExitCode, Is.EqualTo(0),
            "sqlpackage /version should exit 0");
    }
}
