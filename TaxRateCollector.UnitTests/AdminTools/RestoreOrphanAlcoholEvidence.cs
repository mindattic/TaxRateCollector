using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Data;
using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.UnitTests.AdminTools;

/// <summary>
/// One-shot data fix: re-attaches authoritative .gov statute pages as SourceDocuments
/// to the 26 alcohol-excise rates whose original aggregator sources (salestaxhandbook.com)
/// were soft-deleted. Each rate's <see cref="TaxRate.Conditions"/> already cites the
/// controlling statute; this tool maps each citation to a verified statute URL and
/// runs the standard fetch → hash → file-store pipeline.
///
/// Marked [Explicit] so it never runs in CI. Trigger manually with:
///   dotnet test --filter "FullyQualifiedName~RestoreOrphanAlcoholEvidence"
/// </summary>
[TestFixture, Explicit, Category("AdminTool")]
public class RestoreOrphanAlcoholEvidence
{
    // Statute citation fragment → authoritative .gov URL.
    // Matched as a substring against TaxRate.Conditions OR TaxRate.Name (whichever
    // contains the cite). Conditions uses full names ("Montana Code Annotated"),
    // Name uses abbreviations ("MCA"); we register both forms.
    private static readonly (string Cite, string Url)[] Mapping =
    [
        // Idaho — Conditions and Name both use "Idaho Code"
        ("Idaho Code § 23-1008",            "https://legislature.idaho.gov/statutesrules/idstat/Title23/T23CH10/"),
        ("Idaho Code § 23-1319",            "https://legislature.idaho.gov/statutesrules/idstat/Title23/T23CH13/"),
        ("Idaho Code § 23-202",             "https://legislature.idaho.gov/statutesrules/idstat/Title23/T23CH2/"),

        // Montana — Conditions uses "Montana Code Annotated", Name uses "MCA"
        ("Montana Code Annotated § 16-1-406", "https://mca.legmt.gov/bills/mca/title_0160/chapter_0010/part_0040/sections_index.html"),
        ("Montana Code Annotated § 16-1-411", "https://mca.legmt.gov/bills/mca/title_0160/chapter_0010/part_0040/sections_index.html"),
        ("MCA Title 16, Ch. 2",               "https://mca.legmt.gov/bills/mca/title_0160/chapter_0020/parts_index.html"),
        ("Montana Code Annotated Title 16",   "https://mca.legmt.gov/bills/mca/title_0160/chapter_0020/parts_index.html"),

        // North Dakota — Conditions uses "North Dakota Century Code"
        ("North Dakota Century Code § 5-03-07", "https://ndlegis.gov/cencode/t05.html"),

        // Ohio — Conditions uses "Ohio Revised Code"
        ("Ohio Revised Code § 4301.42",     "https://codes.ohio.gov/ohio-revised-code/chapter-4301"),
        ("Ohio Revised Code § 4301.43",     "https://codes.ohio.gov/ohio-revised-code/chapter-4301"),
        ("Ohio Revised Code § 4301.10",     "https://codes.ohio.gov/ohio-revised-code/chapter-4301"),

        // Oregon — Conditions uses "Oregon Revised Statutes"
        ("Oregon Revised Statutes § 473.030", "https://www.oregonlegislature.gov/bills_laws/ors/ors473.html"),
        ("Oregon Revised Statutes § 471.730", "https://www.oregonlegislature.gov/bills_laws/ors/ors471.html"),

        // South Dakota — Conditions uses "South Dakota Codified Laws"
        ("South Dakota Codified Laws § 35-5-3", "https://dor.sd.gov/businesses/taxes/alcohol/"),
    ];

    [Test]
    public async Task ReattachStatuteEvidence()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(TestDbConnection.ConnectionString)
            .Options;

        await using var db = new AppDbContext(opts);

        // Targets: TaxRates that historically had a salestaxhandbook SourceDocument
        // (any IsActive value) and currently have no active document.
        var orphanIds = await db.TaxRates
            .Where(t => db.SourceDocuments.Any(d => d.TaxRateId == t.Id && d.SourceUrl.Contains("salestaxhandbook"))
                     && !db.SourceDocuments.Any(d => d.TaxRateId == t.Id && d.IsActive))
            .Select(t => t.Id)
            .ToListAsync();

        Assert.That(orphanIds, Is.Not.Empty, "Expected orphan rates from the salestaxhandbook deactivation pass.");
        TestContext.Out.WriteLine($"Found {orphanIds.Count} orphan rate(s) to re-evidence.");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // mca.legmt.gov rejects requests carrying "bot-like" UA tokens (anything
        // outside a small allowlist of browser strings), so we send a plain
        // Chrome UA. State legislature sites accept this without issue.
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        var store = new EvidenceFileStore(NullLogger<EvidenceFileStore>.Instance);

        // Fetch each unique URL once and reuse across all rates that map to it.
        var urlCache = new Dictionary<string, (byte[] Bytes, string Mime, StoredEvidenceFile Stored)>();
        var now = DateTime.UtcNow.ToString("o");
        int reattached = 0, skipped = 0;
        var skippedDetails = new List<string>();

        foreach (var rateId in orphanIds)
        {
            var rate = await db.TaxRates.FindAsync(rateId);
            if (rate is null) continue;

            var match = Mapping.FirstOrDefault(m =>
                rate.Conditions.Contains(m.Cite, StringComparison.Ordinal) ||
                rate.Name.Contains(m.Cite, StringComparison.Ordinal));
            if (match.Url is null)
            {
                skipped++;
                skippedDetails.Add($"  [{rate.Id}] '{rate.Name}' — no statute mapping matched Conditions");
                continue;
            }

            if (!urlCache.TryGetValue(match.Url, out var fetched))
            {
                try
                {
                    using var resp = await http.GetAsync(match.Url);
                    resp.EnsureSuccessStatusCode();
                    var bytes = await resp.Content.ReadAsByteArrayAsync();
                    var mime = resp.Content.Headers.ContentType?.MediaType ?? "text/html";
                    var stored = await store.SaveAsync(match.Url, bytes, mime);
                    fetched = (bytes, mime, stored);
                    urlCache[match.Url] = fetched;
                    TestContext.Out.WriteLine($"  fetched {match.Url} ({bytes.Length:N0} bytes, hash {stored.ContentHash[..12]})");
                }
                catch (Exception ex)
                {
                    skipped++;
                    skippedDetails.Add($"  [{rate.Id}] '{rate.Name}' — fetch failed: {ex.Message}");
                    continue;
                }
            }

            db.SourceDocuments.Add(new SourceDocument
            {
                TaxRateId        = rate.Id,
                SourceType       = SourceType.Website,
                SourceUrl        = match.Url,
                MimeType         = fetched.Mime,
                FetchedAt        = now,
                ContentHash      = fetched.Stored.ContentHash,
                EvidenceType     = fetched.Stored.EvidenceType,
                FileName         = fetched.Stored.FileName,
                OriginalFileName = Path.GetFileName(new Uri(match.Url).AbsolutePath),
                RawContent       = string.Empty,
                IsActive         = true,
            });
            reattached++;
        }

        await db.SaveChangesAsync();

        TestContext.Out.WriteLine($"\nResult: re-attached {reattached}, skipped {skipped} of {orphanIds.Count}.");
        foreach (var s in skippedDetails) TestContext.Out.WriteLine(s);

        Assert.That(reattached, Is.GreaterThan(0));
    }
}
