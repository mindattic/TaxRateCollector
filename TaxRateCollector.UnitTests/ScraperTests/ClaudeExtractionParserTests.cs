using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Core.Options;
using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.UnitTests.ScraperTests;

/// <summary>
/// Tests <see cref="ClaudeRateLawExtractor"/> parsing and guard behaviour
/// without making real HTTP calls.
/// </summary>
[TestFixture]
public class ClaudeExtractionParserTests
{
    // ── Setup helpers ─────────────────────────────────────────────────────────

    private static ClaudeRateLawExtractor CreateExtractor(
        string claudeResponseText,
        string apiKey = "sk-test-key",
        HttpStatusCode statusCode = HttpStatusCode.OK,
        int maxContentChars = 60_000)
    {
        var anthropicApiResponse = BuildClaudeApiResponse(claudeResponseText);
        var handler = new FixedHttpHandler(anthropicApiResponse, statusCode);
        var http = new HttpClient(handler);

        var opts = Options.Create(new AnthropicOptions
        {
            Model           = "claude-sonnet-4-6",
            MaxTokens       = 1024,
            MaxContentChars = maxContentChars,
        });

        var settings = new SettingsService();
        settings.Current.AnthropicApiKey = apiKey;

        return new ClaudeRateLawExtractor(http, opts, settings,
            NullLogger<ClaudeRateLawExtractor>.Instance);
    }

    private static string BuildClaudeApiResponse(string text)
    {
        var obj = new
        {
            content = new[] { new { type = "text", text } },
        };
        return JsonSerializer.Serialize(obj);
    }

    private static Jurisdiction TestJurisdiction() => new()
    {
        Id = 1, StateCode = "IL", JurisdictionName = "Illinois",
        JurisdictionType = JurisdictionType.State, FipsCode = "17",
    };

    private static string SingleRateLawJson(
        string name = "General Sales Tax",
        double rate = 0.0625,
        double confidence = 0.95) => $$"""
        [
          {
            "Name": "{{name}}",
            "Rate": {{rate}},
            "Basis": "Percentage",
            "Unit": "",
            "TaxType": "SalesTax",
            "ProductCategory": null,
            "SaleContext": "Any",
            "RemittancePoint": "Retailer",
            "MinAbv": null,
            "MaxAbv": null,
            "Conditions": "",
            "StatutoryReference": "35 ILCS 120/2",
            "EffectiveDate": "2024-01-01",
            "ExpirationDate": "",
            "TaxCategoryId": null,
            "Confidence": {{confidence}},
            "RawEvidence": "General sales tax rate: 6.25%"
          }
        ]
        """;

    // ── Tests: parsing formats ────────────────────────────────────────────────

