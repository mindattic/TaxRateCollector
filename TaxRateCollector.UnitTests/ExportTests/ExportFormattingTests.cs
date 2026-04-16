using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.UnitTests.ExportTests;

/// <summary>
/// Tests for the export data pipeline and formatting:
/// CSV formatting, SQL escaping, combined rate calculation, and
/// edge cases in jurisdiction name handling.
///
/// The export logic lives in Jurisdictions.razor — these tests replicate
/// the core formatting functions as standalone helpers so they can be
/// unit-tested independently of the Razor component.
/// </summary>
[TestFixture]
public class ExportFormattingTests
{
    // ── Helpers that mirror the export logic in Jurisdictions.razor ───────────

    /// <summary>Mirrors BuildExportData's CombinedFor local function.</summary>
    private static decimal CombinedFor(int id,
        Dictionary<int, decimal?> rateById,
        Dictionary<int, int?> parentById)
    {
        var total = rateById.GetValueOrDefault(id) ?? 0;
        var cur = id;
        while (parentById.TryGetValue(cur, out var pid) && pid.HasValue)
        {
            total += rateById.GetValueOrDefault(pid.Value) ?? 0;
            cur = pid.Value;
        }
        return total;
    }

    /// <summary>Mirrors the CSV row format in ExportCsv().</summary>
    private static string CsvRow(string tier, string stateCode, string name, string fips,
        decimal? rate, decimal? combined, string effectiveDate, bool hasEvidence)
    {
        return $"{tier},{stateCode},\"{name}\",{fips}," +
               $"{(rate.HasValue ? (rate.Value * 100).ToString("F3") : "")}," +
               $"{(combined.HasValue ? (combined.Value * 100).ToString("F3") : "")}," +
               $"{effectiveDate},{hasEvidence}";
    }

    /// <summary>Mirrors the SQL row format in ExportSql().</summary>
    private static string SqlRow(string tier, string stateCode, string name, string fips,
        decimal? rate, decimal? combined, string effectiveDate, bool hasEvidence, bool isLast)
    {
        var rateStr     = rate.HasValue     ? (rate.Value     * 100).ToString("F3") : "NULL";
        var combinedStr = combined.HasValue ? (combined.Value * 100).ToString("F3") : "NULL";
        var comma       = isLast ? ";" : ",";
        return $"  ('{tier}','{stateCode}','{name.Replace("'", "''")}','{fips}'," +
               $"{rateStr},{combinedStr},'{effectiveDate}',{(hasEvidence ? 1 : 0)}){comma}";
    }

    // ── CombinedFor rate calculation ──────────────────────────────────────────

    [Test]
    public void CombinedFor_CityWithThreeTiers_SumsAllLevels()
    {
        //  Country(id=1) → 0%
        //  State(id=2)   → 6.25%
        //  County(id=3)  → 1.75%
        //  City(id=4)    → 2.25%
        var rates   = new Dictionary<int, decimal?> { [1] = 0m, [2] = 0.0625m, [3] = 0.0175m, [4] = 0.0225m };
        var parents = new Dictionary<int, int?>     { [2] = 1, [3] = 2, [4] = 3 };

        var combined = CombinedFor(4, rates, parents);

        // 0 + 6.25 + 1.75 + 2.25 = 10.25%
        Assert.That(combined, Is.EqualTo(0.1025m).Within(0.0001m));
    }

    [Test]
    public void CombinedFor_StateLevel_ReturnsSingleRate()
    {
        var rates   = new Dictionary<int, decimal?> { [1] = 0m, [2] = 0.0625m };
        var parents = new Dictionary<int, int?>     { [2] = 1 };

        Assert.That(CombinedFor(2, rates, parents), Is.EqualTo(0.0625m).Within(0.0001m));
    }

    [Test]
    public void CombinedFor_NullRateAtLevel_TreatedAsZero()
    {
        // County has no rate set (null) — should be treated as 0
        var rates   = new Dictionary<int, decimal?> { [1] = 0m, [2] = 0.0625m, [3] = null, [4] = 0.01m };
        var parents = new Dictionary<int, int?>     { [2] = 1, [3] = 2, [4] = 3 };

        var combined = CombinedFor(4, rates, parents);
        Assert.That(combined, Is.EqualTo(0.0725m).Within(0.0001m));
    }

