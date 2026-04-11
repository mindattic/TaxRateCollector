using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.Infrastructure.Seeding;

public static class JurisdictionSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.Jurisdictions.AnyAsync()) return;

        var jurisdictions = new List<Jurisdiction>
        {
            // Illinois counties
            new() { StateCode = "IL", JurisdictionName = "Cook County", JurisdictionType = JurisdictionType.County, FipsCode = "17031", SourceUrl = "https://tax.illinois.gov/research/taxinformation/sales/rot.html", IsActive = false },
            new() { StateCode = "IL", JurisdictionName = "DuPage County", JurisdictionType = JurisdictionType.County, FipsCode = "17043", SourceUrl = "https://tax.illinois.gov/research/taxinformation/sales/rot.html", IsActive = false },
            new() { StateCode = "IL", JurisdictionName = "Lake County", JurisdictionType = JurisdictionType.County, FipsCode = "17097", SourceUrl = "https://tax.illinois.gov/research/taxinformation/sales/rot.html", IsActive = false },
            new() { StateCode = "IL", JurisdictionName = "Will County", JurisdictionType = JurisdictionType.County, FipsCode = "17197", SourceUrl = "https://tax.illinois.gov/research/taxinformation/sales/rot.html", IsActive = false },
            new() { StateCode = "IL", JurisdictionName = "Kane County", JurisdictionType = JurisdictionType.County, FipsCode = "17089", SourceUrl = "https://tax.illinois.gov/research/taxinformation/sales/rot.html", IsActive = false },

            // California cities
            new() { StateCode = "CA", JurisdictionName = "Los Angeles", JurisdictionType = JurisdictionType.City, FipsCode = "06037", SourceUrl = "https://cdtfa.ca.gov/formspubs/cdtfa95.pdf", IsActive = false },
            new() { StateCode = "CA", JurisdictionName = "San Francisco", JurisdictionType = JurisdictionType.City, FipsCode = "06075", SourceUrl = "https://cdtfa.ca.gov/formspubs/cdtfa95.pdf", IsActive = false },
            new() { StateCode = "CA", JurisdictionName = "San Diego", JurisdictionType = JurisdictionType.City, FipsCode = "06073", SourceUrl = "https://cdtfa.ca.gov/formspubs/cdtfa95.pdf", IsActive = false },
            new() { StateCode = "CA", JurisdictionName = "San Jose", JurisdictionType = JurisdictionType.City, FipsCode = "06085", SourceUrl = "https://cdtfa.ca.gov/formspubs/cdtfa95.pdf", IsActive = false },
            new() { StateCode = "CA", JurisdictionName = "Oakland", JurisdictionType = JurisdictionType.City, FipsCode = "06001", SourceUrl = "https://cdtfa.ca.gov/formspubs/cdtfa95.pdf", IsActive = false },

            // Texas cities
            new() { StateCode = "TX", JurisdictionName = "Houston", JurisdictionType = JurisdictionType.City, FipsCode = "48201", SourceUrl = "https://comptroller.texas.gov/taxes/sales/rates/", IsActive = false },
            new() { StateCode = "TX", JurisdictionName = "Dallas", JurisdictionType = JurisdictionType.City, FipsCode = "48113", SourceUrl = "https://comptroller.texas.gov/taxes/sales/rates/", IsActive = false },
            new() { StateCode = "TX", JurisdictionName = "Austin", JurisdictionType = JurisdictionType.City, FipsCode = "48453", SourceUrl = "https://comptroller.texas.gov/taxes/sales/rates/", IsActive = false },
            new() { StateCode = "TX", JurisdictionName = "San Antonio", JurisdictionType = JurisdictionType.City, FipsCode = "48029", SourceUrl = "https://comptroller.texas.gov/taxes/sales/rates/", IsActive = false },
            new() { StateCode = "TX", JurisdictionName = "Fort Worth", JurisdictionType = JurisdictionType.City, FipsCode = "48121", SourceUrl = "https://comptroller.texas.gov/taxes/sales/rates/", IsActive = false },
        };

        db.Jurisdictions.AddRange(jurisdictions);
        await db.SaveChangesAsync();

        // Seed sample tax rates for each jurisdiction (simulated, since scrapers need real URLs)
        var ratesByFips = new Dictionary<string, decimal>
        {
            ["17031"] = 0.1025m, // Cook County IL
            ["17043"] = 0.0750m,
            ["17097"] = 0.0750m,
            ["17197"] = 0.0750m,
            ["17089"] = 0.0750m,
            ["06037"] = 0.1025m, // LA CA
            ["06075"] = 0.0875m, // SF
            ["06073"] = 0.0775m, // SD
            ["06085"] = 0.0925m, // SJ
            ["06001"] = 0.1025m, // Oakland
            ["48201"] = 0.0825m, // Houston TX
            ["48113"] = 0.0825m, // Dallas
            ["48453"] = 0.0825m, // Austin
            ["48029"] = 0.0825m, // SA
            ["48121"] = 0.0825m, // FW
        };

        // Create a seed run
        var seedRun = new ScrapeRun
        {
            StartedAt = DateTime.UtcNow.AddDays(-1).ToString("o"),
            CompletedAt = DateTime.UtcNow.AddDays(-1).ToString("o"),
            Status = TaxRateCollector.Core.Enums.ScrapeStatus.Completed,
            TotalScraped = jurisdictions.Count,
            ChangesDetected = 0,
            ErrorCount = 0
        };
        db.ScrapeRuns.Add(seedRun);
        await db.SaveChangesAsync();

        foreach (var j in jurisdictions)
        {
            if (ratesByFips.TryGetValue(j.FipsCode, out var rate))
            {
                db.TaxRates.Add(new TaxRate
                {
                    JurisdictionId = j.Id,
                    Rate = rate,
                    RateType = "General",
                    EffectiveDate = "2024-01-01",
                    ScrapedAt = DateTime.UtcNow.AddDays(-1).ToString("o"),
                    ScrapeRunId = seedRun.Id,
                    RawValue = $"{rate * 100:F2}%",
                    IsCurrent = true
                });
            }
        }

        await db.SaveChangesAsync();
    }
}
