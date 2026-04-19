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
///     └─ Food &amp; Beverage
///          ├─ Groceries / Unprepared Food   (exempt in 32 states)
///          ├─ Prepared Food                 (taxed everywhere)
///          ├─ Candy &amp; Confectionery
///          └─ Non-Alcoholic Beverages
///     └─ Clothing &amp; Apparel
///     └─ Pharmaceuticals &amp; Medical
///     └─ Electronics &amp; Technology
///     └─ Home &amp; Garden
///     └─ Automotive
///     └─ Construction Materials
///   Services
///     └─ Professional Services              (exempt in most states)
///     └─ Personal Care
///     └─ Digital &amp; Technology Services
///     └─ Construction &amp; Repair
///     └─ Entertainment &amp; Recreation
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
/// Resolution order for a transaction:
///   1. Exact (TaxCategoryId, JurisdictionId) match — most specific.
///   2. Walk up the jurisdiction hierarchy to find the nearest ancestor rule.
///   3. Fall back to <see cref="StateCategoryRule"/> for the state-level default.
///   4. Apply standard rate chain (State + County + City) at full rate if no rule found.
///
/// A missing row means "apply the standard rate chain at full rate".
/// Typically set at State level; county/city overrides are rare.
///
/// Examples:
///   Groceries + California State → Taxability = Exempt
///   Groceries + Tennessee State  → Taxability = ReducedRate, OverrideRate = 0.04
///   Prepared Food + any          → no rule (always taxed at standard rate)
/// </summary>
public class TaxCategoryRule
{
    public int Id { get; set; }
    public int TaxCategoryId { get; set; }
    public int JurisdictionId { get; set; }

    /// <summary>Whether this category is taxable, exempt, reduced-rate, or subject to a special rule in this jurisdiction.</summary>
    public Core.Enums.CategoryTaxability Taxability { get; set; } = Core.Enums.CategoryTaxability.Taxable;

    /// <summary>
    /// Explicit rate for this category in this jurisdiction (e.g., 0.04 for Tennessee's 4% food rate).
    /// Null = use the jurisdiction's general TaxRate rows.
    /// Ignored if Taxability = Exempt.
    /// </summary>
    public decimal? OverrideRate { get; set; }

    /// <summary>
    /// Whether local additive rates apply when this category is taxable in this jurisdiction.
    /// False when the state blocks local rates for this category (e.g., prescription drugs are
    /// exempt from both state AND local in most states).
    /// </summary>
    public bool LocalRateApplies { get; set; } = true;

    /// <summary>Statutory or regulatory citation (e.g., "CA Rev. &amp; Tax Code §6359").</summary>
    public string? StatutoryReference { get; set; }

    /// <summary>URL of the official source document for this rule.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>Human-readable note explaining nuances (e.g., "Exempt only when sold without utensils").</summary>
    public string? Notes { get; set; }

    /// <summary>Date on which this rule became effective. Null if unknown.</summary>
    public DateOnly? EffectiveDate { get; set; }

    public TaxCategory TaxCategory { get; set; } = null!;
    public Jurisdiction Jurisdiction { get; set; } = null!;
}
