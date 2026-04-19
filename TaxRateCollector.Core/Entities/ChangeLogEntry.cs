using TaxRateCollector.Core.Enums;

namespace TaxRateCollector.Core.Entities;

public class ChangeLogEntry
{
    public int Id { get; set; }
    public int JurisdictionId { get; set; }

    /// <summary>Identifies which named rate law changed. Null for legacy entries.</summary>
    public int? TaxRateId { get; set; }

    /// <summary>Snapshot of the rate law name at the time of the change.</summary>
    public string RateName { get; set; } = string.Empty;

    public ChangeType ChangeType { get; set; }
    public decimal? OldRate { get; set; }
    public decimal? NewRate { get; set; }
    public string DetectedAt { get; set; } = string.Empty;
    public bool Acknowledged { get; set; }

    public Jurisdiction Jurisdiction { get; set; } = null!;
    public TaxRate? TaxRate { get; set; }
}