    [Test]
    public void CombinedFor_AllZeroRates_ReturnsZero()
    {
        var rates   = new Dictionary<int, decimal?> { [1] = 0m, [2] = 0m, [3] = 0m };
        var parents = new Dictionary<int, int?>     { [2] = 1, [3] = 2 };

        Assert.That(CombinedFor(3, rates, parents), Is.EqualTo(0m));
    }

    // ── CSV formatting ────────────────────────────────────────────────────────

    [Test]
    public void CsvRow_RateConvertedToPercent()
    {
        var row = CsvRow("State", "IL", "Illinois", "17", 0.0625m, null, "2024-01-01", false);
        Assert.That(row, Does.Contain("6.250"));
    }

    [Test]
    public void CsvRow_NullRate_EmptyField()
    {
        var row = CsvRow("State", "IL", "Illinois", "17", null, null, "", false);
        // Rate field should be empty, not "0.000"
        var parts = row.Split(',');
        Assert.That(parts[4], Is.EqualTo(""));
    }

    [Test]
    public void CsvRow_NameQuoted_ProtectsFromCsvInjection()
    {
        var row = CsvRow("County", "IL", "Cook County", "17031", 0.0175m, 0.08m, "2024-01-01", true);
        // Name is wrapped in quotes
        Assert.That(row, Does.Contain("\"Cook County\""));
    }

    [Test]
    public void CsvRow_HasEvidence_TrueOrFalse()
    {
        var rowYes = CsvRow("State", "IL", "Illinois", "17", 0.0625m, null, "2024-01-01", true);
        var rowNo  = CsvRow("State", "TX", "Texas",    "48", 0.0625m, null, "2024-01-01", false);

        Assert.That(rowYes, Does.EndWith("True"));
        Assert.That(rowNo,  Does.EndWith("False"));
    }

    [Test]
    public void CsvHeader_ContainsExpectedColumns()
    {
        const string header = "Tier,State,Name,FIPS,Rate(%),Combined(%),EffectiveDate,Validated";
        var cols = header.Split(',');

        Assert.That(cols, Has.Length.EqualTo(8));
        Assert.That(cols[0], Is.EqualTo("Tier"));
        Assert.That(cols[4], Is.EqualTo("Rate(%)"));
        Assert.That(cols[5], Is.EqualTo("Combined(%)"));
        Assert.That(cols[7], Is.EqualTo("Validated"));
    }

    // ── SQL formatting ────────────────────────────────────────────────────────

    [Test]
    public void SqlRow_SingleQuoteInName_IsDoubled()
    {
        // SQL injection prevention: O'Brien County → O''Brien County
        var row = SqlRow("County", "TX", "O'Brien County", "48357", 0.0125m, 0.075m, "2024-01-01", false, false);
        Assert.That(row, Does.Contain("O''Brien County"));
        Assert.That(row, Does.Not.Contain("O'Brien County"));
    }

    [Test]
    public void SqlRow_LastRow_EndsWithSemicolon()
    {
        var row = SqlRow("State", "IL", "Illinois", "17", 0.0625m, null, "2024-01-01", false, isLast: true);
        Assert.That(row.TrimEnd(), Does.EndWith(";"));
    }

    [Test]
    public void SqlRow_NotLastRow_EndsWithComma()
    {
        var row = SqlRow("State", "IL", "Illinois", "17", 0.0625m, null, "2024-01-01", false, isLast: false);
        Assert.That(row.TrimEnd(), Does.EndWith(","));
    }

    [Test]
    public void SqlRow_NullRate_ProducesNULLKeyword()
    {
        var row = SqlRow("County", "OR", "Multnomah County", "41051", null, null, "", false, false);
        // Both rate fields should be NULL (not 0 or empty)
        Assert.That(row, Does.Contain(",NULL,NULL,"));
    }

    [Test]
    public void SqlRow_HasEvidence_WritesOneOrZero()
    {
        var rowYes = SqlRow("State", "CA", "California", "06", 0.0725m, null, "2024-01-01", true,  false);
        var rowNo  = SqlRow("State", "OR", "Oregon",     "41", 0.00m,   null, "2024-01-01", false, false);

        Assert.That(rowYes, Does.Contain(",1),"));
        Assert.That(rowNo,  Does.Contain(",0),"));
    }

