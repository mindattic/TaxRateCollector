namespace TaxRateCollector.Core.Interfaces;

/// <summary>
/// Marker interface for canonical document-store entities.
/// Implementations carry a string GUID primary key, a type discriminator,
/// and standard metadata fields (name, description, tags).
/// </summary>
public interface ICanonEntity
{
    string Id { get; set; }
    string Type { get; set; }
    string Name { get; set; }
}
