using TaxRateCollector.Core.Enums;

namespace TaxRateCollector.Core.Entities;

public class TaxRate
{
    public int Id { get; set; }
    public int JurisdictionId { get; set; }

    // ── Identity ──────────────────────────────────────────────────────────────
    /// <summary>Human-readable name of the rate law (e.g. "State Beer Excise Tax").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Statute or code section authorising this rate (e.g. "235 ILCS 5/8-1").</summary>
    public string StatutoryReference { get; set; } = string.Empty;

    // ── Tax type ──────────────────────────────────────────────────────────────
    /// <summary>
    /// What kind of tax this is. Determines calculation behaviour:
    /// SalesTax/OccupancyTax/RentalSurcharge are added to the customer invoice.
    /// ExciseTax may already be embedded upstream — see IsIncludedInPrice.
    /// </summary>
    public TaxType TaxType { get; set; } = TaxType.SalesTax;

    /// <summary>
    /// True when this tax is remitted upstream (manufacturer, importer, or distributor) and
    /// is therefore already embedded in the cost the retailer paid. The tax must NOT be added
    /// to the customer-facing total — doing so would double-count it.
    ///
    /// False (the default) means the retailer collects and remits this tax at point of sale
    /// and it must be added to the customer invoice.
    ///
    /// Examples:
    ///   Illinois state beer excise (distributor-remitted) → IsIncludedInPrice = true
    ///   Illinois retail sales tax (retailer-remitted)     → IsIncludedInPrice = false
    ///   Chicago restaurant occupation tax (retailer)      → IsIncludedInPrice = false
    /// </summary>
    public bool IsIncludedInPrice { get; set; }

    /// <summary>
    /// True when this tax is applied to (price + all other taxes), not just the sale price.
    /// Used for compound/tax-on-tax structures such as Chicago's Matching Tax (0.25% applied
    /// to the total of the transaction price plus all other city and state taxes combined).
    /// When true, the calculation engine must sum all other applicable taxes before applying
    /// this rate. False (the default) means the rate applies to the pre-tax sale price only.
    /// </summary>
    public bool IsCompound { get; set; }

    /// <summary>
    /// Maximum taxable transaction amount for this rate law.
    /// When set, the rate applies only up to this dollar amount per transaction.
    ///
    /// Example — Tennessee single-article cap:
    ///   First $1,600 at the general rate (9.75%), then $1,600–$3,200 at 2.75%,
    ///   then nothing above $3,200. Each bracket is a separate TaxRate row with
    ///   its own MaxTaxableAmount and MinTaxableAmount (stored in Conditions until
    ///   a structured field is added).
    ///
    /// Null = no transaction-level cap; rate applies to the full taxable amount.
    /// </summary>
    public decimal? MaxTaxableAmount { get; set; }

    /// <summary>
    /// True when this rate has a known expiration date that is a sunset provision —
    /// the rate was enacted as temporary legislation and must be re-verified before
    /// ExpirationDate. Distinguishes intentionally temporary rates (e.g., COVID
    /// relief surcharges, infrastructure levies) from rates with a placeholder
    /// expiration date. Drives re-verification workflow alerts.
    /// </summary>
    public bool IsTemporary { get; set; }

    // ── Rate value ────────────────────────────────────────────────────────────
    /// <summary>
    /// Rate value. Interpretation depends on RateBasis:
    ///   Percentage        → decimal fraction of sale price (0.0625 = 6.25%)
    ///   FlatPerUnit       → dollar amount per unit (0.231 = $0.231/pack)
    ///   FlatPerVolume     → dollar amount per volume unit (1.07 = $1.07/gallon)
    ///   FlatPerWeight     → dollar amount per weight unit
    ///   FlatPerProofGallon → dollar amount per proof gallon (distilled spirits)
    /// </summary>
    public decimal Rate { get; set; }

    public RateBasis RateBasis { get; set; } = RateBasis.Percentage;

    /// <summary>Unit label for flat-rate taxes: "per pack", "per gallon", "per gram".</summary>
    public string Unit { get; set; } = string.Empty;

    // ── Applicability conditions ──────────────────────────────────────────────
    /// <summary>Transaction context this rate applies to (on/off-premise, wholesale, etc.).</summary>
    public SaleContext SaleContext { get; set; } = SaleContext.Any;

    /// <summary>
    /// Where in the supply chain this tax is collected and remitted.
    /// Alcohol/tobacco excise taxes are typically collected by the Distributor under the
    /// 3-tier system and are embedded in the wholesale price paid by the retailer.
    /// </summary>
    public RemittancePoint RemittancePoint { get; set; } = RemittancePoint.Retailer;

    /// <summary>
    /// Minimum ABV (as a decimal fraction, e.g. 0.40 = 40%) required for this rate to apply.
    /// Used for ABV-bracketed excise rates (e.g. Chicago spirits &gt;40% ABV = $0.36/liter).
    /// Null = no lower bound.
    /// </summary>
    public decimal? MinAbv { get; set; }

    /// <summary>Maximum ABV (as decimal fraction) above which this rate does NOT apply. Null = no upper bound.</summary>
    public decimal? MaxAbv { get; set; }

    /// <summary>Free-text conditions not captured by the structured fields above.</summary>
    public string Conditions { get; set; } = string.Empty;

    // ── Category ──────────────────────────────────────────────────────────────
    /// <summary>SST product/service category (Goods/Services tree). Null = general rate for all categories.</summary>
    public int? TaxCategoryId { get; set; }

    /// <summary>
    /// Excise product category for taxes that fall outside the SST Goods/Services tree
    /// (alcohol, tobacco, cannabis, fuel, firearms, etc.).
    /// Null for general sales tax and SST-governed items.
    ///
    /// Query path for excise lookups: filter by JurisdictionId + ProductCategory + TaxType
    ///   to get all applicable excise rates for a product at a location.
    /// </summary>
    public ProductCategory? ProductCategory { get; set; }

    // ── Timing ───────────────────────────────────────────────────────────────
    /// <summary>
    /// Date on which this rate became (or becomes) effective.
    /// Null if unknown or not specified by the source document.
    /// Used for historical compliance queries ("what rate applied on 2024-07-15?").
    /// </summary>
    public DateOnly? EffectiveDate { get; set; }

    /// <summary>
    /// Date on which this rate expires. Null = no known expiration (indefinite).
    /// A rate with ExpirationDate in the past should have IsCurrent = false.
    /// </summary>
    public DateOnly? ExpirationDate { get; set; }

    // ── Audit ─────────────────────────────────────────────────────────────────
    /// <summary>Exact text snippet from the source document that proves this rate value.</summary>
    public string RawEvidence { get; set; } = string.Empty;

    public string ScrapedAt { get; set; } = string.Empty;
    public int ScrapeRunId { get; set; }
    public bool IsCurrent { get; set; }

    /// <summary>
    /// True for AI-extracted rates that have not yet been approved by an admin.
    /// The rate is stored but excluded from live lookups until approved.
    /// </summary>
    public bool NeedsReview { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────
    public Jurisdiction Jurisdiction { get; set; } = null!;
    public ScrapeRun ScrapeRun { get; set; } = null!;
    public TaxCategory? TaxCategory { get; set; }

    /// <summary>
    /// Evidence documents proving the veracity of this rate law.
    /// Each file is stored on disk; this collection holds the metadata rows.
    /// </summary>
    public ICollection<SourceDocument> SourceDocuments { get; set; } = [];
}
