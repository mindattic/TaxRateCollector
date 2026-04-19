using System.Security.Cryptography;
using System.Text;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;

namespace TaxRateCollector.UnitTests.EvidenceTests;

/// <summary>
/// Tests for evidence provenance: hashing, content integrity, source document linkage,
/// and verification workflows that will underpin the scraper and evidence capture pipeline.
/// </summary>
[TestFixture]
public class EvidenceValidationTests
{
    // ── SHA-256 hash helpers ──────────────────────────────────────────────────

    private static string Sha256(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    [Test]
    public void Hash_SameContent_ProducesSameHash()
    {
        const string content = """{"rate":6.25,"jurisdiction":"Illinois"}""";
        Assert.That(Sha256(content), Is.EqualTo(Sha256(content)));
    }

    [Test]
    public void Hash_DifferentContent_ProducesDifferentHash()
    {
        var h1 = Sha256("""{"rate":6.25}""");
        var h2 = Sha256("""{"rate":6.50}""");
        Assert.That(h1, Is.Not.EqualTo(h2));
    }

    [Test]
    public void Hash_EmptyString_ProducesKnownHash()
    {
        // SHA-256("") = e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
        var hash = Sha256("");
        Assert.That(hash, Does.StartWith("e3b0c44298fc1c149afb"));
    }

    [Test]
    public void Hash_64CharHexOutput()
    {
        var hash = Sha256("test content");
        Assert.That(hash, Has.Length.EqualTo(64));
        Assert.That(hash, Does.Match("^[0-9a-f]{64}$"));
    }

    // ── SourceDocument integrity ──────────────────────────────────────────────

    [Test]
    public void SourceDocument_HashMatchesContent_PassesVerification()
    {
        const string rawContent = """{"state":"IL","rate":0.0625,"effective":"2024-01-01"}""";
        var doc = new SourceDocument
        {
            TaxRateId = 1,
            SourceType = SourceType.Api,
            SourceUrl = "https://tax.illinois.gov/api/rates",
            MimeType = "application/json",
            FetchedAt = "2026-04-15T00:00:00Z",
            ContentHash = Sha256(rawContent),
            RawContent = rawContent
        };

        var recomputed = Sha256(doc.RawContent);
        Assert.That(recomputed, Is.EqualTo(doc.ContentHash), "Re-hashing RawContent should match stored ContentHash.");
    }

    [Test]
    public void SourceDocument_TamperedContent_FailsVerification()
    {
        const string original = """{"rate":0.0625}""";
        const string tampered = """{"rate":0.0725}""";

        var doc = new SourceDocument
        {
            ContentHash = Sha256(original),
            RawContent = tampered
        };

        var recomputed = Sha256(doc.RawContent);
        Assert.That(recomputed, Is.Not.EqualTo(doc.ContentHash), "Tampered content should not match original hash.");
    }

    [Test]
    public void SourceDocument_Base64Pdf_RoundTrips()
    {
        // Simulate a PDF being stored as base64
        var pdfBytes = Encoding.UTF8.GetBytes("%PDF-1.4 simulated content...");
        var base64 = Convert.ToBase64String(pdfBytes);

        var doc = new SourceDocument
        {
            SourceType = SourceType.Pdf,
            MimeType = "application/pdf",
            ContentHash = Sha256(base64),
            RawContent = base64
        };

        var decoded = Convert.FromBase64String(doc.RawContent);
        Assert.That(decoded, Is.EqualTo(pdfBytes));
    }

    // ── SourceType discrimination ─────────────────────────────────────────────

    [Test]
    [TestCase(SourceType.Api,     "application/json")]
    [TestCase(SourceType.Pdf,     "application/pdf")]
    [TestCase(SourceType.Csv,     "text/csv")]
    [TestCase(SourceType.Website, "text/html")]
    [TestCase(SourceType.Manual,  "text/plain")]
    public void SourceType_MimeMapping_IsConsistent(SourceType sourceType, string expectedMime)
    {
        var mime = sourceType switch
        {
            SourceType.Api     => "application/json",
            SourceType.Pdf     => "application/pdf",
            SourceType.Csv     => "text/csv",
            SourceType.Website => "text/html",
            SourceType.Manual  => "text/plain",
            _                  => "application/octet-stream"
        };
        Assert.That(mime, Is.EqualTo(expectedMime));
    }

    // ── URL validation ────────────────────────────────────────────────────────

    [Test]
    [TestCase("https://tax.illinois.gov/research/taxinformation/sales/rot.html")]
    [TestCase("https://www.cdtfa.ca.gov/formspubs/cdtfa95.pdf")]
    [TestCase("https://comptroller.texas.gov/taxes/sales/rates/")]
    public void SourceUrl_GovernmentDomains_AreValid(string url)
    {
        Assert.That(Uri.TryCreate(url, UriKind.Absolute, out var uri), Is.True);
        Assert.That(uri!.Scheme, Is.EqualTo("https"));
    }

    [Test]
    [TestCase("")]
    [TestCase("not-a-url")]
    [TestCase("ftp://wrong-scheme.gov")]
    public void SourceUrl_InvalidUrls_FailValidation(string url)
    {
        var isValidHttps = Uri.TryCreate(url, UriKind.Absolute, out var uri)
                           && uri.Scheme == "https";
        Assert.That(isValidHttps, Is.False);
    }

    // ── Excise tax rate laws (merged into TaxRate) ────────────────────────────

    [Test]
    public void TaxRate_FlatPerUnit_RoundTrips()
    {
        var excise = new TaxRate
        {
            Name = "Cigarette Excise",
            Rate = 0.231m,   // $0.231 per pack (Illinois)
            RateBasis = RateBasis.FlatPerUnit,
            Unit = "per pack",
            RawEvidence = "$0.231/pack",
            RemittancePoint = RemittancePoint.Distributor
        };

        Assert.That(excise.Name, Is.EqualTo("Cigarette Excise"));
        Assert.That(excise.Rate, Is.EqualTo(0.231m));
        Assert.That(excise.Unit, Is.EqualTo("per pack"));
        Assert.That(excise.RateBasis, Is.EqualTo(RateBasis.FlatPerUnit));
    }

    [Test]
    public void TaxRate_PercentageExcise_IsInBounds()
    {
        var excise = new TaxRate
        {
            Name = "Alcohol Excise",
            Rate = 0.14m,   // hypothetical 14% alcohol excise
            RateBasis = RateBasis.Percentage,
            RemittancePoint = RemittancePoint.Distributor
        };

        Assert.That(excise.Rate, Is.GreaterThanOrEqualTo(0m));
        Assert.That(excise.Rate, Is.LessThanOrEqualTo(1m));
    }

    // ── TaxSourceProvenance (domain model) ───────────────────────────────────

    [Test]
    public void TaxSourceProvenance_EmptyByDefault()
    {
        var p = new TaxSourceProvenance();
        Assert.That(p.SourceUri, Is.Empty);
        Assert.That(p.DocumentHash, Is.Empty);
        Assert.That(p.RawResponse, Is.Empty);
    }

    [Test]
    public void TaxSourceProvenance_WithApiResponse_IsPopulated()
    {
        const string json = """{"jurisdiction":"IL","rate":6.25,"unit":"%"}""";
        var p = new TaxSourceProvenance
        {
            SourceType = "api",
            SourceUri = "https://tax.illinois.gov/api",
            ContentType = "application/json",
            RetrievedAt = "2026-04-15T00:00:00Z",
            DocumentHash = Sha256(json),
            RawResponse = json,
            Notes = "Illinois DOR API endpoint"
        };

        Assert.That(p.SourceType, Is.EqualTo("api"));
        Assert.That(p.DocumentHash, Has.Length.EqualTo(64));
        Assert.That(Sha256(p.RawResponse), Is.EqualTo(p.DocumentHash));
    }
}
