namespace TaxRateCollector.Core.Entities;

/// <summary>
/// Hierarchical product/service category for sales-tax classification.
///
/// Top-level types: "Goods" and "Services".
/// Tax on a transaction = (StateRate + CountyRate + CityRate for the ZIP code)
///                       × the TaxCategoryRule modifier for the matching category + jurisdiction.
///
/// Example tree:
///   Goods
///     └─ Food & Beverage
///          ├─ Groceries / Unprepared Food   (exempt in 32 states)
///          ├─ Prepared Food                 (taxed everywhere)
///          ├─ Candy & Confectionery
///          └─ Non-Alcoholic Beverages
///     └─ Clothing & Apparel
///     └─ Pharmaceuticals & Medical
///     └─ Electronics & Technology
///     └─ Home & Garden
///     └─ Automotive
///     └─ Construction Materials
///   Services
///     └─ Professional Services              (exempt in most states)
///     └─ Personal Care
///     └─ Digital & Technology Services
///     └─ Construction & Repair
///     └─ Entertainment & Recreation
///     └─ Healthcare Services                (exempt in all states)
///     └─ Transportation
/// </summary>
public class TaxCategory
{
    public int Id { get; set; }
    public int? ParentId { get; set; }

    /// <summary>"Goods" or "Services" — top-level classification (denormalized for fast filtering).</summary>
    public string TopLevelType { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>True if this is a leaf node that can be directly assigned to a product/transaction.</summary>
    public bool IsLeaf { get; set; }

    public int SortOrder { get; set; }

    public TaxCategory? Parent { get; set; }
    public ICollection<TaxCategory> Children { get; set; } = new List<TaxCategory>();
    public ICollection<TaxCategoryRule> Rules { get; set; } = new List<TaxCategoryRule>();
}

/// <summary>
/// Jurisdiction-specific tax treatment override for a product category.
///
/// A missing row means "apply the standard rate chain (State + County + City) at full rate".
/// Typically set at State level; county/city overrides are rare.
///
/// Examples:
///   Groceries + California State → IsExempt = true (CA exempts most unprepared food)
///   Groceries + Tennessee State → OverrideRate = 0.04m (TN taxes food at 4% instead of 7%)
///   Prepared Food + any jurisdiction → no rule (always taxed at standard rate)
/// </summary>
public class TaxCategoryRule
{
    public int Id { get; set; }
    public int TaxCategoryId { get; set; }
    public int JurisdictionId { get; set; }

    /// <summary>True if this category is completely exempt from sales tax in this jurisdiction.</summary>
    public bool IsExempt { get; set; }

    /// <summary>
    /// Explicit rate for this category in this jurisdiction (e.g., 0.04 for Tennessee's 4% food rate).
    /// Null = use the jurisdiction's general rate.
    /// Ignored if IsExempt = true.
    /// </summary>
    public decimal? OverrideRate { get; set; }

    /// <summary>Statutory citation or short note (e.g., "CA Rev. & Tax Code §6359 — sales of food for human consumption").</summary>
    public string Notes { get; set; } = string.Empty;

    public string EffectiveDate { get; set; } = string.Empty;

    public TaxCategory TaxCategory { get; set; } = null!;
    public Jurisdiction Jurisdiction { get; set; } = null!;
}
