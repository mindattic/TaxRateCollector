using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Infrastructure.Data;
using TaxRateCollector.Infrastructure.Seeding;

namespace TaxRateCollector.UnitTests.SetupTests;

/// <summary>
/// Tests for TaxCategorySeeder using an EF Core in-memory database.
/// Validates seeding logic without requiring SQL Server.
/// </summary>
[TestFixture]
public class TaxCategorySeederTests
{
    private static AppDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AppDbContext(opts);
    }

    [Test]
    public async Task Seed_PopulatesCategories_OnEmptyDb()
    {
        await using var db = CreateDb(nameof(Seed_PopulatesCategories_OnEmptyDb));
        await TaxCategorySeeder.SeedAsync(db);

        var count = await db.TaxCategories.CountAsync();
        Assert.That(count, Is.GreaterThan(0));
    }

    [Test]
    public async Task Seed_CategoryCount_MatchesSstTaxonomyDataDefinitionCount()
    {
        await using var db = CreateDb(nameof(Seed_CategoryCount_MatchesSstTaxonomyDataDefinitionCount));
        await TaxCategorySeeder.SeedAsync(db);

        var dbCount   = await db.TaxCategories.CountAsync();
        var defsCount = SstTaxonomyData.Definitions.Count;
        Assert.That(dbCount, Is.EqualTo(defsCount),
            $"Expected {defsCount} categories (one per SstTaxonomyData entry), got {dbCount}");
    }

    [Test]
    public async Task Seed_CreatesExactlyTwoRootCategories()
    {
        await using var db = CreateDb(nameof(Seed_CreatesExactlyTwoRootCategories));
        await TaxCategorySeeder.SeedAsync(db);

        var roots = await db.TaxCategories.Where(c => c.ParentId == null).ToListAsync();
        Assert.That(roots.Count, Is.EqualTo(2), "Expected exactly 2 roots: Goods and Services");
    }

    [Test]
    public async Task Seed_RootNames_AreGoodsAndServices()
    {
        await using var db = CreateDb(nameof(Seed_RootNames_AreGoodsAndServices));
        await TaxCategorySeeder.SeedAsync(db);

        var rootNames = await db.TaxCategories
            .Where(c => c.ParentId == null)
            .Select(c => c.Name)
            .ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(rootNames, Contains.Item("Goods"));
            Assert.That(rootNames, Contains.Item("Services"));
        });
    }

    [Test]
    public async Task Seed_AllLeafCategories_HaveAParent()
    {
        await using var db = CreateDb(nameof(Seed_AllLeafCategories_HaveAParent));
        await TaxCategorySeeder.SeedAsync(db);

        var orphanLeafCount = await db.TaxCategories.CountAsync(c => c.IsLeaf && c.ParentId == null);
        Assert.That(orphanLeafCount, Is.Zero, "All leaf categories must have a parent");
    }

    [Test]
    public async Task Seed_AllCategories_HaveGoodsOrServicesTopLevelType()
    {
        await using var db = CreateDb(nameof(Seed_AllCategories_HaveGoodsOrServicesTopLevelType));
        await TaxCategorySeeder.SeedAsync(db);

        var invalid = await db.TaxCategories
            .Where(c => c.TopLevelType != "Goods" && c.TopLevelType != "Services")
            .Select(c => c.Name)
            .ToListAsync();
        Assert.That(invalid, Is.Empty, "All categories must have TopLevelType 'Goods' or 'Services'");
    }

    [Test]
    public async Task Seed_AllCategories_HavePositiveSortOrder()
    {
        await using var db = CreateDb(nameof(Seed_AllCategories_HavePositiveSortOrder));
        await TaxCategorySeeder.SeedAsync(db);

        var badSort = await db.TaxCategories.Where(c => c.SortOrder <= 0).Select(c => c.Name).ToListAsync();
        Assert.That(badSort, Is.Empty, "All categories must have SortOrder > 0");
    }

    [Test]
    public async Task Seed_IsIdempotent_WhenCalledTwice()
    {
        await using var db = CreateDb(nameof(Seed_IsIdempotent_WhenCalledTwice));
        await TaxCategorySeeder.SeedAsync(db);
        var first = await db.TaxCategories.CountAsync();

        await TaxCategorySeeder.SeedAsync(db);
        var second = await db.TaxCategories.CountAsync();

        Assert.That(second, Is.EqualTo(first), "Seeding twice must not create duplicate categories");
    }

    [Test]
    public async Task Seed_AllCategories_HaveNonEmptyNames()
    {
        await using var db = CreateDb(nameof(Seed_AllCategories_HaveNonEmptyNames));
        await TaxCategorySeeder.SeedAsync(db);

        var unnamed = await db.TaxCategories.CountAsync(c => c.Name == "");
        Assert.That(unnamed, Is.Zero);
    }
}
