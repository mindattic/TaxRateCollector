using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Infrastructure.Data;
using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.UnitTests.TaxCalcTests;

[TestFixture]
public class TaxCalculatorTests
{
    private static IDbContextFactory<AppDbContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContextFactory(opts);
    }

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> opts)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(opts);
    }

    [Test]
    public async Task Calculate_KnownJurisdiction_ReturnsCorrectAmount()
    {
        var factory = CreateFactory();
        await using var setup = factory.CreateDbContext();

        var run = new ScrapeRun { StartedAt = DateTime.UtcNow.ToString("o"), Status = ScrapeStatus.Completed };
        setup.ScrapeRuns.Add(run);

        var j = new Jurisdiction
        {
            StateCode = "IL", JurisdictionName = "Cook County",
            FipsCode = "17031", JurisdictionType = JurisdictionType.County
        };
        setup.Jurisdictions.Add(j);
        await setup.SaveChangesAsync();

        setup.TaxRates.Add(new TaxRate
        {
            JurisdictionId = j.Id,
            Rate = 0.1025m,
            Name = "General Sales Tax",
            RateBasis = RateBasis.Percentage,
            EffectiveDate = DateOnly.Parse("2024-01-01"),
            ScrapedAt = DateTime.UtcNow.ToString("o"),
            ScrapeRunId = run.Id,
            IsCurrent = true
        });
        await setup.SaveChangesAsync();

        var calc = new TaxCalculator(factory);
        var result = await calc.CalculateAsync(j.Id, 100m, 2);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.TotalTaxAmount, Is.EqualTo(20.50m));
        Assert.That(result.TotalPercentageRate, Is.EqualTo(0.1025m));
    }

    [Test]
    public async Task Calculate_UnknownJurisdiction_ReturnsNull()
    {
        var factory = CreateFactory();
        var calc = new TaxCalculator(factory);
        var result = await calc.CalculateAsync(9999, 100m, 1);
        Assert.That(result, Is.Null);
    }
}
