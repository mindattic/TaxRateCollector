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
    /// A live order is not paid yet: the buyer must open <see cref="PayPalOrderResult.ApprovalUrl"/>
    /// and approve it; PayPal then sends them back to <paramref name="returnUrl"/> (with
    /// <c>token=&lt;orderId&gt;</c>), and only then may the order be captured.
    /// </summary>
    Task<PayPalOrderResult> CreateOrderAsync(decimal amount, string currency, string description,
        string? returnUrl = null, string? cancelUrl = null);

    /// <summary>
    /// Captures a buyer-approved PayPal order; true only when PayPal reports the capture
    /// COMPLETED. Always succeeds for mock orders.
    /// </summary>
    Task<bool> CaptureOrderAsync(string orderId);

    /// <summary>True when PayPal Client ID + Secret are configured in the database.</summary>
    Task<bool> IsConfiguredAsync();
}
