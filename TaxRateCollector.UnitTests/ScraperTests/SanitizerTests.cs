using TaxRateCollector.Infrastructure.Scrapers;

namespace TaxRateCollector.UnitTests.ScraperTests;

[TestFixture]
public class SanitizerTests
{
    [TestCase("6.25%",   0.0625)]
    [TestCase("10.25%",  0.1025)]
    [TestCase("0.0625",  0.0625)]
    [TestCase("  8.25%  ", 0.0825)]
    [TestCase("0.10",    0.10)]
    public void Parse_ValidInputs_ReturnsCorrectDecimal(string raw, decimal expected)
    {
        var result = RateSanitizer.Parse(raw);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value, Is.EqualTo(expected).Within(1e-6m));
    }

    [TestCase("N/A")]
    [TestCase("")]
    [TestCase("  ")]
    [TestCase("0%")]
    [TestCase("25%")]   // > 20% ceiling
    [TestCase("abc")]
    [TestCase(null)]
    public void Parse_InvalidInputs_ReturnsNull(string? raw)
    {
        Assert.That(RateSanitizer.Parse(raw!), Is.Null);
    }

    [Test]
    public void Parse_NegativeRate_ReturnsNull()
    {
        Assert.That(RateSanitizer.Parse("-1%"), Is.Null);
    }
}