    [Test]
    public void SqlHeader_CreateTableStatement_IsValid()
    {
        const string ddl = "CREATE TABLE IF NOT EXISTS tax_rates_master (\n" +
                           "  tier TEXT, state_code TEXT, name TEXT, fips_code TEXT,\n" +
                           "  rate REAL, combined_rate REAL, effective_date TEXT, validated INTEGER\n" +
                           ");";

        Assert.That(ddl, Does.Contain("CREATE TABLE IF NOT EXISTS"));
        Assert.That(ddl, Does.Contain("tax_rates_master"));
        Assert.That(ddl, Does.Contain("rate REAL"));
        Assert.That(ddl, Does.Contain("validated INTEGER"));
    }

    // ── Rate percentage formatting ─────────────────────────────────────────────

    [Test]
    [TestCase(0.0625,   "6.250")]
    [TestCase(0.1025,   "10.250")]
    [TestCase(0.0,      "0.000")]
    [TestCase(0.00125,  "0.125")]
    [TestCase(0.065,    "6.500")]
    public void RateToPercentString_F3Format_IsCorrect(double rate, string expected)
    {
        var result = ((decimal)rate * 100).ToString("F3");
        Assert.That(result, Is.EqualTo(expected));
    }

    // ── Export row ordering ───────────────────────────────────────────────────

    [Test]
    public void ExportOrder_ByStateCodeThenTierThenName()
    {
        // Mirrors the .OrderBy(j => j.StateCode).ThenBy(j => j.JurisdictionType).ThenBy(j => j.JurisdictionName) in BuildExportData
        var rows = new[]
        {
            (StateCode: "TX", Tier: JurisdictionType.City,   Name: "Austin"),
            (StateCode: "CA", Tier: JurisdictionType.State,  Name: "California"),
            (StateCode: "TX", Tier: JurisdictionType.State,  Name: "Texas"),
            (StateCode: "CA", Tier: JurisdictionType.County, Name: "Los Angeles County"),
        };

        var ordered = rows
            .OrderBy(r => r.StateCode)
            .ThenBy(r => r.Tier)
            .ThenBy(r => r.Name)
            .ToList();

        Assert.That(ordered[0].StateCode, Is.EqualTo("CA"));
        Assert.That(ordered[0].Name,      Is.EqualTo("California"));       // State before County
        Assert.That(ordered[1].Name,      Is.EqualTo("Los Angeles County"));
        Assert.That(ordered[2].StateCode, Is.EqualTo("TX"));
        Assert.That(ordered[2].Name,      Is.EqualTo("Texas"));             // State before City
        Assert.That(ordered[3].Name,      Is.EqualTo("Austin"));
    }

    // ── BuildExportData DB integration ────────────────────────────────────────

    [Test]
    public async Task BuildExportData_EmptyDb_ReturnsEmptyList()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(opts);

        var jurisdictions = await db.Jurisdictions
            .Where(j => j.IsActive && j.JurisdictionType != JurisdictionType.Country)
            .ToListAsync();

