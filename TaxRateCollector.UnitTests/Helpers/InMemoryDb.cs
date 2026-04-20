using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.UnitTests.Helpers;

/// <summary>Convenience factory for isolated in-memory AppDbContext instances.</summary>
public static class InMemoryDb
{
    public static AppDbContext Create(string? name = null)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(opts);
    }

    public static IDbContextFactory<AppDbContext> CreateFactory(string? name = null)
        => new SingletonDbContextFactory(Create(name ?? Guid.NewGuid().ToString()));

    private sealed class SingletonDbContextFactory(AppDbContext db) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => db;
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(db);
    }
}
