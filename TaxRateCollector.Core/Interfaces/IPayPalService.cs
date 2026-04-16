namespace TaxRateCollector.Core.Interfaces;

public sealed record PayPalOrderResult(
    bool Success,
    string OrderId,
    bool IsMock,
    string? ApprovalUrl,
    string? Error);

public interface IPayPalService
{
    /// <summary>
    /// Creates a PayPal order. Returns a mock result when credentials are not configured.
    /// </summary>
    Task<PayPalOrderResult> CreateOrderAsync(decimal amount, string currency, string description);

    /// <summary>
    /// Captures an approved PayPal order. Always succeeds for mock orders.
    /// </summary>
    Task<bool> CaptureOrderAsync(string orderId);

    /// <summary>True when PayPal Client ID + Secret are configured in the database.</summary>
    Task<bool> IsConfiguredAsync();
}
