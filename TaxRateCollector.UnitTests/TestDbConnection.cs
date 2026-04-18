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
}
