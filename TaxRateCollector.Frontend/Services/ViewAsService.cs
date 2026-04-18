namespace TaxRateCollector.Frontend.Services;

public enum ViewAsRole { Actual, Subscriber, Guest }

public class ViewAsService
{
    public ViewAsRole Role { get; set; } = ViewAsRole.Actual;

    public bool EffectiveIsAdmin(bool actualIsAdmin) =>
        actualIsAdmin && Role == ViewAsRole.Actual;
}
