using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Infrastructure.Data;
using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.UnitTests.PayPalTests;

/// <summary>
/// Tests for PayPalService mock-mode behavior and order ID conventions.
/// Real HTTP calls are not made — all tests target code paths that run
/// without network access (mock mode when credentials are absent, and
/// MOCK-* capture shortcut that bypasses HTTP entirely).
/// </summary>
[TestFixture]
public class PayPalServiceTests
{
    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> opts)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(opts);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(new AppDbContext(opts));
    }

    private static (PayPalService svc, IDbContextFactory<AppDbContext> factory) BuildMockService(
        string? clientId = null, string? clientSecret = null, string mode = "sandbox")
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var factory = new TestDbContextFactory(opts);

        using var db = factory.CreateDbContext();
        db.PayPalConfigs.Add(new PayPalConfig
        {
            Id = 1,
            ClientId = clientId ?? "",
            ClientSecret = clientSecret ?? "",
            Mode = mode,
            UpdatedAt = DateTime.UtcNow.ToString("o")
        });
        db.SaveChanges();

        // IHttpClientFactory is not needed for mock-mode tests.
        // Pass null — the code path that uses it is only reached with real credentials.
        var svc = new PayPalService(factory, null!, NullLogger<PayPalService>.Instance);
        return (svc, factory);
    }

    // ── IsConfiguredAsync ─────────────────────────────────────────────────────

    [Test]
    public async Task IsConfigured_NoCreds_ReturnsFalse()
    {
        var (svc, _) = BuildMockService();
        Assert.That(await svc.IsConfiguredAsync(), Is.False);
    }

    [Test]
    public async Task IsConfigured_OnlyClientId_ReturnsFalse()
    {
        var (svc, _) = BuildMockService(clientId: "AXabc");
        Assert.That(await svc.IsConfiguredAsync(), Is.False);
    }

    [Test]
    public async Task IsConfigured_OnlyClientSecret_ReturnsFalse()
    {
        var (svc, _) = BuildMockService(clientSecret: "EXsecret");
        Assert.That(await svc.IsConfiguredAsync(), Is.False);
    }

    [Test]
    public async Task IsConfigured_BothCreds_ReturnsTrue()
    {
        var (svc, _) = BuildMockService(clientId: "AXabc", clientSecret: "EXsecret");
        Assert.That(await svc.IsConfiguredAsync(), Is.True);
    }

    [Test]
    public async Task IsConfigured_WhitespaceCreds_ReturnsFalse()
    {
        var (svc, _) = BuildMockService(clientId: "   ", clientSecret: "   ");
        Assert.That(await svc.IsConfiguredAsync(), Is.False);
    }

    [Test]
    public async Task IsConfigured_NoConfigRow_ReturnsFalse()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var factory = new TestDbContextFactory(opts);
        // No PayPalConfig row seeded
        var svc = new PayPalService(factory, null!, NullLogger<PayPalService>.Instance);
        Assert.That(await svc.IsConfiguredAsync(), Is.False);
    }

    // ── CreateOrderAsync — mock mode (no credentials) ─────────────────────────

    [Test]
    public async Task CreateOrder_NoCreds_ReturnsMockOrder()
    {
        var (svc, _) = BuildMockService();
        var result = await svc.CreateOrderAsync(0.50m, "USD", "Test subscription");

        Assert.That(result.Success, Is.True);
        Assert.That(result.IsMock, Is.True);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public async Task CreateOrder_NoCreds_OrderIdStartsWithMock()
    {
        var (svc, _) = BuildMockService();
        var result = await svc.CreateOrderAsync(0.03m, "USD", "3 states/month");

        Assert.That(result.OrderId, Does.StartWith("MOCK-"));
    }

    [Test]
    public async Task CreateOrder_NoCreds_OrderIdIsUnique()
    {
        var (svc, _) = BuildMockService();
        var ids = new HashSet<string>();

        for (var i = 0; i < 20; i++)
        {
            var result = await svc.CreateOrderAsync(0.01m, "USD", "1 state/month");
            ids.Add(result.OrderId);
        }

        Assert.That(ids, Has.Count.EqualTo(20), "Every mock order ID must be unique.");
    }

    [Test]
    public async Task CreateOrder_NoCreds_ApprovalUrlIsNull()
    {
        var (svc, _) = BuildMockService();
        var result = await svc.CreateOrderAsync(0.01m, "USD", "test");
        Assert.That(result.ApprovalUrl, Is.Null);
    }

    [Test]
    [TestCase(0.01)]
    [TestCase(0.50)]
    [TestCase(99.99)]
    public async Task CreateOrder_MockMode_AmountDoesNotAffectSuccess(double amount)
    {
        var (svc, _) = BuildMockService();
        var result = await svc.CreateOrderAsync((decimal)amount, "USD", "test");
        Assert.That(result.Success, Is.True);
    }

    // ── CaptureOrderAsync — MOCK-* shortcut ──────────────────────────────────

    [Test]
    public async Task CaptureOrder_MockId_ReturnsTrue()
    {
        var (svc, _) = BuildMockService();
        var captured = await svc.CaptureOrderAsync("MOCK-abc123def456");
        Assert.That(captured, Is.True);
    }

    [Test]
    public async Task CaptureOrder_MockIdFromCreateOrder_ReturnsTrue()
    {
        var (svc, _) = BuildMockService();
        var created = await svc.CreateOrderAsync(0.05m, "USD", "test");
        var captured = await svc.CaptureOrderAsync(created.OrderId);
        Assert.That(captured, Is.True);
    }

    [Test]
    [TestCase("MOCK-00000000000000000000000000000000")]
    [TestCase("MOCK-ffffffffffffffffffffffffffffffff")]
    [TestCase("MOCK-abcdef1234567890abcdef1234567890")]
    public async Task CaptureOrder_AnyMockId_ReturnsTrue(string orderId)
    {
        var (svc, _) = BuildMockService();
        Assert.That(await svc.CaptureOrderAsync(orderId), Is.True);
    }

    [Test]
    public async Task CaptureOrder_NullOrderIdNoConfig_ReturnsFalse()
    {
        // Non-MOCK- ID with no config row → returns false (cannot reach PayPal API)
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var factory = new TestDbContextFactory(opts);
        var svc = new PayPalService(factory, null!, NullLogger<PayPalService>.Instance);

        var result = await svc.CaptureOrderAsync("REAL-ORDER-12345");
        Assert.That(result, Is.False);
    }

    // ── Order ID format ───────────────────────────────────────────────────────

    [Test]
    public async Task MockOrderId_Format_IsCorrect()
    {
        var (svc, _) = BuildMockService();
        var result = await svc.CreateOrderAsync(0.01m, "USD", "test");

        // Format: "MOCK-" + 32 hex chars (UUIDv7 without hyphens)
        Assert.That(result.OrderId, Does.StartWith("MOCK-"));
        var suffix = result.OrderId["MOCK-".Length..];
        Assert.That(suffix, Has.Length.EqualTo(32));
        Assert.That(suffix, Does.Match("^[0-9a-f]{32}$"), "Suffix must be 32 lowercase hex chars.");
    }

    // ── End-to-end mock flow ──────────────────────────────────────────────────

    [Test]
    public async Task MockFlow_CreateThenCapture_BothSucceed()
    {
        var (svc, _) = BuildMockService();

        var created = await svc.CreateOrderAsync(0.50m, "USD", "50 states/month");
        Assert.That(created.Success, Is.True);
        Assert.That(created.IsMock, Is.True);

        var captured = await svc.CaptureOrderAsync(created.OrderId);
        Assert.That(captured, Is.True);
    }

    [Test]
    public async Task MockFlow_MultipleOrdersCapture_AllSucceed()
    {
        var (svc, _) = BuildMockService();

        var orders = new List<string>();
        for (var i = 1; i <= 5; i++)
        {
            var created = await svc.CreateOrderAsync(i * 0.01m, "USD", $"{i} states");
            orders.Add(created.OrderId);
        }

        foreach (var id in orders)
        {
            var captured = await svc.CaptureOrderAsync(id);
            Assert.That(captured, Is.True, $"Capture should succeed for order {id}");
        }
    }
}