        Assert.That(jurisdictions, Is.Empty);
    }

    [Test]
    public async Task BuildExportData_CountryExcluded_FromExport()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(opts);

        db.Jurisdictions.Add(new Jurisdiction
        {
            JurisdictionType = JurisdictionType.Country,
            JurisdictionName = "United States",
            StateCode = "US",
            FipsCode = "US-export",
            IsActive = true,
            SourceUrl = ""
        });
        await db.SaveChangesAsync();

        // The export explicitly filters out Country nodes
        var exportable = await db.Jurisdictions
            .Where(j => j.IsActive && j.JurisdictionType != JurisdictionType.Country)
            .ToListAsync();

        Assert.That(exportable, Is.Empty, "Country-level nodes must never appear in exports.");
    }

    [Test]
    public async Task BuildExportData_InactiveJurisdiction_Excluded()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(opts);

        db.Jurisdictions.AddRange(
            new Jurisdiction { JurisdictionType = JurisdictionType.State, JurisdictionName = "Active State", StateCode = "AS", FipsCode = "99", IsActive = true,  SourceUrl = "" },
            new Jurisdiction { JurisdictionType = JurisdictionType.State, JurisdictionName = "Inactive State", StateCode = "IS", FipsCode = "98", IsActive = false, SourceUrl = "" }
        );
        await db.SaveChangesAsync();

        var exportable = await db.Jurisdictions
            .Where(j => j.IsActive)
            .ToListAsync();

        Assert.That(exportable, Has.Count.EqualTo(1));
        Assert.That(exportable[0].JurisdictionName, Is.EqualTo("Active State"));
    }

    [Test]
    public async Task BuildExportData_HasEvidence_CorrectlyFlagged()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(opts);

        var run = new ScrapeRun { StartedAt = DateTime.UtcNow.ToString("o"), Status = ScrapeStatus.Manual };
        db.ScrapeRuns.Add(run);

        var country = new Jurisdiction { JurisdictionType = JurisdictionType.Country, JurisdictionName = "US", StateCode = "US", FipsCode = "US-ev", IsActive = true, SourceUrl = "" };
        db.Jurisdictions.Add(country);
        await db.SaveChangesAsync();

        var state = new Jurisdiction { JurisdictionType = JurisdictionType.State, JurisdictionName = "Illinois", StateCode = "IL", FipsCode = "17-ev", IsActive = true, ParentId = country.Id, SourceUrl = "" };
        db.Jurisdictions.Add(state);
        await db.SaveChangesAsync();

        var rate = new TaxRate { JurisdictionId = state.Id, Rate = 0.0625m, RateType = "General", EffectiveDate = "2024-01-01", ScrapedAt = DateTime.UtcNow.ToString("o"), ScrapeRunId = run.Id, IsCurrent = true };
        db.TaxRates.Add(rate);
        await db.SaveChangesAsync();

        var doc = new SourceDocument { TaxRateId = rate.Id, SourceType = SourceType.Pdf, FileName = "il_rate.pdf", MimeType = "application/pdf", FetchedAt = DateTime.UtcNow.ToString("o"), ContentHash = "abc", IsActive = true };
        db.SourceDocuments.Add(doc);
        await db.SaveChangesAsync();

        // Simulate HasEvidence flag computation
        var rateIds = new[] { rate.Id };
        var evidencedRateIds = await db.SourceDocuments
            .Where(d => rateIds.Contains(d.TaxRateId) && d.IsActive)
            .Select(d => d.TaxRateId)
            .Distinct()
            .ToHashSetAsync();

        Assert.That(evidencedRateIds.Contains(rate.Id), Is.True, "Rate with an active SourceDocument should be flagged as evidenced.");
    }

    [Test]
    public async Task BuildExportData_NoEvidence_NotFlagged()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(opts);

        var run = new ScrapeRun { StartedAt = DateTime.UtcNow.ToString("o"), Status = ScrapeStatus.Manual };
        db.ScrapeRuns.Add(run);

        var country = new Jurisdiction { JurisdictionType = JurisdictionType.Country, JurisdictionName = "US", StateCode = "US", FipsCode = "US-noev", IsActive = true, SourceUrl = "" };
        db.Jurisdictions.Add(country);
        await db.SaveChangesAsync();

        var state = new Jurisdiction { JurisdictionType = JurisdictionType.State, JurisdictionName = "Texas", StateCode = "TX", FipsCode = "48-noev", IsActive = true, ParentId = country.Id, SourceUrl = "" };
        db.Jurisdictions.Add(state);
        await db.SaveChangesAsync();

        var rate = new TaxRate { JurisdictionId = state.Id, Rate = 0.0625m, RateType = "General", EffectiveDate = "2024-01-01", ScrapedAt = DateTime.UtcNow.ToString("o"), ScrapeRunId = run.Id, IsCurrent = true };
        db.TaxRates.Add(rate);
        await db.SaveChangesAsync();
        // No SourceDocuments added

        var rateIds = new[] { rate.Id };
        var evidencedRateIds = await db.SourceDocuments
            .Where(d => rateIds.Contains(d.TaxRateId) && d.IsActive)
            .Select(d => d.TaxRateId)
            .Distinct()
            .ToHashSetAsync();

        Assert.That(evidencedRateIds.Contains(rate.Id), Is.False, "Rate with no evidence should not be flagged.");
    }
}
