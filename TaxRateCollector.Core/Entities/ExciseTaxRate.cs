using TaxRateCollector.Core.Enums;

namespace TaxRateCollector.Core.Entities;

/// <summary>
/// An excise ("sin") tax rate for a specific product category within a jurisdiction tier.
/// Excise taxes stack on top of the general sales tax chain and are product-specific.
/// For example: Illinois levies a $0.231/pack cigarette tax on top of the 6.25% state sales tax.
/// </summary>
public class ExciseTaxRate
{
    public int Id { get; set; }
    public int JurisdictionId { get; set; }

    /// <summary>The product category this excise applies to.</summary>
    public ProductCategory ProductCategory { get; set; }

    /// <summary>
    /// Tax rate value. Interpretation depends on RateType:
    ///   percentage → decimal fraction of sale price (e.g., 0.10 = 10%)
    ///   flat        → fixed amount per unit (e.g., 0.231 = $0.231 per pack of cigarettes)
    /// </summary>
    public decimal Rate { get; set; }

    /// <summary>"percentage" or "flat".</summary>
    public string RateType { get; set; } = "percentage";

    /// <summary>
    /// Unit description for flat-rate taxes (e.g., "per pack", "per gallon", "per ounce").
    /// Empty for percentage-based excises.
    /// </summary>
    public string Unit { get; set; } = "";

    public string EffectiveDate { get; set; } = "";
    public string ScrapedAt { get; set; } = "";
    public int ScrapeRunId { get; set; }
    public string RawValue { get; set; } = "";
    public bool IsCurrent { get; set; } = true;

    public Jurisdiction Jurisdiction { get; set; } = null!;
    public ScrapeRun ScrapeRun { get; set; } = null!;

    /// <summary>Source document proving this excise rate's veracity.</summary>
    public ExciseSourceDocument? SourceDocument { get; set; }
}

/// <summary>
/// Evidence document for an ExciseTaxRate row — mirrors SourceDocument for general rates.
/// </summary>
public class ExciseSourceDocument
{
    public int Id { get; set; }
    public int ExciseTaxRateId { get; set; }
    public SourceType SourceType { get; set; }
    public string SourceUrl { get; set; } = "";
    public string MimeType { get; set; } = "";
    public string FetchedAt { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string RawContent { get; set; } = "";

    public ExciseTaxRate ExciseTaxRate { get; set; } = null!;
}
