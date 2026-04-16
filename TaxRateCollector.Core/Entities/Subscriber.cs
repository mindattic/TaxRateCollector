namespace TaxRateCollector.Core.Entities;

/// <summary>
/// Links an Identity user to their subscription profile and billing address.
/// Address fields are used to calculate sales tax on the subscription itself
/// using TaxRateCollector's own jurisdiction data.
/// </summary>
public class Subscriber
{
    public int Id { get; set; }
    public string UserId { get; set; } = ""; // FK → AspNetUsers.Id
    public string FullName { get; set; } = "";
    public string AddressLine1 { get; set; } = "";
    public string City { get; set; } = "";
    public string StateCode { get; set; } = ""; // subscriber's billing state (for tax)
    public string ZipCode { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public string CreatedAt { get; set; } = "";

    public ICollection<SubscribedState> SubscribedStates { get; set; } = [];
    public ICollection<BillingRecord> BillingRecords { get; set; } = [];
}
