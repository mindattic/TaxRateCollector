using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.UnitTests.SubscriptionTests;

/// <summary>
/// Tests for the cross-feature interaction between the subscription system and
/// the jurisdiction tax-rate data: the subscriber's billing state is used to
/// look up the sales tax rate on their own subscription.
/// </summary>
[TestFixture]
public class SubscriptionTaxLookupTests
{
    private static AppDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(opts);
    }

    private static async Task<Jurisdiction> SeedStateAsync(AppDbContext db, string stateCode, string name, string fips)
    {
        var country = await db.Jurisdictions
            .FirstOrDefaultAsync(j => j.JurisdictionType == JurisdictionType.Country);
        if (country is null)
        {
            country = new Jurisdiction
            {
                JurisdictionType = JurisdictionType.Country,
                JurisdictionName = "United States",
                StateCode = "US",
                FipsCode = "US-lookup",
                SourceUrl = ""
            };
            db.Jurisdictions.Add(country);
            await db.SaveChangesAsync();
        }

        var state = new Jurisdiction
        {
            JurisdictionType = JurisdictionType.State,
            JurisdictionName = name,
            StateCode = stateCode,
            FipsCode = fips,
            ParentId = country.Id,
            SourceUrl = ""
        };
        db.Jurisdictions.Add(state);
        await db.SaveChangesAsync();
        return state;
    }

    private static async Task AddRateAsync(AppDbContext db, int jurisdictionId, decimal rate)
    {
        var run = new ScrapeRun { StartedAt = DateTime.UtcNow.ToString("o"), Status = ScrapeStatus.Manual };
        db.ScrapeRuns.Add(run);
        await db.SaveChangesAsync();

        db.TaxRates.Add(new TaxRate
        {
            JurisdictionId = jurisdictionId,
            Rate = rate,
            Name = "General Sales Tax", RateBasis = RateBasis.Percentage,
            EffectiveDate = DateOnly.Parse("2024-01-01"),
            ScrapedAt = DateTime.UtcNow.ToString("o"),
            ScrapeRunId = run.Id,
            RawEvidence = $"{rate * 100:F3}%",
            IsCurrent = true
        });
        await db.SaveChangesAsync();
    }

    // ── Core cross-feature: billing state tax lookup ──────────────────────────

    [Test]
    public async Task TaxLookup_BillingStateIL_Returns6_25Percent()
    {
        await using var db = CreateDb();
        var il = await SeedStateAsync(db, "IL", "Illinois", "17-lookup");
        await AddRateAsync(db, il.Id, 0.0625m);

        // Simulate the OnBillingStateChange lookup in Subscribe.razor
        var jurisdiction = await db.Jurisdictions
            .Where(j => j.StateCode == "IL" && j.JurisdictionType == JurisdictionType.State)
            .FirstOrDefaultAsync();
        Assert.That(jurisdiction, Is.Not.Null);

        var taxRate = await db.TaxRates
            .Where(r => r.JurisdictionId == jurisdiction!.Id && r.IsCurrent)
            .Select(r => r.Rate)
            .FirstOrDefaultAsync();

        Assert.That(taxRate, Is.EqualTo(0.0625m));
    }

    [Test]
    public async Task TaxLookup_BillingStateNoTax_ReturnsZero()
    {
        await using var db = CreateDb();
        var or = await SeedStateAsync(db, "OR", "Oregon", "41-lookup");
        await AddRateAsync(db, or.Id, 0.00m); // Oregon has no sales tax

        var jurisdiction = await db.Jurisdictions
            .Where(j => j.StateCode == "OR" && j.JurisdictionType == JurisdictionType.State)
            .FirstOrDefaultAsync();

        var taxRate = await db.TaxRates
            .Where(r => r.JurisdictionId == jurisdiction!.Id && r.IsCurrent)
            .Select(r => r.Rate)
            .FirstOrDefaultAsync();

        Assert.That(taxRate, Is.EqualTo(0m));
    }

    [Test]
    public async Task TaxLookup_StateNotInDB_ReturnsZeroDefault()
    {
        await using var db = CreateDb();
        // No rates in DB at all

        var taxRate = await db.TaxRates
            .Where(r => r.JurisdictionId == -1 && r.IsCurrent)
            .Select(r => (decimal?)r.Rate)
            .FirstOrDefaultAsync();

        // EF FirstOrDefault on decimal returns 0 (default); nullable returns null
        Assert.That(taxRate ?? 0m, Is.EqualTo(0m));
    }

    [Test]
    public async Task TaxLookup_OnlyCurrentRateUsed_IgnoresRetiredRate()
    {
        await using var db = CreateDb();
        var ca = await SeedStateAsync(db, "CA", "California", "06-lookup");

        var run = new ScrapeRun { StartedAt = DateTime.UtcNow.ToString("o"), Status = ScrapeStatus.Manual };
        db.ScrapeRuns.Add(run);
        await db.SaveChangesAsync();

        // Old rate (retired)
        db.TaxRates.Add(new TaxRate
        {
            JurisdictionId = ca.Id,
            Rate = 0.0725m,
            Name = "General Sales Tax", RateBasis = RateBasis.Percentage,
            EffectiveDate = DateOnly.Parse("2020-01-01"),
            ScrapedAt = DateTime.UtcNow.ToString("o"),
            ScrapeRunId = run.Id,
            IsCurrent = false
        });
        // Current rate
        db.TaxRates.Add(new TaxRate
        {
            JurisdictionId = ca.Id,
            Rate = 0.0725m,
            Name = "General Sales Tax", RateBasis = RateBasis.Percentage,
            EffectiveDate = DateOnly.Parse("2024-01-01"),
            ScrapedAt = DateTime.UtcNow.ToString("o"),
            ScrapeRunId = run.Id,
            IsCurrent = true
        });
        await db.SaveChangesAsync();

        var currentRates = await db.TaxRates
            .Where(r => r.JurisdictionId == ca.Id && r.IsCurrent)
            .ToListAsync();

        Assert.That(currentRates, Has.Count.EqualTo(1));
        Assert.That(currentRates[0].IsCurrent, Is.True);
    }

    // ── Full flow: create subscriber, look up tax, build billing record ───────

    [Test]
    public async Task FullFlow_CreateSubscriberWithTaxAndStates_AllConsistent()
    {
        await using var db = CreateDb();

        // Seed Illinois state with 6.25% rate
        var il = await SeedStateAsync(db, "IL", "Illinois", "17-full");
        await AddRateAsync(db, il.Id, 0.0625m);

        // Look up tax rate (mirrors OnBillingStateChange)
        var jurisdiction = await db.Jurisdictions
            .FirstAsync(j => j.StateCode == "IL" && j.JurisdictionType == JurisdictionType.State);
        var billingTaxRate = await db.TaxRates
            .Where(r => r.JurisdictionId == jurisdiction.Id && r.IsCurrent)
            .Select(r => r.Rate)
            .FirstOrDefaultAsync();

        // Create subscriber
        var subscriber = new Subscriber
        {
            UserId = "user-full-flow",
            FullName = "Test User",
            AddressLine1 = "100 Test Ave",
            City = "Chicago",
            StateCode = "IL",
            ZipCode = "60601",
            IsActive = true,
            CreatedAt = DateTime.UtcNow.ToString("o")
        };
        db.Subscribers.Add(subscriber);
        await db.SaveChangesAsync();

        // Pricing
        var pricing = new PricingConfig { Id = 1, PricePerState = 0.01m, Currency = "USD", UpdatedAt = DateTime.UtcNow.ToString("o") };
        db.PricingConfigs.Add(pricing);
        await db.SaveChangesAsync();

        // Selected states: CA, TX (2 states)
        var selectedStates = new[] { "CA", "TX" };
        var subtotal = selectedStates.Length * pricing.PricePerState; // 0.02
        var taxAmount = subtotal * billingTaxRate;                     // 0.02 × 0.0625 = 0.00125
        var total = subtotal + taxAmount;                              // 0.02125

        var billing = new BillingRecord
        {
            SubscriberId = subscriber.Id,
            StateCount = selectedStates.Length,
            PricePerState = pricing.PricePerState,
            Subtotal = subtotal,
            BillingStateCode = "IL",
            TaxRate = billingTaxRate,
            TaxAmount = taxAmount,
            Total = total,
            Currency = pricing.Currency,
            PayPalOrderId = "MOCK-testorder",
            Status = Core.Enums.BillingStatus.Completed,
            CreatedAt = DateTime.UtcNow.ToString("o")
        };
        db.BillingRecords.Add(billing);
        await db.SaveChangesAsync();

        // Add subscribed states
        foreach (var code in selectedStates)
        {
            db.SubscribedStates.Add(new SubscribedState
            {
                SubscriberId = subscriber.Id,
                StateCode = code,
                StateName = code,
                IsActive = true,
                StartDate = DateTime.UtcNow.ToString("o")
            });
        }
        await db.SaveChangesAsync();

        // Assertions
        Assert.That(billing.Subtotal, Is.EqualTo(0.02m));
        Assert.That(billing.TaxRate, Is.EqualTo(0.0625m));
        Assert.That(billing.TaxAmount, Is.EqualTo(0.00125m));
        Assert.That(billing.Total, Is.EqualTo(0.02125m));
        Assert.That(billing.Status, Is.EqualTo(Core.Enums.BillingStatus.Completed));

        var activeStates = await db.SubscribedStates
            .Where(ss => ss.SubscriberId == subscriber.Id && ss.IsActive)
            .CountAsync();
        Assert.That(activeStates, Is.EqualTo(2));

        var loadedBilling = await db.BillingRecords
            .FirstAsync(b => b.SubscriberId == subscriber.Id);
        Assert.That(loadedBilling.StateCount, Is.EqualTo(2));
        Assert.That(loadedBilling.BillingStateCode, Is.EqualTo("IL"));
    }

    // ── Multiple subscribers, isolated data ───────────────────────────────────

    [Test]
    public async Task MultipleSubscribers_DataIsIsolated()
    {
        await using var db = CreateDb();

        var sub1 = new Subscriber { UserId = "user-1", CreatedAt = DateTime.UtcNow.ToString("o") };
        var sub2 = new Subscriber { UserId = "user-2", CreatedAt = DateTime.UtcNow.ToString("o") };
        db.Subscribers.AddRange(sub1, sub2);
        await db.SaveChangesAsync();

        // sub1 subscribes to CA, TX
        db.SubscribedStates.AddRange(
            new SubscribedState { SubscriberId = sub1.Id, StateCode = "CA", StateName = "California", IsActive = true, StartDate = DateTime.UtcNow.ToString("o") },
            new SubscribedState { SubscriberId = sub1.Id, StateCode = "TX", StateName = "Texas", IsActive = true, StartDate = DateTime.UtcNow.ToString("o") }
        );

        // sub2 subscribes to only NY
        db.SubscribedStates.Add(
            new SubscribedState { SubscriberId = sub2.Id, StateCode = "NY", StateName = "New York", IsActive = true, StartDate = DateTime.UtcNow.ToString("o") }
        );
        await db.SaveChangesAsync();

        var sub1States = await db.SubscribedStates
            .Where(ss => ss.SubscriberId == sub1.Id && ss.IsActive)
            .Select(ss => ss.StateCode)
            .ToListAsync();

        var sub2States = await db.SubscribedStates
            .Where(ss => ss.SubscriberId == sub2.Id && ss.IsActive)
            .Select(ss => ss.StateCode)
            .ToListAsync();

        Assert.That(sub1States, Is.EquivalentTo(new[] { "CA", "TX" }));
        Assert.That(sub2States, Is.EquivalentTo(new[] { "NY" }));
    }
}
