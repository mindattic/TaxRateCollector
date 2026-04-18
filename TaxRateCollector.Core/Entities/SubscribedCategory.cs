namespace TaxRateCollector.Core.Entities;

/// <summary>
/// Each row represents one product category a subscriber has paid access to.
/// Access to a category+state combination requires both a SubscribedState
/// and a SubscribedCategory for that subscriber.
/// </summary>
public class SubscribedCategory
{
    public int Id { get; set; }
    public int SubscriberId { get; set; }
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public string StartDate { get; set; } = "";

    public Subscriber Subscriber { get; set; } = null!;
}
