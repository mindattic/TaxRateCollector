namespace TaxRateCollector.Core.Enums;

public enum LocalTaxAuthorityType
{
    /// <summary>SST member state — all local jurisdictions must follow state taxability definitions exactly.</summary>
    SstUniform,

    /// <summary>Non-SST — local rates piggyback on the state tax base (most non-member states).</summary>
    Piggyback,

    /// <summary>Local jurisdictions can define their own taxability rules (Colorado model).</summary>
    HomeRule,

    /// <summary>Local jurisdictions have significant independent authority (Louisiana parish model).</summary>
    Independent,
}
