namespace TaxRateCollector.Core.Entities;

/// <summary>
/// Singleton pricing record (always Id = 1). Admin-configurable from Settings → Admin.
/// </summary>
public class PricingConfig
{
    public int Id { get; set; }
    public decimal PricePerState { get; set; } = 0.01m;
    public decimal PricePerCategory { get; set; } = 0.01m;
    public string Currency { get; set; } = "USD";
    public string UpdatedAt { get; set; } = "";
}
