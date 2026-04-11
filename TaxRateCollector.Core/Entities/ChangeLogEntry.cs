using TaxRateCollector.Core.Enums;

namespace TaxRateCollector.Core.Entities;

public class ChangeLogEntry
{
    public int Id { get; set; }
    public int JurisdictionId { get; set; }
    public ChangeType ChangeType { get; set; }
    public decimal? OldRate { get; set; }
    public decimal? NewRate { get; set; }
    public string DetectedAt { get; set; } = string.Empty;
    public bool Acknowledged { get; set; }

    public Jurisdiction Jurisdiction { get; set; } = null!;
}
