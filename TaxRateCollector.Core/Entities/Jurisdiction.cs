using TaxRateCollector.Core.Enums;

namespace TaxRateCollector.Core.Entities;

public class Jurisdiction
{
    public int Id { get; set; }
    public string StateCode { get; set; } = string.Empty;
    public string JurisdictionName { get; set; } = string.Empty;
    public JurisdictionType JurisdictionType { get; set; }
    public string FipsCode { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public ICollection<TaxRate> TaxRates { get; set; } = new List<TaxRate>();
    public ICollection<ChangeLogEntry> ChangeLogEntries { get; set; } = new List<ChangeLogEntry>();
}
