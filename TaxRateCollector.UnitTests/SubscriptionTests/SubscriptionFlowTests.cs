using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.UnitTests.SubscriptionTests;

/// <summary>
/// Tests for the full subscription lifecycle:
/// subscriber creation, state activation, billing records, and soft-delete behavior.
/// </summary>
[TestFixture]
public class SubscriptionFlowTests
{
    private static AppDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(opts);
    }

    private static PricingConfig DefaultPricing() =>
        new() { Id = 1, PricePerState = 0.01m, Currency = "USD", UpdatedAt = DateTime.UtcNow.ToString("o") };

    // ── Subscriber creation ───────────────────────────────────────────────────

    [Test]
    public async Task CreateSubscriber_PersistsAllFields()
    {
        await using var db = CreateDb();

        var subscriber = new Subscriber
        {
            UserId = "user-abc-123",
            FullName = "Jane Smith",
            AddressLine1 = "123 Main St",
            City = "Springfield",
            StateCode = "IL",
            ZipCode = "62701",
            IsActive = true,
            CreatedAt = DateTime.UtcNow.ToString("o")
        };
        db.Subscribers.Add(subscriber);
        await db.SaveChangesAsync();

        var loaded = await db.Subscribers.FindAsync(subscriber.Id);
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.UserId, Is.EqualTo("user-abc-123"));
        Assert.That(loaded.FullName, Is.EqualTo("Jane Smith"));
        Assert.That(loaded.StateCode, Is.EqualTo("IL"));
        Assert.That(loaded.IsActive, Is.True);
    }

    [Test]
    public async Task CreateSubscriber_DuplicateUser_BothExist()
    {
        // Two subscriptions for the same user are allowed at the DB level;
        // business logic in the checkout page handles de-duplication.
        await using var db = CreateDb();

        db.Subscribers.Add(new Subscriber { UserId = "user-x", FullName = "A", CreatedAt = DateTime.UtcNow.ToString("o") });
        db.Subscribers.Add(new Subscriber { UserId = "user-x", FullName = "B", CreatedAt = DateTime.UtcNow.ToString("o") });
        await db.SaveChangesAsync();

        var count = await db.Subscribers.CountAsync(s => s.UserId == "user-x");
        Assert.That(count, Is.EqualTo(2));
    }

    // ── SubscribedState activation ────────────────────────────────────────────

    [Test]
    public async Task SubscribedState_AddStates_AllActive()
    {
        await using var db = CreateDb();

        var subscriber = new Subscriber { UserId = "u1", CreatedAt = DateTime.UtcNow.ToString("o") };
        db.Subscribers.Add(subscriber);
        await db.SaveChangesAsync();

        var states = new[] { "CA", "OR", "WA" };
        foreach (var code in states)
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

        var active = await db.SubscribedStates
            .Where(ss => ss.SubscriberId == subscriber.Id && ss.IsActive)
            .CountAsync();

        Assert.That(active, Is.EqualTo(3));
    }

    [Test]
    public async Task SubscribedState_Deactivate_SoftDeletePreservesRecord()
    {
        await using var db = CreateDb();

        var subscriber = new Subscriber { UserId = "u2", CreatedAt = DateTime.UtcNow.ToString("o") };
        db.Subscribers.Add(subscriber);
        await db.SaveChangesAsync();

        var ss = new SubscribedState
        {
            SubscriberId = subscriber.Id,
            StateCode = "TX",
            StateName = "Texas",
            IsActive = true,
            StartDate = DateTime.UtcNow.ToString("o")
        };
        db.SubscribedStates.Add(ss);
        await db.SaveChangesAsync();
        var ssId = ss.Id;

        // Deactivate (plan change — unsubscribe from state)
        ss.IsActive = false;
        await db.SaveChangesAsync();

        // Record still exists
        var preserved = await db.SubscribedStates.FindAsync(ssId);
        Assert.That(preserved, Is.Not.Null);
        Assert.That(preserved!.IsActive, Is.False);

        // No longer returned in active query
        var activeCount = await db.SubscribedStates
            .CountAsync(s => s.SubscriberId == subscriber.Id && s.IsActive);
        Assert.That(activeCount, Is.EqualTo(0));
    }

    [Test]
    public async Task SubscribedState_Resubscribe_ReactivatesExistingRow()
    {
        await using var db = CreateDb();

        var subscriber = new Subscriber { UserId = "u3", CreatedAt = DateTime.UtcNow.ToString("o") };
        db.Subscribers.Add(subscriber);
        await db.SaveChangesAsync();

        // Initial subscribe
        var ss = new SubscribedState
        {
            SubscriberId = subscriber.Id,
            StateCode = "FL",
            StateName = "Florida",
            IsActive = true,
            StartDate = DateTime.UtcNow.ToString("o")
        };
        db.SubscribedStates.Add(ss);
        await db.SaveChangesAsync();

        // Deactivate
        ss.IsActive = false;
        await db.SaveChangesAsync();

        // Re-subscribe — reactivate existing row (no new row)
        ss.IsActive = true;
        await db.SaveChangesAsync();

        var rows = await db.SubscribedStates
            .Where(s => s.SubscriberId == subscriber.Id && s.StateCode == "FL")
            .CountAsync();

        Assert.That(rows, Is.EqualTo(1), "Re-subscribing should reactivate the existing row, not insert a duplicate.");

        var active = await db.SubscribedStates
            .CountAsync(s => s.SubscriberId == subscriber.Id && s.IsActive);
        Assert.That(active, Is.EqualTo(1));
    }

    [Test]
    public async Task SubscribedState_PlanChange_DeactivatesOldActivatesNew()
    {
        await using var db = CreateDb();

        var subscriber = new Subscriber { UserId = "u4", CreatedAt = DateTime.UtcNow.ToString("o") };
        db.Subscribers.Add(subscriber);
        await db.SaveChangesAsync();

        // Original plan: CA + OR
        db.SubscribedStates.AddRange(
            new SubscribedState { SubscriberId = subscriber.Id, StateCode = "CA", StateName = "California", IsActive = true, StartDate = DateTime.UtcNow.ToString("o") },
            new SubscribedState { SubscriberId = subscriber.Id, StateCode = "OR", StateName = "Oregon", IsActive = true, StartDate = DateTime.UtcNow.ToString("o") }
        );
        await db.SaveChangesAsync();

        // Plan change: switch to CA + WA (drop OR, add WA)
        var existing = await db.SubscribedStates.Where(ss => ss.SubscriberId == subscriber.Id).ToListAsync();
        foreach (var row in existing) row.IsActive = false;

        var newStates = new[] { ("CA", "California"), ("WA", "Washington") };
        foreach (var (code, name) in newStates)
        {
            var existingRow = existing.FirstOrDefault(ss => ss.StateCode == code);
            if (existingRow is not null)
                existingRow.IsActive = true;
            else
                db.SubscribedStates.Add(new SubscribedState { SubscriberId = subscriber.Id, StateCode = code, StateName = name, IsActive = true, StartDate = DateTime.UtcNow.ToString("o") });
        }
        await db.SaveChangesAsync();

        var activeStates = await db.SubscribedStates
            .Where(ss => ss.SubscriberId == subscriber.Id && ss.IsActive)
            .Select(ss => ss.StateCode)
            .OrderBy(c => c)
            .ToListAsync();

        Assert.That(activeStates, Is.EqualTo(new[] { "CA", "WA" }));

        // OR is preserved but inactive (soft-delete audit trail)
        var orRow = await db.SubscribedStates
            .FirstOrDefaultAsync(ss => ss.SubscriberId == subscriber.Id && ss.StateCode == "OR");
        Assert.That(orRow, Is.Not.Null);
        Assert.That(orRow!.IsActive, Is.False);
    }

    // ── BillingRecord persistence ─────────────────────────────────────────────

    [Test]
    public async Task BillingRecord_Created_PersistsWithPendingStatus()
    {
        await using var db = CreateDb();

        var subscriber = new Subscriber { UserId = "u5", CreatedAt = DateTime.UtcNow.ToString("o") };
        db.Subscribers.Add(subscriber);
        await db.SaveChangesAsync();

        var billing = new BillingRecord
        {
            SubscriberId = subscriber.Id,
            StateCount = 3,
            PricePerState = 0.01m,
            Subtotal = 0.03m,
            BillingStateCode = "IL",
            TaxRate = 0.0625m,
            TaxAmount = 0.001875m,
            Total = 0.031875m,
            Currency = "USD",
            Status = BillingStatus.Pending,
            CreatedAt = DateTime.UtcNow.ToString("o")
        };
        db.BillingRecords.Add(billing);
        await db.SaveChangesAsync();

        var loaded = await db.BillingRecords.FindAsync(billing.Id);
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.Status, Is.EqualTo(BillingStatus.Pending));
        Assert.That(loaded.StateCount, Is.EqualTo(3));
        Assert.That(loaded.Currency, Is.EqualTo("USD"));
    }

    [Test]
    public async Task BillingRecord_StatusTransition_PendingToCompleted()
    {
        await using var db = CreateDb();

        var subscriber = new Subscriber { UserId = "u6", CreatedAt = DateTime.UtcNow.ToString("o") };
        db.Subscribers.Add(subscriber);
        await db.SaveChangesAsync();

        var billing = new BillingRecord
        {
            SubscriberId = subscriber.Id,
            StateCount = 1,
            PricePerState = 0.01m,
            Subtotal = 0.01m,
            BillingStateCode = "TX",
            TaxRate = 0m,
            TaxAmount = 0m,
            Total = 0.01m,
            Currency = "USD",
            Status = BillingStatus.Pending,
            CreatedAt = DateTime.UtcNow.ToString("o")
        };
        db.BillingRecords.Add(billing);
        await db.SaveChangesAsync();

        billing.PayPalOrderId = "MOCK-abc123";
        billing.Status = BillingStatus.Completed;
        await db.SaveChangesAsync();

        var loaded = await db.BillingRecords.FindAsync(billing.Id);
        Assert.That(loaded!.Status, Is.EqualTo(BillingStatus.Completed));
        Assert.That(loaded.PayPalOrderId, Is.EqualTo("MOCK-abc123"));
    }

    [Test]
    public async Task BillingRecord_StatusTransition_PendingToFailed()
    {
        await using var db = CreateDb();

        var subscriber = new Subscriber { UserId = "u7", CreatedAt = DateTime.UtcNow.ToString("o") };
        db.Subscribers.Add(subscriber);
        await db.SaveChangesAsync();

        var billing = new BillingRecord
        {
            SubscriberId = subscriber.Id,
            StateCount = 2,
            PricePerState = 0.01m,
            Subtotal = 0.02m,
            BillingStateCode = "CA",
            Status = BillingStatus.Pending,
            CreatedAt = DateTime.UtcNow.ToString("o")
        };
        db.BillingRecords.Add(billing);
        await db.SaveChangesAsync();

        billing.Status = BillingStatus.Failed;
        await db.SaveChangesAsync();

        var loaded = await db.BillingRecords.FindAsync(billing.Id);
        Assert.That(loaded!.Status, Is.EqualTo(BillingStatus.Failed));
    }

    [Test]
    public async Task BillingRecord_MultiplePerSubscriber_AllPreserved()
    {
        await using var db = CreateDb();

        var subscriber = new Subscriber { UserId = "u8", CreatedAt = DateTime.UtcNow.ToString("o") };
        db.Subscribers.Add(subscriber);
        await db.SaveChangesAsync();

        for (var i = 1; i <= 3; i++)
        {
            db.BillingRecords.Add(new BillingRecord
            {
                SubscriberId = subscriber.Id,
                StateCount = i,
                PricePerState = 0.01m,
                Subtotal = i * 0.01m,
                Currency = "USD",
                Status = BillingStatus.Completed,
                CreatedAt = DateTime.UtcNow.AddMonths(-i).ToString("o")
            });
        }
        await db.SaveChangesAsync();

        var count = await db.BillingRecords.CountAsync(b => b.SubscriberId == subscriber.Id);
        Assert.That(count, Is.EqualTo(3));
    }

    // ── PricingConfig ─────────────────────────────────────────────────────────

    [Test]
    public async Task PricingConfig_DefaultValues_AreCorrect()
    {
        await using var db = CreateDb();

        db.PricingConfigs.Add(DefaultPricing());
        await db.SaveChangesAsync();

        var cfg = await db.PricingConfigs.FirstAsync();
        Assert.That(cfg.PricePerState, Is.EqualTo(0.01m));
        Assert.That(cfg.Currency, Is.EqualTo("USD"));
    }

    [Test]
    public async Task PricingConfig_Update_ReflectsNewPrice()
    {
        await using var db = CreateDb();

        var pricing = DefaultPricing();
        db.PricingConfigs.Add(pricing);
        await db.SaveChangesAsync();

        pricing.PricePerState = 0.05m;
        pricing.UpdatedAt = DateTime.UtcNow.ToString("o");
        db.PricingConfigs.Update(pricing);
        await db.SaveChangesAsync();

        var loaded = await db.PricingConfigs.FindAsync(pricing.Id);
        Assert.That(loaded!.PricePerState, Is.EqualTo(0.05m));
    }

    // ── Subscriber cascade delete ─────────────────────────────────────────────

    [Test]
    public async Task DeleteSubscriber_CascadesStatesAndBillingRecords()
    {
        await using var db = CreateDb();

        var subscriber = new Subscriber { UserId = "u9", CreatedAt = DateTime.UtcNow.ToString("o") };
        db.Subscribers.Add(subscriber);
        await db.SaveChangesAsync();

        db.SubscribedStates.Add(new SubscribedState { SubscriberId = subscriber.Id, StateCode = "CA", StateName = "California", IsActive = true, StartDate = DateTime.UtcNow.ToString("o") });
        db.BillingRecords.Add(new BillingRecord { SubscriberId = subscriber.Id, StateCount = 1, Currency = "USD", Status = BillingStatus.Completed, CreatedAt = DateTime.UtcNow.ToString("o") });
        await db.SaveChangesAsync();

        db.Subscribers.Remove(subscriber);
        await db.SaveChangesAsync();

        Assert.That(await db.SubscribedStates.AnyAsync(ss => ss.SubscriberId == subscriber.Id), Is.False);
        Assert.That(await db.BillingRecords.AnyAsync(b => b.SubscriberId == subscriber.Id), Is.False);
    }
}
