namespace TaxRateCollector.Blazor.Services;

public enum ViewAsRole { Actual, Subscriber, Guest }

public class ViewAsService
{
    public static readonly IReadOnlySet<string> DemoSubscribedStateCodes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // West coast
            "WA", "OR", "CA", "AK", "HI",
            // East coast
            "ME", "NH", "VT", "MA", "RI", "CT", "NY", "NJ", "DE", "MD", "VA", "NC", "SC", "GA", "FL"
        };

    public event Action? RoleChanged;

    private ViewAsRole role = ViewAsRole.Actual;
    public ViewAsRole Role
    {
        get => role;
        set { role = value; RoleChanged?.Invoke(); }
    }

    public bool EffectiveIsAdmin(bool actualIsAdmin) =>
        actualIsAdmin && role == ViewAsRole.Actual;
}
