using TaxRateCollector.Core.Enums;

namespace TaxRateCollector.Core.Entities;

public class ScrapeRun
{
    public int Id { get; set; }
    public string StartedAt { get; set; } = string.Empty;
    public string? CompletedAt { get; set; }
    public ScrapeStatus Status { get; set; }
    public int TotalScraped { get; set; }
    public int ChangesDetected { get; set; }
    public int ErrorCount { get; set; }

    public ICollection<TaxRate> TaxRates { get; set; } = new List<TaxRate>();
}
