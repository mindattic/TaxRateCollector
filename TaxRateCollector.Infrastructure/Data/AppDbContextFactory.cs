using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaxRateCollector.Infrastructure.Data;

/// <summary>
/// Design-time factory for <c>dotnet ef</c>. Uses <c>ConnectionStrings__DefaultConnection</c> when set,
/// else the development database in Azure SQL, opened with the developer's own <c>az login</c>.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    /// <summary>The development database: no secret in the string, no local SQL Server.</summary>
    public const string DevelopmentConnectionString =
        "Server=tcp:sql-mindattic-f65744.database.windows.net,1433;Database=TaxRateCollector;" +
        "Authentication=Active Directory Default;Encrypt=True;Connect Timeout=60;MultipleActiveResultSets=True;";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connStr =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? DevelopmentConnectionString;

        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connStr, sql => sql.EnableRetryOnFailure())
            .Options;
        return new AppDbContext(opts);
    }
}
