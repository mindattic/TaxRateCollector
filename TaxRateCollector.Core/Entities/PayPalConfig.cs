namespace TaxRateCollector.Core.Entities;

/// <summary>
/// Singleton PayPal credential record (always Id = 1). Admin-configurable from Settings → Admin.
/// ClientSecret is stored plain in the DB; restrict DB access appropriately in production,
/// or add IDataProtector encryption over the column as a hardening step.
/// </summary>
public class PayPalConfig
{
    public int Id { get; set; }
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Mode { get; set; } = "sandbox"; // "sandbox" | "live"
    public string WebhookId { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}