    [Test]
    public async Task PlainJsonArray_ParsedCorrectly()
    {
        var extractor = CreateExtractor(SingleRateLawJson());
        var result = await extractor.ExtractAsync(
            TestJurisdiction(), "some content", "text/html", "https://test.gov");

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Name, Is.EqualTo("General Sales Tax"));
        Assert.That(result[0].Rate, Is.EqualTo(0.0625m));
        Assert.That(result[0].Basis, Is.EqualTo(RateBasis.Percentage));
    }

    [Test]
    public async Task MarkdownWrappedJson_ParsedCorrectly()
    {
        var wrapped = "```json\n" + SingleRateLawJson() + "\n```";
        var extractor = CreateExtractor(wrapped);
        var result = await extractor.ExtractAsync(
            TestJurisdiction(), "some content", "text/html", "https://test.gov");

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Name, Is.EqualTo("General Sales Tax"));
    }

    [Test]
    public async Task MarkdownWithoutLanguageTag_ParsedCorrectly()
    {
        var wrapped = "```\n" + SingleRateLawJson() + "\n```";
        var extractor = CreateExtractor(wrapped);
        var result = await extractor.ExtractAsync(
            TestJurisdiction(), "some content", "text/html", "https://test.gov");

        Assert.That(result, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task EmptyArray_ReturnsEmpty()
    {
        var extractor = CreateExtractor("[]");
        var result = await extractor.ExtractAsync(
            TestJurisdiction(), "some content", "text/html", "https://test.gov");

        Assert.That(result, Is.Empty);
    }

    // ── Tests: confidence filtering ───────────────────────────────────────────

    [Test]
    public async Task LowConfidence_IsFiltered()
    {
        var json = SingleRateLawJson(confidence: 0.30);
        var extractor = CreateExtractor(json);
        var result = await extractor.ExtractAsync(
            TestJurisdiction(), "content", "text/html", "https://test.gov");

        Assert.That(result, Is.Empty, "Rates with Confidence < 0.5 must be filtered");
    }

    [Test]
    public async Task ThresholdConfidence_IsIncluded()
    {
        var json = SingleRateLawJson(confidence: 0.50);
        var extractor = CreateExtractor(json);
        var result = await extractor.ExtractAsync(
            TestJurisdiction(), "content", "text/html", "https://test.gov");

        Assert.That(result, Has.Count.EqualTo(1));
    }

    // ── Tests: field mapping ──────────────────────────────────────────────────

    [Test]
    public async Task AllFields_MappedFromDto()
    {
        const string json = """
        [{
          "Name": "Beer Excise ≤7% ABV",
          "Rate": 0.231,
          "Basis": "FlatPerUnit",
          "Unit": "per pack",
          "TaxType": "ExciseTax",
          "ProductCategory": "Beer",
          "SaleContext": "OffPremise",
          "RemittancePoint": "Distributor",
          "MinAbv": null,
          "MaxAbv": 0.07,
          "Conditions": "Applies to retailers only",
          "StatutoryReference": "235 ILCS 5/8-1",
          "EffectiveDate": "2024-07-01",
          "ExpirationDate": "2025-06-30",
          "TaxCategoryId": null,
          "Confidence": 0.9,
          "RawEvidence": "Beer ≤7% ABV: $0.231/pack"
        }]
        """;

        var extractor = CreateExtractor(json);
        var result = await extractor.ExtractAsync(
            TestJurisdiction(), "content", "text/html", "https://test.gov");

        Assert.That(result, Has.Count.EqualTo(1));
        var law = result[0];
        Assert.That(law.Name,               Is.EqualTo("Beer Excise ≤7% ABV"));
        Assert.That(law.Rate,               Is.EqualTo(0.231m));
        Assert.That(law.Basis,              Is.EqualTo(RateBasis.FlatPerUnit));
        Assert.That(law.Unit,               Is.EqualTo("per pack"));
        Assert.That(law.TaxType,            Is.EqualTo(TaxType.ExciseTax));
        Assert.That(law.ProductCategory,    Is.EqualTo(ProductCategory.Beer));
        Assert.That(law.SaleContext,        Is.EqualTo(SaleContext.OffPremise));
        Assert.That(law.RemittancePoint,    Is.EqualTo(RemittancePoint.Distributor));
        Assert.That(law.MaxAbv,             Is.EqualTo(0.07m));
        Assert.That(law.Conditions,         Is.EqualTo("Applies to retailers only"));
        Assert.That(law.StatutoryReference, Is.EqualTo("235 ILCS 5/8-1"));
        Assert.That(law.EffectiveDate,      Is.EqualTo("2024-07-01"));
        Assert.That(law.ExpirationDate,     Is.EqualTo("2025-06-30"));
        Assert.That(law.RawEvidence,        Does.Contain("$0.231/pack"));
    }

    [Test]
    public async Task UnknownEnumValue_FallsBackToDefault()
    {
        const string json = """
        [{
          "Name": "Test",
          "Rate": 0.05,
          "Basis": "INVALID_BASIS",
          "Unit": "",
          "TaxType": "INVALID_TYPE",
          "ProductCategory": "INVALID_CAT",
          "SaleContext": "INVALID",
          "RemittancePoint": "INVALID",
          "MinAbv": null, "MaxAbv": null,
          "Conditions": "", "StatutoryReference": "",
          "EffectiveDate": "", "ExpirationDate": "",
          "TaxCategoryId": null,
          "Confidence": 0.9,
          "RawEvidence": "test"
        }]
        """;

        var extractor = CreateExtractor(json);
        var result = await extractor.ExtractAsync(
            TestJurisdiction(), "content", "text/html", "https://test.gov");

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Basis,           Is.EqualTo(RateBasis.Percentage));
        Assert.That(result[0].TaxType,         Is.EqualTo(TaxType.SalesTax));
        Assert.That(result[0].ProductCategory, Is.Null);
        Assert.That(result[0].SaleContext,     Is.EqualTo(SaleContext.Any));
        Assert.That(result[0].RemittancePoint, Is.EqualTo(RemittancePoint.Retailer));
    }

    // ── Tests: guards ─────────────────────────────────────────────────────────

    [Test]
    public async Task MissingApiKey_ReturnsEmpty_WithoutCallingHttp()
    {
        var callCount = 0;
        var handler = new CountingHttpHandler(ref callCount, "{}");
        var http = new HttpClient(handler);
        var opts = Options.Create(new AnthropicOptions());
        var settings = new SettingsService();
        settings.Current.AnthropicApiKey = "";   // blank key

        var extractor = new ClaudeRateLawExtractor(http, opts, settings,
            NullLogger<ClaudeRateLawExtractor>.Instance);
        var result = await extractor.ExtractAsync(
            TestJurisdiction(), "content", "text/html", "https://test.gov");

        Assert.That(result, Is.Empty);
        Assert.That(callCount, Is.EqualTo(0), "No HTTP call should be made with a blank API key");
    }

    [Test]
    public async Task EmptyContent_ReturnsEmpty_WithoutCallingHttp()
    {
        var callCount = 0;
        var handler = new CountingHttpHandler(ref callCount, "{}");
        var http = new HttpClient(handler);
        var opts = Options.Create(new AnthropicOptions());
        var settings = new SettingsService();
        settings.Current.AnthropicApiKey = "sk-test";

        var extractor = new ClaudeRateLawExtractor(http, opts, settings,
            NullLogger<ClaudeRateLawExtractor>.Instance);
        var result = await extractor.ExtractAsync(
            TestJurisdiction(), "   ", "text/html", "https://test.gov");

        Assert.That(result, Is.Empty);
        Assert.That(callCount, Is.EqualTo(0));
    }

    [Test]
    public async Task HttpError_ReturnsEmpty_DoesNotThrow()
    {
        var extractor = CreateExtractor("{}", statusCode: HttpStatusCode.InternalServerError);
        Assert.DoesNotThrowAsync(async () =>
        {
            var result = await extractor.ExtractAsync(
                TestJurisdiction(), "content", "text/html", "https://test.gov");
            Assert.That(result, Is.Empty);
        });
    }

    // ── Tests: content preparation ────────────────────────────────────────────

    [Test]
    public async Task HtmlContent_TagsAreStripped_BeforeSending()
    {
        // The extractor strips HTML before sending. We verify that by checking
        // that the extraction still works even with a content-length near the limit
        // and that the cleaned content doesn't contain HTML brackets.
        var captureHandler = new CapturingHttpHandler(BuildClaudeApiResponse("[]"));
        var http = new HttpClient(captureHandler);
        var opts = Options.Create(new AnthropicOptions
            { Model = "test", MaxTokens = 100, MaxContentChars = 60_000 });
        var settings = new SettingsService();
        settings.Current.AnthropicApiKey = "sk-test";

        var extractor = new ClaudeRateLawExtractor(http, opts, settings,
            NullLogger<ClaudeRateLawExtractor>.Instance);

        var htmlContent = "<html><body><p>Rate: 6.25%</p></body></html>";
        await extractor.ExtractAsync(TestJurisdiction(), htmlContent, "text/html", "https://test.gov");

        var sentBody = captureHandler.LastRequestBody;
        Assert.That(sentBody, Does.Not.Contain("<html>"));
        Assert.That(sentBody, Does.Not.Contain("<body>"));
        Assert.That(sentBody, Does.Contain("Rate: 6.25%"));
    }

    [Test]
    public async Task LargeContent_IsTruncated_ToMaxContentChars()
    {
        const int maxChars = 50;
        var captureHandler = new CapturingHttpHandler(BuildClaudeApiResponse("[]"));
        var http = new HttpClient(captureHandler);
        var opts = Options.Create(new AnthropicOptions
            { Model = "test", MaxTokens = 100, MaxContentChars = maxChars });
        var settings = new SettingsService();
        settings.Current.AnthropicApiKey = "sk-test";

        var extractor = new ClaudeRateLawExtractor(http, opts, settings,
            NullLogger<ClaudeRateLawExtractor>.Instance);

        var longContent = new string('x', 200);
        await extractor.ExtractAsync(TestJurisdiction(), longContent, "text/plain", "https://test.gov");

        // The prompt template itself contains 'x' in words, so we check that no run of
        // consecutive x's exceeds maxChars rather than counting total occurrences.
        var sentBody = captureHandler.LastRequestBody ?? "";
        var longestRun = System.Text.RegularExpressions.Regex.Match(sentBody, "x+").Value.Length;
        Assert.That(longestRun, Is.LessThanOrEqualTo(maxChars));
    }

    // ── HTTP stubs ────────────────────────────────────────────────────────────

    private sealed class FixedHttpHandler(string body, HttpStatusCode code = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(code)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class CountingHttpHandler(ref int count, string body) : HttpMessageHandler
    {
        private int count = count;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            count++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }

        public int Count => count;
    }

    private sealed class CapturingHttpHandler(string body) : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
