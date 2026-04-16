namespace TaxRateCollector.Core.Entities;

public class TaxRate
{
    public int Id { get; set; }
    public int JurisdictionId { get; set; }

    /// <summary>
    /// The rate for this tier only (e.g. 0.06 for 6%).
    /// The cumulative rate at a purchase location is the sum of all active
    /// ancestor rates in the Country → State → County → City chain.
    /// </summary>
    public decimal Rate { get; set; }

    /// <summary>
    /// Rate category label, e.g. "General", "Sales", "Use", "Excise".
    /// </summary>
    public string RateType { get; set; } = "General";

    public string EffectiveDate { get; set; } = string.Empty;
    public string ScrapedAt { get; set; } = string.Empty;
    public int ScrapeRunId { get; set; }

    /// <summary>
    /// The raw rate string as it appeared in the source before normalisation,
    /// kept for quick auditing without loading the full SourceDocument.
    /// </summary>
    public string RawValue { get; set; } = string.Empty;

    public bool IsCurrent { get; set; }

    // Navigation
    public Jurisdiction Jurisdiction { get; set; } = null!;
    public ScrapeRun ScrapeRun { get; set; } = null!;

    /// <summary>
    /// Evidence documents attached to this rate. Each file is stored on the
    /// filesystem; this collection holds the metadata rows pointing to them.
    /// </summary>
    public ICollection<SourceDocument> SourceDocuments { get; set; } = [];
}
