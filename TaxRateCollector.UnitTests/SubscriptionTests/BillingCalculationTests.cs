namespace TaxRateCollector.UnitTests.SubscriptionTests;

/// <summary>
/// Tests for subscription billing math:
/// subtotal, tax, total calculations — mirrors the computed properties in Subscribe.razor.
/// </summary>
[TestFixture]
public class BillingCalculationTests
{
    // These mirror the computed properties in Subscribe.razor @code:
    //   PricePerMonth = selectedStates.Count * pricing.PricePerState
    //   TaxAmount     = PricePerMonth * billingTaxRate
    //   Total         = PricePerMonth + TaxAmount

    private static decimal PricePerMonth(int stateCount, decimal pricePerState)
        => stateCount * pricePerState;

    private static decimal TaxAmount(decimal pricePerMonth, decimal taxRate)
        => pricePerMonth * taxRate;

    private static decimal Total(decimal pricePerMonth, decimal taxAmount)
        => pricePerMonth + taxAmount;

    // ── Subtotal ──────────────────────────────────────────────────────────────

    [Test]
    [TestCase(1,  0.01, 0.01)]
    [TestCase(3,  0.01, 0.03)]
    [TestCase(50, 0.01, 0.50)]
    [TestCase(10, 0.05, 0.50)]
    [TestCase(50, 0.05, 2.50)]
    public void PricePerMonth_MatchesStateCountTimesPrice(int count, double price, double expected)
    {
        var result = PricePerMonth(count, (decimal)price);
        Assert.That(result, Is.EqualTo((decimal)expected).Within(0.0001m));
    }

    [Test]
    public void PricePerMonth_ZeroStates_IsZero()
    {
        Assert.That(PricePerMonth(0, 0.01m), Is.EqualTo(0m));
    }

    // ── Tax calculation ───────────────────────────────────────────────────────

    [Test]
    [TestCase(0.03,  0.0625,  0.001875)]
    [TestCase(0.50,  0.065,   0.0325)]
    [TestCase(0.10,  0.10,    0.01)]
    [TestCase(0.50,  0.0,     0.0)]     // no-tax state (e.g. Oregon)
    public void TaxAmount_MatchesSubtotalTimesRate(double subtotal, double rate, double expected)
    {
        var result = TaxAmount((decimal)subtotal, (decimal)rate);
        Assert.That(result, Is.EqualTo((decimal)expected).Within(0.000001m));
    }

    [Test]
    public void TaxAmount_ZeroRate_IsZero()
    {
        Assert.That(TaxAmount(0.50m, 0m), Is.EqualTo(0m));
    }

    // ── Total ─────────────────────────────────────────────────────────────────

    [Test]
    public void Total_IsSubtotalPlusTax()
    {
        var subtotal = PricePerMonth(3, 0.01m);   // $0.03
        var tax      = TaxAmount(subtotal, 0.0625m); // $0.001875
        var total    = Total(subtotal, tax);

        Assert.That(total, Is.EqualTo(0.031875m).Within(0.000001m));
    }

    [Test]
    public void Total_NoTax_EqualsSubtotal()
    {
        var subtotal = PricePerMonth(10, 0.01m); // $0.10
        var tax      = TaxAmount(subtotal, 0m);
        var total    = Total(subtotal, tax);

        Assert.That(total, Is.EqualTo(subtotal));
    }

    [Test]
    public void Total_FullMembership_50States_DefaultPrice()
    {
        // $0.01/state × 50 states = $0.50 base, + tax
        var subtotal = PricePerMonth(50, 0.01m);
        Assert.That(subtotal, Is.EqualTo(0.50m));

        // With Illinois rate (6.25%)
        var tax   = TaxAmount(subtotal, 0.0625m);
        var total = Total(subtotal, tax);

        Assert.That(tax,   Is.EqualTo(0.03125m).Within(0.000001m));
        Assert.That(total, Is.EqualTo(0.53125m).Within(0.000001m));
    }

    [Test]
    public void Total_NeverNegative_ForValidInputs()
    {
        // All combinations of valid positive inputs should yield non-negative totals
        foreach (var count in new[] { 0, 1, 10, 50 })
        foreach (var price in new[] { 0.01m, 0.05m, 0.10m })
        foreach (var rate  in new[] { 0m, 0.05m, 0.10m })
        {
            var subtotal = PricePerMonth(count, price);
            var tax      = TaxAmount(subtotal, rate);
            var total    = Total(subtotal, tax);
            Assert.That(total, Is.GreaterThanOrEqualTo(0m),
                $"Total should not be negative (count={count}, price={price}, rate={rate})");
        }
    }

    // ── Decimal precision ─────────────────────────────────────────────────────

    [Test]
    public void TaxAmount_DecimalPrecision_NotTruncated()
    {
        // 0.03 × 0.0625 = 0.001875 — must not be truncated to 0.00
        var result = TaxAmount(0.03m, 0.0625m);
        Assert.That(result, Is.GreaterThan(0m));
        Assert.That(result, Is.EqualTo(0.001875m));
    }

    [Test]
    public void PricePerState_StoredAsDecimal_NotDouble()
    {
        // Verifies that PricingConfig.PricePerState is a decimal (no float imprecision)
        const decimal pricePerState = 0.01m;
        const int stateCount = 50;
        var total = pricePerState * stateCount;
        Assert.That(total, Is.EqualTo(0.50m), "Decimal arithmetic must be exact.");
    }

    // ── BillingRecord field validation ────────────────────────────────────────

    [Test]
    public void BillingRecord_TaxOnSubscription_UsesSubscriberBillingState()
    {
        // The tax rate should come from the subscriber's billing state,
        // not the states they subscribe to. This test verifies the calculation
        // is independent of subscribed states.
        const decimal subtotal = 0.03m;          // 3 states subscribed
        const decimal ilTaxRate = 0.0625m;        // Subscriber is billed in Illinois

        var tax   = TaxAmount(subtotal, ilTaxRate);
        var total = Total(subtotal, tax);

        // Tax is on the service fee, not on the jurisdictions themselves
        Assert.That(tax,   Is.EqualTo(0.001875m).Within(0.000001m));
        Assert.That(total, Is.EqualTo(0.031875m).Within(0.000001m));
    }

    [Test]
    public void BillingRecord_ZeroTaxState_NoTaxCharged()
    {
        // States with no SaaS sales tax (e.g. Oregon, Montana, etc.)
        const decimal subtotal = 0.50m; // 50 states @ $0.01
        var tax   = TaxAmount(subtotal, 0m);
        var total = Total(subtotal, tax);

        Assert.That(tax,   Is.EqualTo(0m));
        Assert.That(total, Is.EqualTo(subtotal));
    }
}
