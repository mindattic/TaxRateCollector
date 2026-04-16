using TaxRateCollector.Core.Enums;

namespace TaxRateCollector.Core.Entities;

public class Jurisdiction
{
    public int Id { get; set; }

    /// <summary>
    /// Null for top-level Country nodes; points to the parent for State→County→City.
    /// </summary>
    public int? ParentId { get; set; }

    public JurisdictionType JurisdictionType { get; set; }
    public string JurisdictionName { get; set; } = string.Empty;

    /// <summary>
    /// ISO 3166-1 alpha-2 for countries, FIPS state/county code, or municipal code.
    /// Used as the stable lookup key when querying the tier's source independently.
    /// </summary>
    public string FipsCode { get; set; } = string.Empty;

    /// <summary>
    /// Denormalised state abbreviation kept for fast filtering (e.g. "CA", "TX").
    /// </summary>
    public string StateCode { get; set; } = string.Empty;

    /// <summary>
    /// Root URL of the data source for this jurisdiction's tier.
    /// Each tier may point at a different API endpoint or document repository.
    /// </summary>
    public string SourceUrl { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    // Hierarchy navigation
    public Jurisdiction? Parent { get; set; }
    public ICollection<Jurisdiction> Children { get; set; } = new List<Jurisdiction>();

    // Rate history and audit
    public ICollection<TaxRate> TaxRates { get; set; } = new List<TaxRate>();
    public ICollection<ChangeLogEntry> ChangeLogEntries { get; set; } = new List<ChangeLogEntry>();
}
