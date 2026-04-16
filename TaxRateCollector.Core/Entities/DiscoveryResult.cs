using TaxRateCollector.Core.Enums;

namespace TaxRateCollector.Core.Entities;

public class DiscoveryResult
{
    public int JurisdictionId { get; set; }
    public string JurisdictionName { get; set; } = "";
    public string StateCode { get; set; } = "";
    public JurisdictionType Tier { get; set; }

    /// <summary>Pending | Running | Found | NotFound | Skipped | Error</summary>
    public string Status { get; set; } = "Pending";

    public decimal? FoundRate { get; set; }
    public string SourceUsed { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
