using TaxRateCollector.Infrastructure.Seeding;
using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.UnitTests.SetupTests;

[TestFixture]
public class SstDefinitionParserTests
{
    // ── NormalizeTerm ─────────────────────────────────────────────────────────

    [Test]
    public void NormalizeTerm_TrimsEdgeWhitespace()
        => Assert.That(SstDefinitionParser.NormalizeTerm("  Candy  "), Is.EqualTo("Candy"));

    [Test]
    public void NormalizeTerm_CollapsesInternalSpaces()
        => Assert.That(SstDefinitionParser.NormalizeTerm("Food   and   Drink"), Is.EqualTo("Food and Drink"));

    [Test]
    public void NormalizeTerm_TabsCollapsed()
        => Assert.That(SstDefinitionParser.NormalizeTerm("Candy\t\tBars"), Is.EqualTo("Candy Bars"));

    [Test]
    public void NormalizeTerm_NoChange_WhenAlreadyClean()
        => Assert.That(SstDefinitionParser.NormalizeTerm("Candy"), Is.EqualTo("Candy"));

    // ── CleanDefinition ───────────────────────────────────────────────────────

    [Test]
    public void CleanDefinition_StripsLeadingAndTrailingWhitespace()
    {
        var result = SstDefinitionParser.CleanDefinition("   some definition   ");
        Assert.That(result, Is.EqualTo("some definition"));
    }

    [Test]
    public void CleanDefinition_StripsTrailingPeriod()
    {
        var result = SstDefinitionParser.CleanDefinition("a taxable item.");
        Assert.That(result, Does.Not.EndWith("."));
    }

    [Test]
    public void CleanDefinition_CollapsesExcessiveWhitespace()
    {
        var result = SstDefinitionParser.CleanDefinition("word   gap   here");
        Assert.That(result, Does.Not.Contain("   "));
    }

    [Test]
    public void CleanDefinition_StripsPageNumbers()
    {
        var result = SstDefinitionParser.CleanDefinition("before\n 42 \nafter");
        Assert.That(result, Does.Not.Match(@"\n\s*\d+\s*\n"));
        Assert.That(result, Does.Contain("before"));
        Assert.That(result, Does.Contain("after"));
    }

    [Test]
    public void CleanDefinition_TruncatesAt600Chars()
    {
        var longText = new string('x', 700);
        var result = SstDefinitionParser.CleanDefinition(longText);
        Assert.That(result, Has.Length.LessThanOrEqualTo(601)); // 600 + "…"
        Assert.That(result, Does.EndWith("…"));
    }

    [Test]
    public void CleanDefinition_ShortText_NotTruncated()
    {
        const string text = "short definition text";
        var result = SstDefinitionParser.CleanDefinition(text);
        Assert.That(result, Does.Not.EndWith("…"));
        Assert.That(result, Is.EqualTo(text));
    }

    // ── ParseDefinedTerms ─────────────────────────────────────────────────────

    private static string TermBlock(string term, string definition, string connector = "means")
        => $"\"{term}\" {connector} {definition}";

    [Test]
    public void ParseDefinedTerms_SingleMeansTerm_Parsed()
    {
        var text = TermBlock("Candy", "a preparation of sugar, honey, or other natural or artificial sweeteners combined with chocolate, fruits, nuts, or other ingredients in the form of bars, drops, or pieces.");
        var result = SstDefinitionParser.ParseDefinedTerms(text);

        Assert.That(result, Does.ContainKey("Candy"));
    }

    [Test]
    public void ParseDefinedTerms_RefersToConnector_Parsed()
    {
        var text = TermBlock("Bundled Transaction", "a taxable item sold with another item for a single non-itemized price.", "refers to");
        var result = SstDefinitionParser.ParseDefinedTerms(text);

        Assert.That(result, Does.ContainKey("Bundled Transaction"));
    }

    [Test]
    public void ParseDefinedTerms_IsDefinedAsConnector_Parsed()
    {
        var text = TermBlock("Nexus", "a substantial connection between a seller and a state requiring tax collection.", "is defined as");
        var result = SstDefinitionParser.ParseDefinedTerms(text);

        Assert.That(result, Does.ContainKey("Nexus"));
    }

    [Test]
    public void ParseDefinedTerms_MultipleTerms_AllParsed()
    {
        var text = TermBlock("Candy", "a preparation of sugar or natural sweeteners combined with chocolate, fruits, or other ingredients.")
                 + "\n\n"
                 + TermBlock("Food", "substances consumed for nutritional value, including liquids, concentrated, frozen, or dried items.");
        var result = SstDefinitionParser.ParseDefinedTerms(text);

        Assert.That(result.Keys, Is.SupersetOf(new[] { "Candy", "Food" }));
    }

    [Test]
    public void ParseDefinedTerms_MultipleTerms_EachDefinitionBoundedByNextTerm()
    {
        var text = TermBlock("Candy", "sugar-based confection.")
                 + " Extra candy text. "
                 + TermBlock("Food", "anything edible for human consumption.");
        var result = SstDefinitionParser.ParseDefinedTerms(text);

        // Candy's definition should NOT bleed into Food's definition
        Assert.That(result["Candy"], Does.Not.Contain("anything edible"));
    }

