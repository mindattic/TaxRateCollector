using TaxRateCollector.Core.Enums;

namespace TaxRateCollector.Core.Entities;

/// <summary>
/// Describes how a state's sales tax system works at the structural level.
/// Exists independently of whether Jurisdiction rows have been imported.
///
/// For SST member states the rules are uniform statewide and governed by SSUTA.
/// For non-members (CA, TX, NY, FL, PA, CO, etc.) the profile captures the
/// state-specific structural differences — home-rule authority, local caps,
/// base rate — that cannot be inferred from the SST taxonomy alone.
/// </summary>
public class StateTaxProfile
{
    public int Id { get; set; }

    /// <summary>Two-letter USPS state code (unique).</summary>
    public string StateCode { get; set; } = "";

    public string StateName { get; set; } = "";

    /// <summary>Whether the state is a full SST Governing Board member.</summary>
    public bool IsSstMember { get; set; }

    /// <summary>General statewide sales tax rate (e.g., 0.0625 for Texas 6.25%).</summary>
    public decimal GeneralSalesTaxRate { get; set; }

    /// <summary>How local jurisdictions relate to the state tax base.</summary>
    public LocalTaxAuthorityType LocalTaxAuthorityType { get; set; }

    /// <summary>
    /// True if the state caps total combined local rates
    /// (e.g., Texas caps combined local tax at 2%).
    /// </summary>
    public bool HasLocalRateCap { get; set; }

    /// <summary>Maximum combined local rate when capped. Null if no cap.</summary>
    public decimal? LocalRateCap { get; set; }

    /// <summary>Official name of the state tax authority (e.g., "Texas Comptroller of Public Accounts").</summary>
    public string StateRevenueAgencyName { get; set; } = "";

    /// <summary>Primary URL for state tax rate / taxability information.</summary>
    public string StateRevenueUrl { get; set; } = "";

    /// <summary>
    /// Post-Wayfair (South Dakota v. Wayfair, 2018) economic nexus threshold: the minimum
    /// annual sales dollar amount into this state that triggers a remote seller's obligation
    /// to register and collect sales tax. Most states adopted $100,000. Null = not established
    /// or not applicable (e.g. state has no sales tax).
    /// </summary>
    public decimal? EconomicNexusThresholdAmount { get; set; }

    /// <summary>
    /// Post-Wayfair transaction count threshold that independently triggers economic nexus,
    /// regardless of dollar amount. Most states adopted 200 transactions. Null = no
    /// transaction-count threshold (some states use dollar-only thresholds).
    /// </summary>
    public int? EconomicNexusThresholdTransactions { get; set; }

    /// <summary>Admin notes about quirks, pending changes, or data gaps for this state.</summary>
    public string? Notes { get; set; }

    public string UpdatedAt { get; set; } = "";

    public ICollection<StateCategoryRule> CategoryRules { get; set; } = [];
}

/// <summary>
/// State-level taxability rule for a specific SST product/service category.
///
/// A missing row means "unknown — not yet sourced from the state revenue authority".
/// Populated from state statutes, revenue rulings, or SSUTA amendments.
///
/// The combination (StateTaxProfileId, TaxCategoryId) is unique.
/// </summary>
public class StateCategoryRule
{
    public int Id { get; set; }

    public int StateTaxProfileId { get; set; }

    /// <summary>
    /// The SST leaf category this rule applies to.
    /// Nullable until TaxCategories have been populated from SSUTA Appendix C.
    /// </summary>
    public int? TaxCategoryId { get; set; }

    /// <summary>Whether this category is taxable, exempt, reduced-rate, or subject to a special rule.</summary>
    public CategoryTaxability Taxability { get; set; } = CategoryTaxability.Unknown;

    /// <summary>
    /// The state's rate for this category when Taxability = ReducedRate or Taxable with a
    /// category-specific override (e.g., 0.04 for Tennessee's 4% food rate).
    /// Null = use StateTaxProfile.GeneralSalesTaxRate.
    /// Ignored when Taxability = Exempt.
    /// </summary>
    public decimal? StateRate { get; set; }

    /// <summary>
    /// Whether local additive rates apply when this category is taxable.
    /// False for some exempt categories in states where local rates are also blocked
    /// (e.g., prescription drugs are exempt from both state AND local in most states).
    /// </summary>
    public bool LocalRateApplies { get; set; } = true;

    /// <summary>
    /// Statutory or regulatory citation (e.g., "CA Rev. & Tax Code §6359",
    /// "SSUTA Section 313 — Clothing").
    /// </summary>
    public string? StatutoryReference { get; set; }

    /// <summary>URL of the official source document for this rule.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>Human-readable note explaining nuances (e.g., "Exempt only when sold without utensils").</summary>
    public string? Notes { get; set; }

    /// <summary>Date on which this rule became effective. Null if unknown.</summary>
    public DateOnly? EffectiveDate { get; set; }

    public StateTaxProfile StateTaxProfile { get; set; } = null!;
    public TaxCategory? TaxCategory { get; set; }
}
