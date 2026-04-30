using TaxRateCollector.Core.Enums;

namespace TaxRateCollector.Core.Entities;

public class Jurisdiction
{
    public int Id { get; set; }

    /// <summary>Null for top-level Country nodes; points to the parent for State→County→City→District.</summary>
    public int? ParentId { get; set; }

    public JurisdictionType JurisdictionType { get; set; }
    public string JurisdictionName { get; set; } = string.Empty;

    /// <summary>
    /// ISO 3166-1 alpha-2 for countries, FIPS state/county code, or municipal code.
    /// Stable lookup key when querying the tier's source independently.
    /// </summary>
    public string FipsCode { get; set; } = string.Empty;

    /// <summary>Denormalised state abbreviation kept for fast filtering (e.g. "CA", "TX").</summary>
    public string StateCode { get; set; } = string.Empty;

    /// <summary>
    /// Source URL(s) for this jurisdiction's tax rate laws. The first non-empty URL is the
    /// primary source — used by the AI extractor to derive structured rate laws.
    /// Additional URLs (newline-separated) are fetched and attached as supplementary
    /// SourceDocument evidence, letting one rate stack a statute PDF, the live DOR HTML,
    /// and a news article — all hashed and verifiable.
    /// </summary>
    public string SourceUrl { get; set; } = string.Empty;

    /// <summary>
    /// Returns the non-empty URLs from <see cref="SourceUrl"/> in declaration order.
    /// Splits on newlines so the field can hold multiple corroborating sources.
    /// </summary>
    public IReadOnlyList<string> SourceUrls() =>
        SourceUrl.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// True when this local jurisdiction (city or district) administers and collects its own
    /// sales tax independently of the state revenue authority. Sellers must file a separate
    /// return directly with the local authority rather than remitting everything to the state.
    ///
    /// Primarily affects Colorado (home-rule cities: Denver, Boulder, Aurora, etc.),
    /// Alabama (home-rule cities), and Louisiana (home-rule parishes).
    ///
    /// False (the default) means the state collects on behalf of the locality — one combined
    /// return is sufficient.
    /// </summary>
    public bool IsHomeRuleAdministered { get; set; }

    // ── USPS validation ───────────────────────────────────────────────────────
    /// <summary>
    /// True when this jurisdiction's name has been confirmed by the USPS CityStateLookup API.
    /// Set during ZIP import when UspsApiKey is configured in settings.
    /// </summary>
    public bool UspsValidated { get; set; }

    /// <summary>UTC timestamp of the most recent USPS validation. Null if never validated.</summary>
    public DateTime? UspsValidatedAt { get; set; }

    // ── Hierarchy navigation ──────────────────────────────────────────────────
    public Jurisdiction? Parent { get; set; }
    public ICollection<Jurisdiction> Children { get; set; } = new List<Jurisdiction>();

    // ── Rate history and audit ────────────────────────────────────────────────
    public ICollection<TaxRate> TaxRates { get; set; } = new List<TaxRate>();
    public ICollection<ChangeLogEntry> ChangeLogEntries { get; set; } = new List<ChangeLogEntry>();

    /// <summary>ZIP codes that include this jurisdiction as an overlapping special district.</summary>
    public ICollection<ZipCodeDistrict> ZipCodeDistricts { get; set; } = new List<ZipCodeDistrict>();
}
