using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace TaxRateCollector.UnitTests;

internal static class TestDbConnection
{
    public static string ConnectionString { get; } = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("testsettings.json", optional: false)
        .Build()
        .GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("DefaultConnection not found in testsettings.json");

    /// <summary>
    /// Opens the shared development database, or skips the fixture when it is unreachable
    /// (serverless Azure SQL paused, out of free-tier credit, or offline) so the outage is not a test failure.
    /// </summary>
    public static SqlConnection OpenOrIgnore()
    {
        var conn = new SqlConnection(ConnectionString);
        try
        {
            conn.Open();
            return conn;
        }
        catch (SqlException ex)
        {
            conn.Dispose();
            Assert.Ignore($"Development database unavailable: {ex.Message}");
            throw;
        }
    }
}
