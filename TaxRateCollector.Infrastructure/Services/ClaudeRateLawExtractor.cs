using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MindAttic.Legion;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Core.Options;

namespace TaxRateCollector.Infrastructure.Services;

/// <summary>
/// Uses the Claude API (claude-sonnet-4-6) to read raw source content from a government
/// tax authority page and extract structured <see cref="ExtractedRateLaw"/> records.
///
/// Each extracted rate comes back with a confidence score (0–1) and a RawEvidence
/// snippet — the exact text that proves the rate — stored as the <see cref="TaxRate.RawEvidence"/>.
/// All AI-extracted rates are flagged NeedsReview = true until an admin approves them.
/// </summary>
public sealed class ClaudeRateLawExtractor(
    LegionClient legion,
    IOptions<AnthropicOptions> options,
    SettingsService settings,
    ILogger<ClaudeRateLawExtractor> logger) : IRateLawExtractor
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
    };

    public async Task<IReadOnlyList<ExtractedRateLaw>> ExtractAsync(
        Jurisdiction jurisdiction,
        string content,
        string mimeType,
        string sourceUrl,
        CancellationToken ct = default)
    {
        var opts = options.Value;

        // Key is loaded by SettingsService from %APPDATA%\MindAttic\LLM\providers.json
        // (the shared MindAttic.Legion credential store) with a fallback to this app's
        // settings.json — see MindAttic.Legion.MindAtticCredentialStore.
        var apiKey = settings.Current.AnthropicApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("Anthropic API key not set in AppData settings — skipping extraction for {Name}",
                jurisdiction.JurisdictionName);
            return [];
        }

        var cleaned = CleanContent(content, mimeType, opts.MaxContentChars);
        if (string.IsNullOrWhiteSpace(cleaned))
            return [];

        var prompt = BuildPrompt(jurisdiction, sourceUrl, cleaned);

        try
        {
            var responseText = await CallClaudeAsync(prompt, apiKey, opts, ct);
            return ParseResponse(responseText, jurisdiction.JurisdictionName);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Claude extraction failed for {Name}", jurisdiction.JurisdictionName);
            return [];
        }
    }

    // ── Prompt ────────────────────────────────────────────────────────────────

    private static string BuildPrompt(Jurisdiction jurisdiction, string sourceUrl, string content)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a US tax law data extraction assistant.");
        sb.AppendLine();
        sb.AppendLine("Extract all tax rate laws from the government source content below.");
        sb.AppendLine("Return ONLY a valid JSON array — no markdown, no explanation.");
        sb.AppendLine();
        sb.AppendLine("Each element must match this schema exactly:");
        sb.AppendLine("""
{
  "Name": "Human-readable name, e.g. 'General Sales Tax' or 'Beer Excise ≤3.2% ABV'",
  "Rate": 0.065,
  "Basis": "Percentage|FlatPerUnit|FlatPerVolume|FlatPerWeight|FlatPerProofGallon|PercentageOfWholesale",
  "Unit": "per gallon",
  "TaxType": "SalesTax|UseTax|ExciseTax|OccupancyTax|RentalSurcharge",
  "ProductCategory": null,
  "SaleContext": "Any|OnPremise|OffPremise|Wholesale|DirectToConsumer",
  "RemittancePoint": "Manufacturer|Importer|Distributor|Retailer|Consumer",
  "MinAbv": null,
  "MaxAbv": null,
  "Conditions": "",
  "StatutoryReference": "e.g. '35 ILCS 120/2'",
  "EffectiveDate": "2024-01-01",
  "ExpirationDate": "",
  "TaxCategoryId": null,
  "Confidence": 0.95,
  "RawEvidence": "exact text snippet from source proving this rate",
  "IsCompound": false,
  "MinTaxableAmount": null,
  "MaxTaxableAmount": null,
  "FlatCapPerUnit": null,
  "IsTemporary": false,
  "IsRecurring": false,
  "AdjustmentFrequency": "Static",
  "AdjustmentMechanism": null
}
""");
        sb.AppendLine("Rules:");
        sb.AppendLine("- Rate: decimal fraction for percentages (6.5% → 0.065); dollar amount for flat rates ($0.231/pack → 0.231)");
        sb.AppendLine("- TaxType: SalesTax for general sales tax; ExciseTax for alcohol/tobacco/fuel/cannabis/firearms; OccupancyTax for hotel/lodging; RentalSurcharge for rental cars");
        sb.AppendLine("- ProductCategory: set for excise taxes — one of: Alcohol, Beer, Wine, Spirits, Tobacco, Cigarettes, Cannabis, Sugar, SoftDrinks, Firearms, Ammunition, Fuel, Hotel, RentalCar, Lottery, Gaming. Null for sales/use tax.");
        sb.AppendLine("- RemittancePoint: Distributor or Manufacturer for state alcohol/tobacco excise; Retailer for sales tax and restaurant-level taxes");
        sb.AppendLine("- Basis: PercentageOfWholesale for OTP tobacco/cigar taxes assessed on wholesale or manufacturer price; Percentage for rates on retail price");
        sb.AppendLine("- RemittancePoint: MarketplaceFacilitator when a platform (Amazon, Etsy, eBay) is required by law to collect and remit on behalf of third-party sellers");
        sb.AppendLine("- IsCompound: true only when the source explicitly states the tax applies to (price + all other taxes), not just the sale price (e.g. Chicago Matching Tax)");
        sb.AppendLine("- MinTaxableAmount: lower bound dollar threshold for tiered bracket rates (e.g. TN single-article: middle tier starts at $1,600). Null if rate applies from $0.");
        sb.AppendLine("- MaxTaxableAmount: upper bound dollar threshold per transaction (e.g. TN single-article cap at $1,600 for tier 1). Null if no cap.");
        sb.AppendLine("- FlatCapPerUnit: dollar cap per unit for 'lesser of percentage or flat' structures (e.g. federal cigar: 52.75% capped at $0.4026/unit). Null if no per-unit cap.");
        sb.AppendLine("- IsTemporary: true if the source states a sunset date or refers to the rate as temporary, expiring, or subject to legislative renewal");
        sb.AppendLine("- IsRecurring: true for annual tax holidays (back-to-school, hurricane prep) where the exemption repeats each year on the same approximate schedule");
        sb.AppendLine("- AdjustmentFrequency: Static (fixed rate) | Annual (recalculates yearly, e.g. IL fuel CPI) | Quarterly (e.g. VA fuel avg wholesale) | Monthly (rare)");
        sb.AppendLine("- AdjustmentMechanism: describe the indexing formula when AdjustmentFrequency is not Static (e.g. 'CPI-indexed, recalculated July 1 per Public Act 101-0032')");
        sb.AppendLine("- One object per ABV bracket; one object per SaleContext if rates differ");
        sb.AppendLine("- Confidence: 1.0 = explicit table cell; 0.8 = clearly stated in prose; 0.6 = inferred");
        sb.AppendLine("- TaxCategoryId: always null (categories are assigned during human review)");
        sb.AppendLine("- Return [] if no rates can be confidently identified");
        sb.AppendLine();
        sb.AppendLine($"Jurisdiction: {jurisdiction.JurisdictionType} — {jurisdiction.JurisdictionName}, {jurisdiction.StateCode}");
        sb.AppendLine($"Source URL: {sourceUrl}");
        sb.AppendLine();
        sb.AppendLine("=== SOURCE CONTENT ===");
        sb.AppendLine(content);
        sb.AppendLine("=== END ===");

        return sb.ToString();
    }

    // ── API call ──────────────────────────────────────────────────────────────

    private Task<string> CallClaudeAsync(
        string prompt, string apiKey, AnthropicOptions opts, CancellationToken ct)
        => legion.CallAsync(
            providerId: "claude-api",
            apiKey: apiKey,
            model: opts.Model,
            systemPrompt: "",
            userMessage: prompt,
            maxTokens: opts.MaxTokens,
            temperature: 0.0,
            ct: ct);

    // ── Response parsing ──────────────────────────────────────────────────────

    private IReadOnlyList<ExtractedRateLaw> ParseResponse(string text, string jurisdictionName)
    {
        var json = Regex.Replace(text.Trim(), @"^```(?:json)?\s*|\s*```$", "", RegexOptions.Multiline).Trim();

        if (string.IsNullOrWhiteSpace(json) || json == "[]")
            return [];

        try
        {
            var dtos = JsonSerializer.Deserialize<List<ExtractedRateLawDto>>(json, JsonOpts);
            if (dtos is null) return [];

            return dtos
                .Where(d => d.Confidence >= 0.5f)
                .Select(d => new ExtractedRateLaw(
                    Name:               d.Name,
                    Rate:               d.Rate,
                    Basis:              ParseEnum(d.Basis, RateBasis.Percentage),
                    Unit:               d.Unit,
                    SaleContext:        ParseEnum(d.SaleContext, SaleContext.Any),
                    RemittancePoint:    ParseEnum(d.RemittancePoint, RemittancePoint.Retailer),
                    MinAbv:             d.MinAbv,
                    MaxAbv:             d.MaxAbv,
                    Conditions:         d.Conditions,
                    StatutoryReference: d.StatutoryReference,
                    EffectiveDate:      d.EffectiveDate,
                    ExpirationDate:     d.ExpirationDate,
                    TaxCategoryId:      null,
                    Confidence:         d.Confidence,
                    RawEvidence:        d.RawEvidence,
                    TaxType:              ParseEnum(d.TaxType, TaxType.SalesTax),
                    ProductCategory:      ParseNullableEnum<ProductCategory>(d.ProductCategory),
                    IsCompound:           d.IsCompound,
                    MinTaxableAmount:     d.MinTaxableAmount,
                    MaxTaxableAmount:     d.MaxTaxableAmount,
                    FlatCapPerUnit:       d.FlatCapPerUnit,
                    IsTemporary:          d.IsTemporary,
                    IsRecurring:          d.IsRecurring,
                    AdjustmentFrequency:  ParseEnum(d.AdjustmentFrequency, RateAdjustmentFrequency.Static),
                    AdjustmentMechanism:  d.AdjustmentMechanism))
                .ToList()
                .AsReadOnly();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse Claude response for {Name}: {Json}",
                jurisdictionName, json[..Math.Min(200, json.Length)]);
            return [];
        }
    }

    // ── Content prep ──────────────────────────────────────────────────────────

    private static string CleanContent(string content, string mimeType, int maxChars)
    {
        var text = content;

        if (mimeType.Contains("html", StringComparison.OrdinalIgnoreCase))
            text = Regex.Replace(text, "<[^>]+>", " ");

        text = Regex.Replace(text, @"\s{2,}", " ").Trim();

        return text.Length > maxChars ? text[..maxChars] : text;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static T ParseEnum<T>(string value, T fallback) where T : struct, Enum
        => Enum.TryParse<T>(value, ignoreCase: true, out var result) ? result : fallback;

    private static T? ParseNullableEnum<T>(string? value) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Enum.TryParse<T>(value, ignoreCase: true, out var result) ? result : null;
    }

    // ── DTO matching Claude's JSON output ─────────────────────────────────────

    private sealed class ExtractedRateLawDto
    {
        public string Name               { get; set; } = "";
        public decimal Rate              { get; set; }
        public string Basis              { get; set; } = "Percentage";
        public string Unit               { get; set; } = "";
        public string TaxType            { get; set; } = "SalesTax";
        public string? ProductCategory   { get; set; }
        public string SaleContext        { get; set; } = "Any";
        public string RemittancePoint    { get; set; } = "Retailer";
        public decimal? MinAbv           { get; set; }
        public decimal? MaxAbv           { get; set; }
        public string Conditions         { get; set; } = "";
        public string StatutoryReference { get; set; } = "";
        public string EffectiveDate      { get; set; } = "";
        public string ExpirationDate     { get; set; } = "";
        public int? TaxCategoryId        { get; set; }
        public float Confidence          { get; set; } = 0.8f;
        public string RawEvidence        { get; set; } = "";
        public bool IsCompound              { get; set; }
        public decimal? MinTaxableAmount    { get; set; }
        public decimal? MaxTaxableAmount    { get; set; }
        public decimal? FlatCapPerUnit      { get; set; }
        public bool IsTemporary             { get; set; }
        public bool IsRecurring             { get; set; }
        public string AdjustmentFrequency   { get; set; } = "Static";
        public string? AdjustmentMechanism  { get; set; }
    }
}
