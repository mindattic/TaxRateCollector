using System.Text.RegularExpressions;
using TaxRateCollector.Infrastructure.Seeding;

namespace TaxRateCollector.Infrastructure.Services;

internal static class SstDefinitionParser
{
    // Matches: "Term Name" means  /  "Term Name" refers to  /  "Term Name" is defined as
    // Handles ASCII quotes, typographic curly quotes (U+201C/D), and PDFs that double
    // the closing quote character.
    private static readonly Regex TermPattern = new(
        @"[\u201c\u201d""]([^\u201c\u201d""]+)[\u201c\u201d""][\u201c\u201d""]?\s*(means|refers to|is defined as)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static Dictionary<string, string> ParseDefinedTerms(string text)
    {
        var result  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var matches = TermPattern.Matches(text);

        for (int i = 0; i < matches.Count; i++)
        {
            var termName = NormalizeTerm(matches[i].Groups[1].Value);
            var defStart = matches[i].Index + matches[i].Length;
            var defEnd   = i + 1 < matches.Count ? matches[i + 1].Index : Math.Min(defStart + 1200, text.Length);
            var definition = CleanDefinition(text[defStart..defEnd]);

            if (!string.IsNullOrWhiteSpace(termName) && definition.Length > 10)
                result[termName] = definition;
        }

        return result;
    }

    internal static string NormalizeTerm(string raw) =>
        Regex.Replace(raw.Trim(), @"\s+", " ");

    internal static string CleanDefinition(string raw)
    {
        var cleaned = Regex.Replace(raw, @"\n\s*\d+\s*\n", " ");  // strip page numbers
        cleaned = Regex.Replace(cleaned, @"\s{3,}", "  ");
        cleaned = cleaned.Trim().TrimEnd('.').Trim();
        if (cleaned.Length > 600) cleaned = cleaned[..600].Trim() + "…";
        return cleaned;
    }

    internal static IEnumerable<string> PdfTermVariants(string name)
    {
        yield return name;
        var noParens = Regex.Replace(name, @"\s*\([^)]+\)", "").Trim();
        if (noParens != name) yield return noParens;
        yield return name.Replace(" (General)", "").Trim();
        yield return name.Replace("&", "and").Trim();
        yield return name.Replace(" (OTC)", "").Trim();
        yield return name.Replace(" (Downloaded)", "").Trim();
        yield return name.Replace(" (Physical Media)", "").Trim();
        yield return name.Replace(" (Unprepared)", "").Trim();
    }

    internal static string ResolveDescription(TaxCategoryDef def, Dictionary<string, string> pdfDefinitions)
    {
        foreach (var key in PdfTermVariants(def.Name))
        {
            if (pdfDefinitions.TryGetValue(key, out var pdfDef))
                return pdfDef;
        }
        return def.KnownDescription;
    }
}
