namespace TaxRateCollector.Core.Enums;

/// <summary>
/// Indicates how trustworthy the source of a tax rate value is.
/// Drives UI color-coding and downstream data-quality warnings.
/// </summary>
public enum SourceConfidence
{
    /// <summary>Rate was fetched directly from an official government URL.</summary>
    Official   = 0,

    /// <summary>Rate was fetched from the Wayback Machine (the live government URL was unreachable).</summary>
    Archive    = 1,

    /// <summary>Rate came from a third-party aggregator or secondary source, not a government page.</summary>
    ThirdParty = 2,

    /// <summary>Source has not been verified or is unknown.</summary>
    Unverified = 3,
}
