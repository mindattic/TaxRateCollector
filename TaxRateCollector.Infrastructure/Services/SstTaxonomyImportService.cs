using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Data;
using TaxRateCollector.Infrastructure.Seeding;
using TaxRateCollector.Infrastructure.Services;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace TaxRateCollector.Infrastructure.Services;

/// <summary>
/// Downloads the SSUTA Agreement PDF, extracts Appendix C defined terms,
/// and builds the TaxCategories hierarchy.
///
/// Strategy:
///   1. Locate pages containing "Appendix C" in the PDF.
///   2. Concatenate their text and split on the "ARTICLE" / section boundaries.
///   3. Match defined terms using the SSUTA pattern:
///         "Term Name" means <definition text ending at next quoted term or section break>
///   4. Map each extracted term to the known SST hierarchy (below) and store
///      the extracted definition as the Description.
///   5. If a term is not found in the PDF, fall back to the known description.
///
/// The hierarchy structure is fixed by the SSUTA and the SST Taxability Matrix.
/// Definitions are extracted from the actual PDF text so they reflect the
/// current amendment (including any changes since the last code update).
/// </summary>
public class SstTaxonomyImportService(
    IDbContextFactory<AppDbContext> dbFactory,
    IHttpClientFactory httpFactory,
    SettingsService settingsSvc,
    ILogger<SstTaxonomyImportService> logger) : ISstTaxonomyImportService
{
    public async Task<bool> IsPopulatedAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.TaxCategories.AnyAsync();
    }

    public async Task<SstImportResult> ImportAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            progress?.Report("Downloading SSUTA PDF…");
            var pdfBytes = await DownloadPdfAsync(ct);

            progress?.Report($"PDF downloaded ({pdfBytes.Length / 1024:N0} KB). Extracting text…");
            var appendixText = ExtractAppendixC(pdfBytes, progress);

            progress?.Report("Parsing defined terms…");
            var definitions = ParseDefinedTerms(appendixText);
            logger.LogInformation("SST import: extracted {Count} defined terms from Appendix C", definitions.Count);

            progress?.Report($"Building taxonomy ({definitions.Count} terms found). Writing to database…");
            var count = await WriteCategoriesAsync(definitions, ct);

            sw.Stop();
            progress?.Report($"Done — {count} categories created in {sw.Elapsed:m\\:ss}.");
            return new SstImportResult(count, true, Elapsed: sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "SST taxonomy import failed");
            return new SstImportResult(0, false, ex.Message, sw.Elapsed);
        }
    }

    // ── PDF download ──────────────────────────────────────────────────────────

    private async Task<byte[]> DownloadPdfAsync(CancellationToken ct)
    {
        var url = settingsSvc.Current.SstAgreementUrl;
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("SST Agreement URL is not configured in Settings.");

        using var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromMinutes(5);
        return await http.GetByteArrayAsync(url, ct);
    }

    // ── Text extraction ───────────────────────────────────────────────────────

    private static string ExtractAppendixC(byte[] pdfBytes, IProgress<string>? progress)
    {
        using var pdf = PdfDocument.Open(pdfBytes);
        var sb = new StringBuilder();
        bool inAppendixC = false;
        int pagesProcessed = 0;

        foreach (Page page in pdf.GetPages())
        {
            var text = page.Text;

            // Detect start of Appendix C
            if (!inAppendixC &&
                (text.Contains("Appendix C", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("APPENDIX C", StringComparison.Ordinal)))
            {
                inAppendixC = true;
            }

            // Stop at Appendix D or beyond
            if (inAppendixC &&
                (text.Contains("Appendix D", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("APPENDIX D", StringComparison.Ordinal)) &&
                pagesProcessed > 2)
            {
                break;
            }

            if (inAppendixC)
            {
                sb.AppendLine(text);
                pagesProcessed++;
                if (pagesProcessed % 10 == 0)
                    progress?.Report($"Extracting Appendix C — page {page.Number} ({pagesProcessed} appx pages)…");
            }
        }

        if (!inAppendixC)
            throw new InvalidOperationException(
                "Could not locate Appendix C in the PDF. " +
                "Verify the SST Agreement URL is correct and points to the full SSUTA document.");

        return sb.ToString();
    }

    // ── Defined-term parser ───────────────────────────────────────────────────

    // Matches patterns like: "Candy" means  OR  "Candy" - (some PDFs use em-dash)
    private static readonly Regex TermPattern = new(
        """[""]([^""]+)[""][""]\s*(means|refers to|is defined as)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Dictionary<string, string> ParseDefinedTerms(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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

    private static string NormalizeTerm(string raw) =>
        Regex.Replace(raw.Trim(), @"\s+", " ");

    private static string CleanDefinition(string raw)
    {
        // Strip page headers/footers, excessive whitespace, and trailing noise
        var cleaned = Regex.Replace(raw, @"\n\s*\d+\s*\n", " ");  // page numbers
        cleaned = Regex.Replace(cleaned, @"\s{3,}", "  ");
        cleaned = cleaned.Trim().TrimEnd('.').Trim();
        if (cleaned.Length > 600) cleaned = cleaned[..600].Trim() + "…";
        return cleaned;
    }

    // ── Database write ────────────────────────────────────────────────────────

    private async Task<int> WriteCategoriesAsync(
        Dictionary<string, string> pdfDefinitions, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var existing = await db.TaxCategories
            .ToDictionaryAsync(c => c.Name, StringComparer.OrdinalIgnoreCase, ct);

        var nameToEntity = new Dictionary<string, TaxCategory>(StringComparer.OrdinalIgnoreCase);

        // First pass: upsert root nodes (no parent)
        foreach (var def in SstTaxonomyData.Definitions.Where(d => d.ParentName == null))
        {
            var description = ResolveDescription(def, pdfDefinitions);
            if (existing.TryGetValue(def.Name, out var found))
            {
                found.Description = description;
                nameToEntity[def.Name] = found;
            }
            else
            {
                var entity = BuildEntity(def, description, parentId: null);
                db.TaxCategories.Add(entity);
                nameToEntity[def.Name] = entity;
            }
        }
        await db.SaveChangesAsync(ct);

        // Subsequent passes: upsert children in dependency order
        var remaining = SstTaxonomyData.Definitions.Where(d => d.ParentName != null).ToList();
        int maxPasses = 5;
        while (remaining.Count > 0 && maxPasses-- > 0)
        {
            var resolved = new List<(TaxCategoryDef Def, TaxCategory Entity)>();
            foreach (var def in remaining)
            {
                if (!nameToEntity.TryGetValue(def.ParentName!, out var parent)) continue;
                if (parent.Id == 0) continue; // parent not yet persisted — defer to next pass
                var description = ResolveDescription(def, pdfDefinitions);
                TaxCategory entity;
                if (existing.TryGetValue(def.Name, out var found))
                {
                    found.Description = description;
                    entity = found;
                }
                else
                {
                    entity = BuildEntity(def, description, parent.Id);
                    db.TaxCategories.Add(entity);
                }
                resolved.Add((def, entity));
            }
            if (resolved.Count == 0) break;
            remaining = remaining.Except(resolved.Select(r => r.Def)).ToList();
            await db.SaveChangesAsync(ct);
            foreach (var (_, entity) in resolved)
                nameToEntity[entity.Name] = entity;
        }

        if (remaining.Count > 0)
            logger.LogWarning("SST import: {Count} categories could not be resolved (parent not found): {Names}",
                remaining.Count, string.Join(", ", remaining.Select(d => d.Name)));

        return nameToEntity.Count;
    }

    private static string ResolveDescription(TaxCategoryDef def, Dictionary<string, string> pdfDefinitions)
    {
        foreach (var key in PdfTermVariants(def.Name))
        {
            if (pdfDefinitions.TryGetValue(key, out var pdfDef))
                return pdfDef;
        }
        return def.KnownDescription;
    }

    private static TaxCategory BuildEntity(TaxCategoryDef def, string description, int? parentId)
    {
        return new TaxCategory
        {
            Name         = def.Name,
            TopLevelType = def.TopLevel,
            IsLeaf       = def.IsLeaf,
            SortOrder    = def.Sort,
            ParentId     = parentId,
            Description  = description,
        };
    }

    // Try exact name, then without parenthetical, then abbreviated forms
    private static IEnumerable<string> PdfTermVariants(string name)
    {
        yield return name;
        var noParens = Regex.Replace(name, @"\s*\([^)]+\)", "").Trim();
        if (noParens != name) yield return noParens;
        // Some SSUTA PDFs use slightly different phrasing
        yield return name.Replace(" (General)", "").Trim();
        yield return name.Replace("&", "and").Trim();
        yield return name.Replace(" (OTC)", "").Trim();
        yield return name.Replace(" (Downloaded)", "").Trim();
        yield return name.Replace(" (Physical Media)", "").Trim();
        yield return name.Replace(" (Unprepared)", "").Trim();
    }
}
