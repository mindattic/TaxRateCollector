namespace TaxRateCollector.Core.Entities;

/// <summary>
/// Junction table mapping a ZIP code to one or more special taxing district jurisdictions.
/// A single ZIP can be in multiple overlapping districts simultaneously (e.g., a Chicago
/// address may be in both the RTA and MPEA districts, each with their own sales tax levy).
/// </summary>
public class ZipCodeDistrict
{
    public int Id { get; set; }
    public string ZipCode { get; set; } = string.Empty;
    public int JurisdictionId { get; set; }

    public ZipCodeRecord ZipCodeRecord { get; set; } = null!;
    public Jurisdiction Jurisdiction { get; set; } = null!;
}
