namespace TaxRateCollector.Core.Enums;

public enum ScrapeStatus
{
    Running,
    Completed,
    Failed,
    /// <summary>Rate was entered manually through the UI, not via a scrape job.</summary>
    Manual
}
