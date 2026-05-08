namespace TaxRateCollector.Blazor.Services;

/// <summary>
/// Identity the UI is currently rendering as. <see cref="Actual"/> reflects the
/// signed-in user; the other values are admin-only impersonation modes used to
/// preview the subscriber and guest experience.
/// </summary>
public enum ViewAsRole { Actual, Subscriber, Guest }

/// <summary>
/// Singleton that tracks the current "view as" identity for the admin's session
/// and broadcasts changes so layout components can re-render. Also owns the
/// hard-coded coastal-state allowlist used to fake a paid subscription when an
/// admin previews the subscriber experience.
/// </summary>
public class ViewAsService
{
    /// <summary>
    /// States the admin's "view as Subscriber" mode pretends to be paying for —
    /// the entire west and east coasts, used as a representative slice during demos.
    /// </summary>
    public static readonly IReadOnlySet<string> DemoSubscribedStateCodes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // West coast
            "WA", "OR", "CA", "AK", "HI",
            // East coast
            "ME", "NH", "VT", "MA", "RI", "CT", "NY", "NJ", "DE", "MD", "VA", "NC", "SC", "GA", "FL"
        };

    /// <summary>Raised when <see cref="Role"/> changes so dependent components can refresh.</summary>
    public event Action? RoleChanged;

    private ViewAsRole role = ViewAsRole.Actual;
    /// <summary>
    /// The role the UI is currently rendering as. Setting this fires
    /// <see cref="RoleChanged"/>.
    /// </summary>
    public ViewAsRole Role
    {
        get => role;
        set { role = value; RoleChanged?.Invoke(); }
    }

    /// <summary>
    /// Returns true only when the actual user is an admin AND they are not
    /// currently impersonating Subscriber / Guest. Impersonation drops admin
    /// privileges so previews are faithful.
    /// </summary>
    public bool EffectiveIsAdmin(bool actualIsAdmin) =>
        actualIsAdmin && role == ViewAsRole.Actual;
}
