namespace TaxRateCollector.Core.Entities;

/// <summary>
/// Each row represents one state a subscriber has paid access to.
/// Soft-delete via IsActive — billing history is never destroyed.
/// </summary>
public class SubscribedState
{
    public int Id { get; set; }
    public int SubscriberId { get; set; }
    public string StateCode { get; set; } = "";
    public string StateName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public string StartDate { get; set; } = "";

    public Subscriber Subscriber { get; set; } = null!;
}
