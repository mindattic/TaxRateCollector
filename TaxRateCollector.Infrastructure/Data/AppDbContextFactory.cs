using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaxRateCollector.Infrastructure.Data;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=taxrates.db")
            .Options;
        return new AppDbContext(opts);
    }
}
