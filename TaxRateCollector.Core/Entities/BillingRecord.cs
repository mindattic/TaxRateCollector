using TaxRateCollector.Core.Enums;

namespace TaxRateCollector.Core.Entities;

/// <summary>
/// Immutable audit record of every payment attempt.
/// PayPalOrderId is "MOCK-..." when running without live credentials.
/// </summary>
public class BillingRecord
{
    public int Id { get; set; }
    public int SubscriberId { get; set; }
    public int StateCount { get; set; }
    public decimal PricePerState { get; set; }
    public decimal Subtotal { get; set; }
    public string BillingStateCode { get; set; } = "";
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "USD";
    public string PayPalOrderId { get; set; } = "";
    public BillingStatus Status { get; set; } = BillingStatus.Pending;
    public string CreatedAt { get; set; } = "";

    public Subscriber Subscriber { get; set; } = null!;
}