    [Test]
    public void ParseDefinedTerms_ShortDefinition_Skipped()
    {
        // "means X" where X is only a few chars — too short to be a real definition
        var text = "\u201cTerm\u201d means short";
        var result = SstDefinitionParser.ParseDefinedTerms(text);

        Assert.That(result, Does.Not.ContainKey("Term"),
            "Definitions under 10 characters should be excluded");
    }

    [Test]
    public void ParseDefinedTerms_EmptyText_ReturnsEmpty()
        => Assert.That(SstDefinitionParser.ParseDefinedTerms(""), Is.Empty);

    [Test]
    public void ParseDefinedTerms_NoMatchingPattern_ReturnsEmpty()
        => Assert.That(SstDefinitionParser.ParseDefinedTerms("This is just plain text with no defined terms."), Is.Empty);

    [Test]
    public void ParseDefinedTerms_IsCaseInsensitiveForTermNames()
    {
        var text = TermBlock("CANDY", "a preparation of sugar or other sweeteners combined with chocolate or fruit.");
        var result = SstDefinitionParser.ParseDefinedTerms(text);

        Assert.That(result.ContainsKey("candy"), Is.True,
            "Term lookup should be case-insensitive");
    }

    // ── PdfTermVariants ───────────────────────────────────────────────────────

    [Test]
    public void PdfTermVariants_PlainName_YieldsItself()
    {
        var variants = SstDefinitionParser.PdfTermVariants("Candy").ToList();
        Assert.That(variants, Contains.Item("Candy"));
    }

    [Test]
    public void PdfTermVariants_NameWithParenthetical_YieldsStrippedVariant()
    {
        var variants = SstDefinitionParser.PdfTermVariants("Food (General)").ToList();
        Assert.That(variants, Contains.Item("Food"));
    }

    [Test]
    public void PdfTermVariants_NameWithGeneralSuffix_YieldsVariantWithoutGeneral()
    {
        var variants = SstDefinitionParser.PdfTermVariants("Food (General)").ToList();
        Assert.That(variants, Contains.Item("Food"));
    }

    [Test]
    public void PdfTermVariants_NameWithAmpersand_YieldsAndVariant()
    {
        var variants = SstDefinitionParser.PdfTermVariants("Food & Drink").ToList();
        Assert.That(variants, Contains.Item("Food and Drink"));
    }

    [Test]
    public void PdfTermVariants_NameWithOtcSuffix_YieldsStripped()
    {
        var variants = SstDefinitionParser.PdfTermVariants("Drugs (OTC)").ToList();
        Assert.That(variants, Contains.Item("Drugs"));
    }

    [Test]
    public void PdfTermVariants_NameWithDownloadedSuffix_YieldsStripped()
    {
        var variants = SstDefinitionParser.PdfTermVariants("Software (Downloaded)").ToList();
        Assert.That(variants, Contains.Item("Software"));
    }

    [Test]
    public void PdfTermVariants_NameWithPhysicalMediaSuffix_YieldsStripped()
    {
        var variants = SstDefinitionParser.PdfTermVariants("Software (Physical Media)").ToList();
        Assert.That(variants, Contains.Item("Software"));
    }

    [Test]
    public void PdfTermVariants_NameWithUnpreparedSuffix_YieldsStripped()
    {
        var variants = SstDefinitionParser.PdfTermVariants("Food (Unprepared)").ToList();
        Assert.That(variants, Contains.Item("Food"));
    }

    // ── ResolveDescription ────────────────────────────────────────────────────

    [Test]
    public void ResolveDescription_ExactMatch_ReturnsPdfDefinition()
    {
        var def = new TaxCategoryDef("Candy", "Goods", true, 1, "Goods", "Fallback description.");
        var pdf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Candy"] = "PDF-extracted: a preparation of sugar combined with chocolate."
        };

        var result = SstDefinitionParser.ResolveDescription(def, pdf);

        Assert.That(result, Is.EqualTo("PDF-extracted: a preparation of sugar combined with chocolate."));
    }

    [Test]
    public void ResolveDescription_NoMatch_ReturnsFallback()
    {
        var def = new TaxCategoryDef("Candy", "Goods", true, 1, "Goods", "Fallback description.");
        var pdf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var result = SstDefinitionParser.ResolveDescription(def, pdf);

        Assert.That(result, Is.EqualTo("Fallback description."));
    }

    [Test]
    public void ResolveDescription_VariantMatch_ReturnsPdfDefinition()
    {
        var def = new TaxCategoryDef("Food (General)", "Goods", false, 2, "Goods", "Fallback.");
        var pdf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Food"] = "PDF definition for food items."
        };

        var result = SstDefinitionParser.ResolveDescription(def, pdf);

        Assert.That(result, Is.EqualTo("PDF definition for food items."),
            "Should find the definition via the stripped parenthetical variant");
    }
}
