namespace TaxRateCollector.Core.Enums;

public enum ScrapeStatus
{
    /// <summary>Queued by the UI, waiting for the Worker to pick it up.</summary>
    Pending,
    Running,
    Completed,
    Failed,
    /// <summary>Rate was entered manually through the UI, not via a scrape job.</summary>
    Manual
}
