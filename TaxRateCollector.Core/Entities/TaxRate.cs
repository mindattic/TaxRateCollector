namespace TaxRateCollector.Core.Entities;

public class TaxRate
{
    public int Id { get; set; }
    public int JurisdictionId { get; set; }
    public decimal Rate { get; set; }
    public string RateType { get; set; } = "General";
    public string EffectiveDate { get; set; } = string.Empty;
    public string ScrapedAt { get; set; } = string.Empty;
    public int ScrapeRunId { get; set; }
    public string RawValue { get; set; } = string.Empty;
    public bool IsCurrent { get; set; }

    public Jurisdiction Jurisdiction { get; set; } = null!;
    public ScrapeRun ScrapeRun { get; set; } = null!;
}
