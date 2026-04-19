using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Data;
using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.UnitTests.ScraperTests;

[TestFixture]
public class RecursiveRateScraperTests
{
    // ── Factory helpers ───────────────────────────────────────────────────────

    private static IDbContextFactory<AppDbContext> CreateFactory(string? dbName = null)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            .Options;
        return new TestDbContextFactory(opts);
    }

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> opts)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(opts);
    }

    private static RecursiveRateScraper CreateScraper(
        IDbContextFactory<AppDbContext> db,
        IDiscoveryService? discovery = null,
        IRateLawExtractor? extractor = null,
        string httpResponse = "<html>rate page</html>")
    {
        var handler = new FixedHttpHandler(httpResponse);
        var http = new HttpClient(handler);
        return new RecursiveRateScraper(
            db,
            discovery ?? new AlwaysFoundDiscovery(),
            extractor ?? new StubRateLawExtractor(),
            http,
            NullLogger<RecursiveRateScraper>.Instance);
    }

    // ── Seed helpers ──────────────────────────────────────────────────────────

    private static async Task<(Jurisdiction state, ScrapeRun run)> SeedStateAsync(
        AppDbContext db, string stateCode = "IL")
    {
        var run = new ScrapeRun
        {
            StartedAt = DateTime.UtcNow.ToString("o"),
            Status = ScrapeStatus.Completed
        };
        db.ScrapeRuns.Add(run);

        var state = new Jurisdiction
        {
            StateCode = stateCode,
            JurisdictionName = stateCode + " State",
            FipsCode = "17",
            JurisdictionType = JurisdictionType.State,
            IsActive = true,
        };
        db.Jurisdictions.Add(state);
        await db.SaveChangesAsync();
        return (state, run);
    }

    private static async Task<Jurisdiction> SeedCountyAsync(
        AppDbContext db, int parentId, string stateCode = "IL", bool active = true)
    {
        var county = new Jurisdiction
        {
            StateCode = stateCode,
            JurisdictionName = "Test County",
            FipsCode = "17001",
            JurisdictionType = JurisdictionType.County,
            ParentId = parentId,
            IsActive = active,
        };
        db.Jurisdictions.Add(county);
        await db.SaveChangesAsync();
        return county;
    }

    private static async Task<Jurisdiction> SeedCityAsync(
        AppDbContext db, int parentId, string stateCode = "IL")
    {
        var city = new Jurisdiction
        {
            StateCode = stateCode,
            JurisdictionName = "Test City",
            FipsCode = "1700001",
            JurisdictionType = JurisdictionType.City,
            ParentId = parentId,
            IsActive = true,
        };
        db.Jurisdictions.Add(city);
        await db.SaveChangesAsync();
        return city;
    }

    private static ExtractedRateLaw MakeLaw(string name = "General Sales Tax", float confidence = 0.95f)
        => new(
            Name: name,
            Rate: 0.0625m,
            Basis: RateBasis.Percentage,
            Unit: "",
            SaleContext: SaleContext.Any,
            RemittancePoint: RemittancePoint.Retailer,
            MinAbv: null,
            MaxAbv: null,
            Conditions: "",
            StatutoryReference: "35 ILCS 120/2",
            EffectiveDate: "2024-01-01",
            ExpirationDate: "",
            TaxCategoryId: null,
            Confidence: confidence,
            RawEvidence: "Rate: 6.25%");

    // ── Tests: NeedsReview flag ───────────────────────────────────────────────

    [Test]
    public async Task NewRate_HasNeedsReview_True()
    {
        var factory = CreateFactory();
        await using var setup = factory.CreateDbContext();
        var (state, _) = await SeedStateAsync(setup);

        var scraper = CreateScraper(factory, extractor: new FixedExtractor([MakeLaw()]));
        await scraper.ScrapeAsync(state.Id, new RateScrapeOptions(), CancellationToken.None);

        await using var verify = factory.CreateDbContext();
        var rate = await verify.TaxRates.SingleAsync(t => t.Name == "General Sales Tax");
        Assert.That(rate.NeedsReview, Is.True);
    }

    [Test]
    public async Task NewRate_HasIsCurrent_False()
    {
        var factory = CreateFactory();
        await using var setup = factory.CreateDbContext();
        var (state, _) = await SeedStateAsync(setup);

        var scraper = CreateScraper(factory, extractor: new FixedExtractor([MakeLaw()]));
        await scraper.ScrapeAsync(state.Id, new RateScrapeOptions(), CancellationToken.None);

        await using var verify = factory.CreateDbContext();
        var rate = await verify.TaxRates.SingleAsync(t => t.Name == "General Sales Tax");
        Assert.That(rate.IsCurrent, Is.False);
    }

    // ── Tests: skip logic ─────────────────────────────────────────────────────

    [Test]
    public async Task SkippedDiscovery_DoesNotCreateRates()
    {
        var factory = CreateFactory();
        await using var setup = factory.CreateDbContext();
        var (state, _) = await SeedStateAsync(setup);

        var scraper = CreateScraper(factory,
            discovery: new SkippedDiscovery(),
            extractor: new FixedExtractor([MakeLaw()]));
        await scraper.ScrapeAsync(state.Id, new RateScrapeOptions(), CancellationToken.None);

        await using var verify = factory.CreateDbContext();
        Assert.That(await verify.TaxRates.AnyAsync(), Is.False);
    }

    [Test]
    public async Task DuplicatePending_IsSkipped_OnSecondScrape()
    {
        var factory = CreateFactory();
        await using var setup = factory.CreateDbContext();
        var (state, _) = await SeedStateAsync(setup);

        var scraper = CreateScraper(factory, extractor: new FixedExtractor([MakeLaw()]));
        await scraper.ScrapeAsync(state.Id, new RateScrapeOptions(), CancellationToken.None);
        await scraper.ScrapeAsync(state.Id, new RateScrapeOptions(), CancellationToken.None);

        await using var verify = factory.CreateDbContext();
        var count = await verify.TaxRates.CountAsync(t => t.Name == "General Sales Tax");
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public async Task ExistingLiveRate_IsSkipped_WithoutOverwrite()
    {
        var factory = CreateFactory();
        await using var setup = factory.CreateDbContext();
        var (state, run) = await SeedStateAsync(setup);

        // Existing live rate already approved
        setup.TaxRates.Add(new TaxRate
        {
            JurisdictionId = state.Id,
            ScrapeRunId = run.Id,
            Name = "General Sales Tax",
            Rate = 0.0625m,
            RateBasis = RateBasis.Percentage,
            IsCurrent = true,
            NeedsReview = false,
            ScrapedAt = DateTime.UtcNow.ToString("o"),
        });
        await setup.SaveChangesAsync();

        var scraper = CreateScraper(factory, extractor: new FixedExtractor([MakeLaw()]));
        var report = await scraper.ScrapeAsync(state.Id,
            new RateScrapeOptions(OverwriteExisting: false), CancellationToken.None);

        Assert.That(report.RateLawsCreated, Is.EqualTo(0));
    }

    [Test]
    public async Task ExistingLiveRate_AllowsNewPending_WithOverwrite()
    {
        var factory = CreateFactory();
        await using var setup = factory.CreateDbContext();
        var (state, run) = await SeedStateAsync(setup);

        setup.TaxRates.Add(new TaxRate
        {
            JurisdictionId = state.Id,
            ScrapeRunId = run.Id,
            Name = "General Sales Tax",
            Rate = 0.0625m,
            RateBasis = RateBasis.Percentage,
            IsCurrent = true,
            NeedsReview = false,
            ScrapedAt = DateTime.UtcNow.ToString("o"),
        });
        await setup.SaveChangesAsync();

        var scraper = CreateScraper(factory, extractor: new FixedExtractor([MakeLaw()]));
        var report = await scraper.ScrapeAsync(state.Id,
            new RateScrapeOptions(OverwriteExisting: true), CancellationToken.None);

        Assert.That(report.RateLawsCreated, Is.EqualTo(1));

        await using var verify = factory.CreateDbContext();
        // New pending rate created; original live rate untouched until approval
        var pending = await verify.TaxRates.SingleAsync(t => t.NeedsReview);
        Assert.That(pending.IsCurrent, Is.False);
    }

    // ── Tests: confidence filter ──────────────────────────────────────────────

    [Test]
    public async Task BelowMinConfidence_IsFiltered()
    {
        var factory = CreateFactory();
        await using var setup = factory.CreateDbContext();
        var (state, _) = await SeedStateAsync(setup);

        var scraper = CreateScraper(factory,
            extractor: new FixedExtractor([MakeLaw(confidence: 0.50f)]));
        var report = await scraper.ScrapeAsync(state.Id,
            new RateScrapeOptions(MinConfidence: 0.70f), CancellationToken.None);

        Assert.That(report.RateLawsCreated, Is.EqualTo(0));
    }

    [Test]
    public async Task AboveMinConfidence_IsIncluded()
    {
        var factory = CreateFactory();
        await using var setup = factory.CreateDbContext();
        var (state, _) = await SeedStateAsync(setup);

        var scraper = CreateScraper(factory,
            extractor: new FixedExtractor([MakeLaw(confidence: 0.95f)]));
        var report = await scraper.ScrapeAsync(state.Id,
            new RateScrapeOptions(MinConfidence: 0.70f), CancellationToken.None);

        Assert.That(report.RateLawsCreated, Is.EqualTo(1));
    }

    // ── Tests: tier options ───────────────────────────────────────────────────

    [Test]
    public async Task TierOption_SkipsCounties_WhenIncludeCountiesFalse()
    {
        var factory = CreateFactory();
        await using var setup = factory.CreateDbContext();
        var (state, _) = await SeedStateAsync(setup);
        await SeedCountyAsync(setup, state.Id);

        var extractor = new CountingExtractor();
        var scraper = CreateScraper(factory, extractor: extractor);
        await scraper.ScrapeAsync(state.Id,
            new RateScrapeOptions(IncludeCounties: false), CancellationToken.None);

        // Only the state itself should be processed
        Assert.That(extractor.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task TierOption_SkipsCities_WhenIncludeCitiesFalse()
    {
        var factory = CreateFactory();
        await using var setup = factory.CreateDbContext();
        var (state, _) = await SeedStateAsync(setup);
        var county = await SeedCountyAsync(setup, state.Id);
        await SeedCityAsync(setup, county.Id);

        var extractor = new CountingExtractor();
        var scraper = CreateScraper(factory, extractor: extractor);
        await scraper.ScrapeAsync(state.Id,
            new RateScrapeOptions(IncludeCities: false), CancellationToken.None);

        // State + county processed; city skipped
        Assert.That(extractor.CallCount, Is.EqualTo(2));
    }

    // ── Tests: progress tracking ──────────────────────────────────────────────

    [Test]
    public async Task SetsTotalCount_ToQueuedJurisdictionCount()
    {
        var factory = CreateFactory();
        await using var setup = factory.CreateDbContext();
        var (state, _) = await SeedStateAsync(setup);
        await SeedCountyAsync(setup, state.Id);

        var scraper = CreateScraper(factory);
        await scraper.ScrapeAsync(state.Id, new RateScrapeOptions(), CancellationToken.None);

        await using var verify = factory.CreateDbContext();
        var run = await verify.ScrapeRuns.OrderBy(r => r.Id).LastAsync();
        Assert.That(run.TotalCount, Is.EqualTo(2)); // state + county
    }

    [Test]
    public async Task ProcessedCount_EqualsJurisdictionsAtCompletion()
    {
        var factory = CreateFactory();
        await using var setup = factory.CreateDbContext();
        var (state, _) = await SeedStateAsync(setup);
        await SeedCountyAsync(setup, state.Id);

        var scraper = CreateScraper(factory);
        await scraper.ScrapeAsync(state.Id, new RateScrapeOptions(), CancellationToken.None);

        await using var verify = factory.CreateDbContext();
        var run = await verify.ScrapeRuns.OrderBy(r => r.Id).LastAsync();
        Assert.That(run.ProcessedCount, Is.EqualTo(run.TotalCount));
    }

    // ── Tests: report values ──────────────────────────────────────────────────

    [Test]
    public async Task Report_RateLawsFound_MatchesExtractorOutput()
    {
        var factory = CreateFactory();
        await using var setup = factory.CreateDbContext();
        var (state, _) = await SeedStateAsync(setup);

        var laws = new[] { MakeLaw("Beer Tax"), MakeLaw("Wine Tax") };
        var scraper = CreateScraper(factory, extractor: new FixedExtractor(laws));
        var report = await scraper.ScrapeAsync(state.Id, new RateScrapeOptions(), CancellationToken.None);

        Assert.That(report.RateLawsFound, Is.EqualTo(2));
        Assert.That(report.RateLawsCreated, Is.EqualTo(2));
    }

    [Test]
    public async Task ScrapeRun_Status_IsCompleted_OnSuccess()
    {
        var factory = CreateFactory();
        await using var setup = factory.CreateDbContext();
        var (state, _) = await SeedStateAsync(setup);

        var scraper = CreateScraper(factory);
        await scraper.ScrapeAsync(state.Id, new RateScrapeOptions(), CancellationToken.None);

        await using var verify = factory.CreateDbContext();
        var run = await verify.ScrapeRuns.OrderBy(r => r.Id).LastAsync();
        Assert.That(run.Status, Is.EqualTo(ScrapeStatus.Completed));
    }

    [Test]
    public async Task SourceDocument_IsCreated_ForEachExtractedLaw()
    {
        var factory = CreateFactory();
        await using var setup = factory.CreateDbContext();
        var (state, _) = await SeedStateAsync(setup);

        var scraper = CreateScraper(factory,
            extractor: new FixedExtractor([MakeLaw("Beer Excise")]));
        await scraper.ScrapeAsync(state.Id, new RateScrapeOptions(), CancellationToken.None);

        await using var verify = factory.CreateDbContext();
        var docs = await verify.SourceDocuments.ToListAsync();
        Assert.That(docs, Has.Count.EqualTo(1));
        Assert.That(docs[0].SourceUrl, Is.EqualTo("https://test.gov/rates"));
    }

    // ── Stub dependencies ─────────────────────────────────────────────────────

    private sealed class AlwaysFoundDiscovery : IDiscoveryService
    {
        public Task<DiscoveryResult> DiscoverAsync(Jurisdiction j, CancellationToken ct = default)
            => Task.FromResult(new DiscoveryResult
            {
                JurisdictionId   = j.Id,
                JurisdictionName = j.JurisdictionName,
                Status           = "Found",
                SourceUsed       = "https://test.gov/rates",
            });
    }

    private sealed class SkippedDiscovery : IDiscoveryService
    {
        public Task<DiscoveryResult> DiscoverAsync(Jurisdiction j, CancellationToken ct = default)
            => Task.FromResult(new DiscoveryResult
            {
                JurisdictionId = j.Id,
                Status         = "Skipped",
                SourceUsed     = "",
            });
    }

    private sealed class FixedExtractor(IReadOnlyList<ExtractedRateLaw> laws) : IRateLawExtractor
    {
        public Task<IReadOnlyList<ExtractedRateLaw>> ExtractAsync(
            Jurisdiction j, string content, string mimeType, string url, CancellationToken ct = default)
            => Task.FromResult(laws);
    }

    private sealed class CountingExtractor : IRateLawExtractor
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<ExtractedRateLaw>> ExtractAsync(
            Jurisdiction j, string content, string mimeType, string url, CancellationToken ct = default)
        {
            CallCount++;
            IReadOnlyList<ExtractedRateLaw> empty = [];
            return Task.FromResult(empty);
        }
    }

    private sealed class FixedHttpHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "text/html"),
            });
    }
}
