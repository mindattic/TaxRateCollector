using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.Infrastructure.Seeding;

/// <summary>
/// Seeds the global jurisdiction hierarchy: Country → State → County → City.
/// Currently seeds: United States of America (50 states + DC).
/// Idempotent per-country — each country is skipped if already seeded.
/// </summary>
public static class JurisdictionSeeder
{
    private sealed record JState(string Name, string Code, string Fips, decimal Rate, string SourceUrl, JCounty[] Counties);
    private sealed record JCounty(string Name, string Fips, decimal Rate, string SourceUrl, JCity[] Cities);
    private sealed record JCity(string Name, string Fips, decimal Rate, string SourceUrl);

    public static async Task SeedAsync(AppDbContext db)
    {
        // ── One-time cleanup: remove any non-US countries and their full subtrees ──
        // ParentId uses Restrict, so delete bottom-up: City → County → State → Country.
        // TaxRates cascade-delete automatically via the DB FK.
        var nonUsCountryIds = await db.Jurisdictions
            .Where(j => j.JurisdictionType == JurisdictionType.Country && j.FipsCode != "US")
            .Select(j => j.Id)
            .ToListAsync();

        if (nonUsCountryIds.Count > 0)
        {
            var stateIds = await db.Jurisdictions
                .Where(j => j.ParentId != null && nonUsCountryIds.Contains(j.ParentId.Value))
                .Select(j => j.Id)
                .ToListAsync();

            var countyIds = await db.Jurisdictions
                .Where(j => j.ParentId != null && stateIds.Contains(j.ParentId.Value))
                .Select(j => j.Id)
                .ToListAsync();

            // Cities (and any deeper levels)
            await db.Jurisdictions
                .Where(j => j.ParentId != null && countyIds.Contains(j.ParentId.Value))
                .ExecuteDeleteAsync();

            if (countyIds.Count > 0)
                await db.Jurisdictions.Where(j => countyIds.Contains(j.Id)).ExecuteDeleteAsync();

            if (stateIds.Count > 0)
                await db.Jurisdictions.Where(j => stateIds.Contains(j.Id)).ExecuteDeleteAsync();

            await db.Jurisdictions.Where(j => nonUsCountryIds.Contains(j.Id)).ExecuteDeleteAsync();
        }

        // ── One-time rename for databases seeded before the full name was added ──
        var usOld = await db.Jurisdictions.FirstOrDefaultAsync(j =>
            j.JurisdictionType == JurisdictionType.Country && j.FipsCode == "US" && j.JurisdictionName == "United States");
        if (usOld != null) { usOld.JurisdictionName = "United States of America"; await db.SaveChangesAsync(); }

        // Seed each country — per-country idempotency check
        await SeedCountryIfAbsentAsync(db, "US", "United States of America", "https://www.irs.gov/", 0.00m, UnitedStatesData);
    }

    private static async Task SeedCountryIfAbsentAsync(AppDbContext db,
        string fipsCode, string countryName, string sourceUrl, decimal countryRate, JState[] subdivisions)
    {
        if (await db.Jurisdictions.AnyAsync(j => j.JurisdictionType == JurisdictionType.Country && j.FipsCode == fipsCode))
            return;

        var now    = DateTime.UtcNow;
        var today  = DateOnly.FromDateTime(now);
        var nowIso = now.ToString("o");

        var seedRun = new ScrapeRun
        {
            StartedAt = nowIso, CompletedAt = nowIso, Status = ScrapeStatus.Manual,
            TotalScraped = 0, ChangesDetected = 0, ErrorCount = 0
        };
        db.ScrapeRuns.Add(seedRun);
        await db.SaveChangesAsync();

        var country = new Jurisdiction
        {
            JurisdictionType = JurisdictionType.Country,
            JurisdictionName = countryName,
            StateCode = fipsCode,
            FipsCode = fipsCode,
            SourceUrl = sourceUrl,
            IsActive = true,
            ParentId = null
        };
        db.Jurisdictions.Add(country);
        await db.SaveChangesAsync();

        var statePairs = subdivisions
            .Select(s => (Data: s, Entity: new Jurisdiction
            {
                JurisdictionType = JurisdictionType.State,
                JurisdictionName = s.Name,
                StateCode = s.Code,
                FipsCode = s.Fips,
                SourceUrl = s.SourceUrl,
                IsActive = true,
                ParentId = country.Id
            }))
            .ToList();
        db.Jurisdictions.AddRange(statePairs.Select(p => p.Entity));
        await db.SaveChangesAsync();

        // Counties and cities are NOT seeded here.
        // CensusAutoImportService downloads and imports all ~3,200 counties
        // and ~30,000 cities from the Census Bureau on first startup.

        var leafCats = await db.TaxCategories.Where(c => c.IsLeaf).ToListAsync();

        AddRatesForCategories(db, country.Id, countryRate, today, nowIso, seedRun.Id, leafCats);
        foreach (var (s, sEntity) in statePairs) AddRatesForCategories(db, sEntity.Id, s.Rate, today, nowIso, seedRun.Id, leafCats);

        int jurisdictionCount = 1 + statePairs.Count;
        int catCount = leafCats.Count > 0 ? leafCats.Count : 1;
        seedRun.TotalScraped = jurisdictionCount * catCount;
        db.ScrapeRuns.Update(seedRun);
        await db.SaveChangesAsync();
    }

    private static void AddRatesForCategories(AppDbContext db, int jurisdictionId, decimal rate,
        DateOnly effectiveDate, string scrapedAt, int scrapeRunId, IReadOnlyList<TaxCategory> leafCats)
    {
        if (leafCats.Count == 0)
        {
            db.TaxRates.Add(new TaxRate
            {
                JurisdictionId = jurisdictionId,
                Rate = rate,
                Name = "General Sales Tax", RateBasis = RateBasis.Percentage,
                EffectiveDate = effectiveDate,
                ScrapedAt = scrapedAt,
                ScrapeRunId = scrapeRunId,
                RawEvidence = $"{rate * 100:F3}%",
                IsCurrent = true
            });
            return;
        }

        foreach (var cat in leafCats)
        {
            db.TaxRates.Add(new TaxRate
            {
                JurisdictionId = jurisdictionId,
                TaxCategoryId = cat.Id,
                Rate = rate,
                Name = "General Sales Tax", RateBasis = RateBasis.Percentage,
                EffectiveDate = effectiveDate,
                ScrapedAt = scrapedAt,
                ScrapeRunId = scrapeRunId,
                RawEvidence = $"{rate * 100:F3}%",
                IsCurrent = true
            });
        }
    }

    // ── United States ───────────────────────────────────────────────────────────
    // Rates: state = 2024 base sales tax rate. County/city = local add-on only.
    // Combined = state + county + city. Source URLs → official .gov pages.

    private static readonly JState[] UnitedStatesData =
    [
        new("Alabama", "AL", "01", 0.04m, "https://revenue.alabama.gov/sales-use/",
        [
            new("Jefferson County", "01073", 0.01m, "https://www.jeffcotax.com/",
            [
                new("Birmingham", "0107000", 0.00m, "https://www.birminghamal.gov/departments/finance"),
                new("Hoover", "0135896", 0.00m, "https://www.hooveral.gov/241/Finance"),
                new("Bessemer", "0106616", 0.00m, "https://www.bessemeral.gov"),
            ]),
            new("Madison County", "01089", 0.005m, "https://www.madisoncountyal.gov/",
            [
                new("Huntsville", "0137000", 0.005m, "https://www.huntsvilleal.gov/"),
                new("Madison City", "0145784", 0.00m, "https://www.madisonal.gov/"),
            ]),
            new("Mobile County", "01097", 0.01m, "https://www.mobilecountyal.gov/",
            [
                new("Mobile", "0150000", 0.015m, "https://www.cityofmobile.org/finance/"),
                new("Prichard", "0161696", 0.00m, "https://www.prichardal.com/"),
            ]),
            new("Montgomery County", "01101", 0.00m, "https://www.montgomery.al.gov/",
            [
                new("Montgomery", "0151000", 0.035m, "https://www.montgomeryal.gov/"),
                new("Prattville", "0162328", 0.00m, "https://www.prattvilleal.gov/"),
            ]),
        ]),

        new("Alaska", "AK", "02", 0.00m, "https://tax.alaska.gov/",
        [
            new("Anchorage Borough", "02020", 0.00m, "https://www.muni.org/",
            [
                new("Anchorage", "0203000", 0.00m, "https://www.muni.org/"),
                new("Eagle River", "02020-ER", 0.00m, "https://www.muni.org/"),
            ]),
            new("Fairbanks North Star Borough", "02090", 0.00m, "https://www.fnsb.gov/",
            [
                new("Fairbanks", "0224230", 0.00m, "https://www.fairbanks.us/"),
                new("North Pole", "0257200", 0.00m, "https://www.northpolealaska.com/"),
            ]),
            new("Juneau Borough", "02110", 0.05m, "https://www.juneau.org/",
            [
                new("Juneau", "0236400", 0.00m, "https://www.juneau.org/"),
            ]),
            new("Matanuska-Susitna Borough", "02170", 0.00m, "https://www.matsugov.us/",
            [
                new("Wasilla", "0280070", 0.00m, "https://www.cityofwasilla.com/"),
                new("Palmer", "0259350", 0.00m, "https://www.cityofpalmer.org/"),
            ]),
        ]),

        new("Arizona", "AZ", "04", 0.056m, "https://azdor.gov/transaction-privilege-tax",
        [
            new("Maricopa County", "04013", 0.007m, "https://www.maricopa.gov/",
            [
                new("Phoenix", "0455000", 0.023m, "https://www.phoenix.gov/budget"),
                new("Mesa", "0446000", 0.02m, "https://www.mesaaz.gov/"),
                new("Scottsdale", "0465000", 0.0195m, "https://www.scottsdaleaz.gov/"),
                new("Chandler", "0411320", 0.02m, "https://www.chandleraz.gov/"),
                new("Gilbert", "0427820", 0.015m, "https://www.gilbertaz.gov/"),
                new("Tempe", "0473000", 0.018m, "https://www.tempe.gov/"),
                new("Glendale", "0429426", 0.029m, "https://www.glendaleaz.com/"),
                new("Peoria", "0454050", 0.024m, "https://www.peoriaaz.gov/"),
            ]),
            new("Pima County", "04019", 0.005m, "https://www.pima.gov/",
            [
                new("Tucson", "0477000", 0.026m, "https://www.tucsonaz.gov/"),
                new("Oro Valley", "0451810", 0.02m, "https://www.orovalleyaz.gov/"),
                new("Marana", "0444270", 0.02m, "https://www.marana.com/"),
            ]),
            new("Pinal County", "04021", 0.011m, "https://www.pinalcountyaz.gov/",
            [
                new("Apache Junction", "0402740", 0.031m, "https://www.ajcity.net/"),
                new("Casa Grande", "0410530", 0.025m, "https://www.casagrandeaz.gov/"),
            ]),
        ]),

        new("Arkansas", "AR", "05", 0.065m, "https://www.dfa.arkansas.gov/excise-tax/sales-and-use-tax/",
        [
            new("Pulaski County", "05119", 0.01m, "https://www.pulaskicounty.net/",
            [
                new("Little Rock", "0541000", 0.015m, "https://www.littlerock.gov/"),
                new("North Little Rock", "0550450", 0.01m, "https://www.northlittlerock.gov/"),
                new("Maumelle", "0545490", 0.01m, "https://www.maumelle.org/"),
            ]),
            new("Benton County", "05007", 0.01m, "https://www.bentoncountyar.gov/",
            [
                new("Bentonville", "0505810", 0.02m, "https://www.bentonvillear.com/"),
                new("Rogers", "0562150", 0.02m, "https://www.rogersar.gov/"),
                new("Fayetteville", "0523290", 0.02m, "https://www.fayetteville-ar.gov/"),
            ]),
            new("Sebastian County", "05131", 0.01m, "https://www.sebastiancounty.org/",
            [
                new("Fort Smith", "0527370", 0.02m, "https://www.fortsmithar.gov/"),
                new("Van Buren", "0272730", 0.02m, "https://www.vanburenarkansas.org/"),
            ]),
        ]),

        new("California", "CA", "06", 0.0725m, "https://www.cdtfa.ca.gov/",
        [
            new("Los Angeles County", "06037", 0.0125m, "https://www.cdtfa.ca.gov/formspubs/cdtfa95.pdf",
            [
                new("Los Angeles", "0644000", 0.0175m, "https://finance.lacity.org/"),
                new("Long Beach", "0643000", 0.005m, "https://www.longbeach.gov/"),
                new("Glendale", "0630000", 0.005m, "https://www.glendaleca.gov/"),
                new("Santa Clarita", "0670042", 0.00m, "https://www.santa-clarita.com/"),
            ]),
            new("San Diego County", "06073", 0.0025m, "https://www.sandiegocounty.gov/",
            [
                new("San Diego", "0666000", 0.005m, "https://www.sandiego.gov/"),
                new("Chula Vista", "0613392", 0.00m, "https://www.chulavistaca.gov/"),
                new("Oceanside", "0653322", 0.00m, "https://www.ci.oceanside.ca.us/"),
            ]),
            new("Orange County", "06059", 0.005m, "https://www.ocgov.com/",
            [
                new("Anaheim", "0602000", 0.00m, "https://www.anaheim.net/"),
                new("Santa Ana", "0670000", 0.01m, "https://www.santa-ana.org/"),
                new("Irvine", "0636770", 0.00m, "https://www.cityofirvine.org/"),
            ]),
            new("Santa Clara County", "06085", 0.0025m, "https://www.sccgov.org/",
            [
                new("San Jose", "0668000", 0.0025m, "https://www.sanjoseca.gov/"),
                new("Sunnyvale", "0677000", 0.00m, "https://sunnyvale.ca.gov/"),
                new("Fremont", "0626000", 0.00m, "https://www.fremont.gov/"),
            ]),
            new("San Francisco County", "06075", 0.0125m, "https://sfcontroller.org/",
            [
                new("San Francisco", "0667000", 0.00m, "https://sfcontroller.org/"),
            ]),
            new("Alameda County", "06001", 0.005m, "https://www.acgov.org/",
            [
                new("Oakland", "0653000", 0.005m, "https://www.oaklandca.gov/"),
                new("Fremont", "06001-FRE", 0.00m, "https://www.fremont.gov/"),
                new("Hayward", "0633000", 0.01m, "https://www.hayward-ca.gov/"),
            ]),
        ]),

        new("Colorado", "CO", "08", 0.029m, "https://tax.colorado.gov/sales-use-tax",
        [
            new("Denver County", "08031", 0.0481m, "https://www.denvergov.org/",
            [
                new("Denver", "0820000", 0.00m, "https://www.denvergov.org/"),
            ]),
            new("El Paso County", "08041", 0.01m, "https://www.elpasoco.com/",
            [
                new("Colorado Springs", "0816000", 0.0315m, "https://coloradosprings.gov/"),
                new("Fountain", "0827185", 0.03m, "https://www.fountaincolorado.org/"),
            ]),
            new("Jefferson County", "08059", 0.005m, "https://www.jeffco.us/",
            [
                new("Lakewood", "0843000", 0.03m, "https://www.lakewood.org/"),
                new("Arvada", "0804330", 0.031m, "https://www.arvada.org/"),
                new("Westminster", "0883835", 0.0305m, "https://www.cityofwestminster.us/"),
            ]),
            new("Arapahoe County", "08005", 0.00m, "https://www.arapahoegov.com/",
            [
                new("Aurora", "0804000", 0.04m, "https://www.auroragov.org/"),
                new("Englewood", "0824785", 0.038m, "https://www.englewoodco.gov/"),
                new("Centennial", "0812415", 0.025m, "https://www.centennialco.gov/"),
            ]),
        ]),

        new("Connecticut", "CT", "09", 0.0635m, "https://portal.ct.gov/DRS/Sales-Tax/",
        [
            new("Hartford County", "09003", 0.00m, "https://www.hartfordct.gov/",
            [
                new("Hartford", "0937000", 0.00m, "https://www.hartfordct.gov/"),
                new("West Hartford", "0988580", 0.00m, "https://www.westhartfordct.gov/"),
                new("Glastonbury", "0931070", 0.00m, "https://www.glastonbury-ct.gov/"),
            ]),
            new("New Haven County", "09009", 0.00m, "https://www.newhavenct.gov/",
            [
                new("New Haven", "0952000", 0.00m, "https://www.newhavenct.gov/"),
                new("Milford", "0946450", 0.00m, "https://www.ci.milford.ct.us/"),
                new("West Haven", "0988340", 0.00m, "https://www.cityofwesthaven.com/"),
            ]),
            new("Fairfield County", "09001", 0.00m, "https://www.bridgeportct.gov/",
            [
                new("Bridgeport", "0908000", 0.00m, "https://www.bridgeportct.gov/"),
                new("Stamford", "0973000", 0.00m, "https://www.stamfordct.gov/"),
                new("Norwalk", "0955990", 0.00m, "https://www.norwalkct.org/"),
            ]),
        ]),

        new("Delaware", "DE", "10", 0.00m, "https://revenue.delaware.gov/",
        [
            new("New Castle County", "10003", 0.00m, "https://www.nccde.org/",
            [
                new("Wilmington", "1077580", 0.00m, "https://www.wilmingtonde.gov/"),
                new("Newark", "1050670", 0.00m, "https://www.newark.de.us/"),
            ]),
            new("Kent County", "10001", 0.00m, "https://www.co.kent.de.us/",
            [
                new("Dover", "1020440", 0.00m, "https://www.cityofdover.com/"),
            ]),
            new("Sussex County", "10005", 0.00m, "https://www.sussexcountyde.gov/",
            [
                new("Georgetown", "1028560", 0.00m, "https://www.georgetownde.org/"),
                new("Lewes", "1041100", 0.00m, "https://www.ci.lewes.de.us/"),
            ]),
        ]),

        new("Florida", "FL", "12", 0.06m, "https://floridarevenue.com/taxes/taxesfees/Pages/sales_tax.aspx",
        [
            new("Miami-Dade County", "12086", 0.01m, "https://www.miamidade.gov/",
            [
                new("Miami", "1245000", 0.00m, "https://www.miamigov.com/"),
                new("Hialeah", "1230000", 0.00m, "https://www.hialeahfl.gov/"),
                new("Coral Gables", "1214250", 0.00m, "https://www.coralgables.com/"),
            ]),
            new("Broward County", "12011", 0.01m, "https://www.broward.org/",
            [
                new("Fort Lauderdale", "1224000", 0.00m, "https://www.fortlauderdale.gov/"),
                new("Hollywood", "1232000", 0.00m, "https://www.hollywoodfl.org/"),
                new("Pembroke Pines", "1254075", 0.00m, "https://www.ppines.com/"),
            ]),
            new("Palm Beach County", "12099", 0.01m, "https://www.pbcgov.org/",
            [
                new("West Palm Beach", "1276000", 0.00m, "https://www.wpb.org/"),
                new("Boca Raton", "1207300", 0.00m, "https://www.myboca.us/"),
                new("Delray Beach", "1217200", 0.00m, "https://www.delraybeachfl.gov/"),
            ]),
            new("Orange County", "12095", 0.005m, "https://www.ocfl.net/",
            [
                new("Orlando", "1253000", 0.00m, "https://www.orlando.gov/"),
                new("Kissimmee", "1238250", 0.00m, "https://www.kissimmee.gov/"),
                new("Apopka", "1201700", 0.00m, "https://www.apopka.net/"),
            ]),
            new("Hillsborough County", "12057", 0.015m, "https://www.hillsboroughcounty.org/",
            [
                new("Tampa", "1271000", 0.00m, "https://www.tampagov.net/"),
                new("Temple Terrace", "1271900", 0.00m, "https://www.templeterrace.com/"),
                new("Plant City", "1256675", 0.00m, "https://www.plantcitygov.com/"),
            ]),
        ]),

        new("Georgia", "GA", "13", 0.04m, "https://dor.georgia.gov/taxes/business-taxes/sales-use-tax",
        [
            new("Fulton County", "13121", 0.03m, "https://www.fultoncountyga.gov/",
            [
                new("Atlanta", "1304000", 0.019m, "https://www.atlantaga.gov/"),
                new("Alpharetta", "1301696", 0.00m, "https://www.alpharetta.ga.us/"),
                new("Sandy Springs", "1367284", 0.00m, "https://www.sandyspringsga.gov/"),
            ]),
            new("Gwinnett County", "13135", 0.03m, "https://www.gwinnettcounty.com/",
            [
                new("Lawrenceville", "1343908", 0.00m, "https://www.lawrencevillegain.gov/"),
                new("Duluth", "1324108", 0.00m, "https://www.duluthga.net/"),
                new("Norcross", "1355908", 0.00m, "https://www.norcrossga.net/"),
            ]),
            new("Cobb County", "13067", 0.02m, "https://www.cobbcounty.org/",
            [
                new("Marietta", "1349756", 0.00m, "https://www.mariettaga.gov/"),
                new("Smyrna", "1371492", 0.00m, "https://www.smyrnacity.com/"),
                new("Kennesaw", "1342256", 0.00m, "https://www.kennesaw-ga.gov/"),
            ]),
            new("DeKalb County", "13089", 0.04m, "https://www.dekalbcountyga.gov/",
            [
                new("Decatur", "1321592", 0.00m, "https://www.decaturga.com/"),
                new("Tucker", "1374884", 0.00m, "https://www.tuckerga.gov/"),
            ]),
        ]),

        new("Hawaii", "HI", "15", 0.04m, "https://tax.hawaii.gov/geninfo/whatisget/",
        [
            new("Honolulu County", "15003", 0.005m, "https://www.honolulu.gov/",
            [
                new("Honolulu", "1571550", 0.00m, "https://www.honolulu.gov/"),
                new("Pearl City", "1562600", 0.00m, "https://www.honolulu.gov/"),
            ]),
            new("Hawaii County", "15001", 0.0025m, "https://www.hawaiicounty.gov/",
            [
                new("Hilo", "1515070", 0.00m, "https://www.hawaiicounty.gov/"),
                new("Kailua-Kona", "1520750", 0.00m, "https://www.hawaiicounty.gov/"),
            ]),
            new("Maui County", "15009", 0.005m, "https://www.mauicounty.gov/",
            [
                new("Kahului", "1520100", 0.00m, "https://www.mauicounty.gov/"),
                new("Wailuku", "1580100", 0.00m, "https://www.mauicounty.gov/"),
            ]),
        ]),

        new("Idaho", "ID", "16", 0.06m, "https://tax.idaho.gov/taxes/sales-use/",
        [
            new("Ada County", "16001", 0.00m, "https://adacounty.id.gov/",
            [
                new("Boise", "1608830", 0.00m, "https://www.cityofboise.org/"),
                new("Meridian", "1652120", 0.00m, "https://www.meridiancity.org/"),
                new("Eagle", "1623060", 0.00m, "https://www.cityofeagle.org/"),
            ]),
            new("Canyon County", "16027", 0.00m, "https://www.canyonco.org/",
            [
                new("Nampa", "1656260", 0.00m, "https://www.cityofnampa.us/"),
                new("Caldwell", "1612250", 0.00m, "https://www.cityofcaldwell.org/"),
            ]),
            new("Kootenai County", "16055", 0.00m, "https://kcgov.us/",
            [
                new("Coeur d'Alene", "1617230", 0.00m, "https://www.cdaid.org/"),
                new("Post Falls", "1662920", 0.00m, "https://www.postfallsidaho.org/"),
            ]),
        ]),

        new("Illinois", "IL", "17", 0.0625m, "https://tax.illinois.gov/research/taxinformation/sales/rot.html",
        [
            new("Cook County", "17031", 0.0175m, "https://tax.illinois.gov/research/taxinformation/sales/rot.html",
            [
                new("Chicago", "1714000", 0.0225m, "https://www.chicago.gov/city/en/depts/fin.html"),
                new("Aurora", "1705092", 0.0175m, "https://www.aurora-il.org/"),
                new("Evanston", "1724582", 0.015m, "https://www.cityofevanston.org/"),
                new("Cicero", "1714351", 0.015m, "https://www.thetownofcicero.com/"),
            ]),
            new("DuPage County", "17043", 0.00m, "https://www.dupagecounty.gov/",
            [
                new("Naperville", "1753000", 0.0175m, "https://www.naperville.il.us/"),
                new("Wheaton", "1781048", 0.0175m, "https://www.wheaton.il.us/"),
                new("Bolingbrook", "1707133", 0.0175m, "https://www.bolingbrook.com/"),
            ]),
            new("Lake County", "17097", 0.00m, "https://www.lakecountyil.gov/",
            [
                new("Waukegan", "1779293", 0.0175m, "https://www.waukeganil.gov/"),
                new("Round Lake Beach", "1765442", 0.0175m, "https://www.rlb.org/"),
                new("North Chicago", "1753169", 0.0175m, "https://northchicago.org/"),
            ]),
            new("Will County", "17197", 0.00m, "https://www.willcountyillinois.com/",
            [
                new("Joliet", "1739178", 0.0175m, "https://www.joliet.gov/"),
                new("Romeoville", "1765078", 0.0175m, "https://www.romeoville.org/"),
                new("Lockport", "1744277", 0.0175m, "https://www.lockport.org/"),
            ]),
            new("Kane County", "17089", 0.00m, "https://www.countyofkane.org/",
            [
                new("Elgin", "1723074", 0.0175m, "https://www.cityofelgin.org/"),
                new("Aurora", "17089-AUR", 0.0175m, "https://www.aurora-il.org/"),
            ]),
        ]),

        new("Indiana", "IN", "18", 0.07m, "https://www.in.gov/dor/tax-forms/sales-tax/",
        [
            new("Marion County", "18097", 0.02m, "https://www.indy.gov/",
            [
                new("Indianapolis", "1836003", 0.00m, "https://www.indy.gov/"),
                new("Lawrence", "1842426", 0.00m, "https://www.thelawrencecenter.org/"),
            ]),
            new("Lake County", "18089", 0.00m, "https://www.lakecountyin.org/",
            [
                new("Gary", "1826802", 0.00m, "https://www.gary.gov/"),
                new("Hammond", "1831482", 0.00m, "https://www.gohammond.com/"),
            ]),
            new("Allen County", "18003", 0.00m, "https://www.allencounty.us/",
            [
                new("Fort Wayne", "1822000", 0.00m, "https://www.cityoffortwayne.org/"),
                new("New Haven", "1852938", 0.00m, "https://www.newhaven.in.gov/"),
            ]),
            new("Hamilton County", "18057", 0.00m, "https://www.hamiltoncounty.in.gov/",
            [
                new("Carmel", "1810342", 0.00m, "https://www.carmel.in.gov/"),
                new("Fishers", "1822344", 0.00m, "https://www.fishers.in.us/"),
                new("Noblesville", "1854180", 0.00m, "https://www.noblesville.in.gov/"),
            ]),
        ]),

        new("Iowa", "IA", "19", 0.06m, "https://tax.iowa.gov/sales-and-use-tax",
        [
            new("Polk County", "19153", 0.01m, "https://www.polkcountyiowa.gov/",
            [
                new("Des Moines", "1921000", 0.00m, "https://www.dsm.city/"),
                new("Ankeny", "1901930", 0.00m, "https://www.ankenyiowa.gov/"),
                new("West Des Moines", "1983910", 0.00m, "https://www.wdm.iowa.gov/"),
            ]),
            new("Linn County", "19113", 0.01m, "https://www.linncounty.org/",
            [
                new("Cedar Rapids", "1912000", 0.00m, "https://www.cedar-rapids.org/"),
                new("Marion", "1848370", 0.00m, "https://www.cityofmarion.org/"),
            ]),
            new("Scott County", "19163", 0.01m, "https://www.scottcountyiowa.gov/",
            [
                new("Davenport", "1919000", 0.00m, "https://www.cityofdavenport.com/"),
                new("Bettendorf", "1905920", 0.00m, "https://www.bettendorf.org/"),
            ]),
        ]),

        new("Kansas", "KS", "20", 0.065m, "https://www.ksrevenue.gov/salesratecurrent.html",
        [
            new("Johnson County", "20091", 0.005m, "https://www.jocogov.org/",
            [
                new("Overland Park", "2053775", 0.011m, "https://www.opkansas.org/"),
                new("Olathe", "2052575", 0.011m, "https://www.olatheks.org/"),
                new("Shawnee", "2065325", 0.0165m, "https://www.cityofshawnee.org/"),
            ]),
            new("Sedgwick County", "20173", 0.01m, "https://www.sedgwickcounty.org/",
            [
                new("Wichita", "2079000", 0.01m, "https://www.wichita.gov/"),
                new("Derby", "2017875", 0.01m, "https://www.derbyweb.com/"),
            ]),
            new("Wyandotte County", "20209", 0.01m, "https://www.wycokck.org/",
            [
                new("Kansas City", "2036000", 0.02m, "https://www.wycokck.org/"),
                new("Bonner Springs", "2008200", 0.0225m, "https://www.bonnersprings.org/"),
            ]),
        ]),

        new("Kentucky", "KY", "21", 0.06m, "https://revenue.ky.gov/Business/Sales-and-Use-Tax/",
        [
            new("Jefferson County", "21111", 0.00m, "https://www.louisvilleky.gov/",
            [
                new("Louisville", "2148000", 0.00m, "https://www.louisvilleky.gov/"),
                new("St. Matthews", "2171166", 0.00m, "https://www.stmatthewsky.gov/"),
            ]),
            new("Fayette County", "21067", 0.00m, "https://www.lexingtonky.gov/",
            [
                new("Lexington", "2146027", 0.00m, "https://www.lexingtonky.gov/"),
            ]),
            new("Kenton County", "21117", 0.00m, "https://www.kentoncounty.org/",
            [
                new("Covington", "2118574", 0.00m, "https://www.covingtonky.gov/"),
                new("Florence", "2127982", 0.00m, "https://florencekentucky.com/"),
            ]),
        ]),

        new("Louisiana", "LA", "22", 0.0445m, "https://revenue.louisiana.gov/SalesTax",
        [
            new("Orleans Parish", "22071", 0.05m, "https://www.stpso.com/",
            [
                new("New Orleans", "2255000", 0.00m, "https://www.nola.gov/"),
            ]),
            new("Jefferson Parish", "22051", 0.0475m, "https://www.jeffparish.net/",
            [
                new("Metairie", "2249685", 0.00m, "https://www.jeffparish.net/"),
                new("Kenner", "2239095", 0.00m, "https://www.kenner.la.us/"),
            ]),
            new("East Baton Rouge Parish", "22033", 0.0463m, "https://www.ebrp.net/",
            [
                new("Baton Rouge", "2205000", 0.00m, "https://www.brla.gov/"),
                new("Zachary", "2288080", 0.00m, "https://www.zachary-la.com/"),
            ]),
            new("St. Tammany Parish", "22103", 0.0475m, "https://www.stpgov.org/",
            [
                new("Covington", "2218730", 0.00m, "https://www.covla.com/"),
                new("Slidell", "2269180", 0.00m, "https://www.cityofslidell.com/"),
            ]),
        ]),

        new("Maine", "ME", "23", 0.055m, "https://www.maine.gov/revenue/taxes/sales-use-service-provider-tax",
        [
            new("Cumberland County", "23005", 0.00m, "https://www.cumberlandcounty.org/",
            [
                new("Portland", "2360545", 0.00m, "https://www.portlandmaine.gov/"),
                new("South Portland", "2370380", 0.00m, "https://www.southportland.org/"),
            ]),
            new("York County", "23031", 0.00m, "https://www.yorkcountymaine.gov/",
            [
                new("Saco", "2365905", 0.00m, "https://www.sacomaine.org/"),
                new("Biddeford", "2306690", 0.00m, "https://www.biddefordmaine.org/"),
            ]),
            new("Penobscot County", "23019", 0.00m, "https://www.penobscotcounty.net/",
            [
                new("Bangor", "2305260", 0.00m, "https://www.bangormaine.gov/"),
                new("Brewer", "2308695", 0.00m, "https://www.brewermaine.gov/"),
            ]),
        ]),

        new("Maryland", "MD", "24", 0.06m, "https://www.marylandtaxes.gov/business/sales-and-use-tax/",
        [
            new("Montgomery County", "24031", 0.00m, "https://www.montgomerycountymd.gov/",
            [
                new("Rockville", "2467675", 0.00m, "https://www.rockvillemd.gov/"),
                new("Gaithersburg", "2431175", 0.00m, "https://www.gaithersburgmd.gov/"),
                new("Silver Spring", "2473775", 0.00m, "https://www.silverspringmd.gov/"),
            ]),
            new("Prince George's County", "24033", 0.00m, "https://www.princegeorgescountymd.gov/",
            [
                new("Bowie", "2410025", 0.00m, "https://www.cityofbowie.org/"),
                new("College Park", "2418750", 0.00m, "https://www.collegeparkmd.gov/"),
                new("Greenbelt", "2333200", 0.00m, "https://www.greenbeltmd.gov/"),
            ]),
            new("Baltimore County", "24005", 0.00m, "https://www.baltimorecountymd.gov/",
            [
                new("Towson", "2480175", 0.00m, "https://www.baltimorecountymd.gov/"),
                new("Catonsville", "2413875", 0.00m, "https://www.baltimorecountymd.gov/"),
            ]),
            new("Baltimore City", "24510", 0.00m, "https://www.baltimorecity.gov/",
            [
                new("Baltimore", "2404000", 0.00m, "https://www.baltimorecity.gov/"),
            ]),
        ]),

        new("Massachusetts", "MA", "25", 0.0625m, "https://www.mass.gov/info-details/sales-and-use-tax",
        [
            new("Suffolk County", "25025", 0.00m, "https://www.boston.gov/",
            [
                new("Boston", "2507000", 0.00m, "https://www.boston.gov/"),
                new("Revere", "2555745", 0.00m, "https://www.revere.org/"),
            ]),
            new("Middlesex County", "25017", 0.00m, "https://www.mass.gov/",
            [
                new("Cambridge", "2511000", 0.00m, "https://www.cambridgema.gov/"),
                new("Lowell", "2537000", 0.00m, "https://www.lowellma.gov/"),
                new("Somerville", "2567000", 0.00m, "https://www.somervillema.gov/"),
            ]),
            new("Worcester County", "25027", 0.00m, "https://www.worcesterma.gov/",
            [
                new("Worcester", "2582000", 0.00m, "https://www.worcesterma.gov/"),
                new("Leominster", "2536015", 0.00m, "https://www.leominster-ma.gov/"),
            ]),
        ]),

        new("Michigan", "MI", "26", 0.06m, "https://www.michigan.gov/taxes/business-taxes/sales-and-use-taxes",
        [
            new("Wayne County", "26163", 0.00m, "https://www.waynecounty.com/",
            [
                new("Detroit", "2622000", 0.00m, "https://www.detroitmi.gov/"),
                new("Dearborn", "2621000", 0.00m, "https://www.cityofdearborn.org/"),
                new("Livonia", "2649000", 0.00m, "https://www.ci.livonia.mi.us/"),
            ]),
            new("Oakland County", "26125", 0.00m, "https://www.oakgov.com/",
            [
                new("Troy", "2679000", 0.00m, "https://www.troymi.gov/"),
                new("Sterling Heights", "2475440", 0.00m, "https://www.sterling-heights.net/"),
                new("Rochester Hills", "2669035", 0.00m, "https://www.rochesterhills.org/"),
            ]),
            new("Kent County", "26081", 0.00m, "https://www.accesskent.com/",
            [
                new("Grand Rapids", "2634000", 0.00m, "https://www.grandrapidsmi.gov/"),
                new("Wyoming", "2684520", 0.00m, "https://www.wyomingmi.gov/"),
                new("Kentwood", "2643580", 0.00m, "https://www.kentwoodmi.gov/"),
            ]),
        ]),

        new("Minnesota", "MN", "27", 0.06875m, "https://www.revenue.state.mn.us/sales-and-use-tax",
        [
            new("Hennepin County", "27053", 0.015m, "https://www.hennepin.us/",
            [
                new("Minneapolis", "2743000", 0.0075m, "https://www.minneapolismn.gov/"),
                new("Bloomington", "2706382", 0.00m, "https://www.bloomingtonmn.gov/"),
                new("Plymouth", "2756896", 0.00m, "https://www.plymouthmn.gov/"),
            ]),
            new("Ramsey County", "27123", 0.01m, "https://www.ramseycounty.us/",
            [
                new("St. Paul", "2758000", 0.0075m, "https://www.stpaul.gov/"),
                new("Maplewood", "2743342", 0.00m, "https://www.maplewoodmn.gov/"),
                new("Roseville", "2757220", 0.00m, "https://www.cityofroseville.com/"),
            ]),
            new("Dakota County", "27037", 0.003m, "https://www.co.dakota.mn.us/",
            [
                new("Eagan", "2718116", 0.00m, "https://www.cityofeagan.com/"),
                new("Apple Valley", "2702194", 0.00m, "https://www.ci.apple-valley.mn.us/"),
                new("Burnsville", "2708950", 0.00m, "https://www.burnsville.org/"),
            ]),
        ]),

        new("Mississippi", "MS", "28", 0.07m, "https://www.dor.ms.gov/business/sales-tax",
        [
            new("Hinds County", "28049", 0.00m, "https://www.hindscountyms.com/",
            [
                new("Jackson", "2836000", 0.00m, "https://www.jacksonms.gov/"),
                new("Clinton", "2814060", 0.00m, "https://www.clintonms.org/"),
            ]),
            new("Harrison County", "28047", 0.00m, "https://www.co.harrison.ms.us/",
            [
                new("Gulfport", "2831020", 0.00m, "https://www.gulfport-ms.gov/"),
                new("Biloxi", "2807420", 0.00m, "https://www.biloxi.ms.us/"),
            ]),
            new("DeSoto County", "28033", 0.00m, "https://www.desotocountyms.gov/",
            [
                new("Southaven", "2869280", 0.00m, "https://www.southaven.org/"),
                new("Olive Branch", "2854640", 0.00m, "https://www.olivebranch.org/"),
            ]),
        ]),

        new("Missouri", "MO", "29", 0.04225m, "https://dor.mo.gov/business/sales/",
        [
            new("St. Louis County", "29189", 0.0163m, "https://revenue.stlouisco.com/",
            [
                new("Clayton", "2913276", 0.01m, "https://www.claytonmo.gov/"),
                new("Kirkwood", "2939026", 0.01m, "https://www.kirkwoodmo.gov/"),
                new("Florissant", "2625448", 0.01m, "https://www.florissantmo.com/"),
            ]),
            new("St. Louis City", "29510", 0.0163m, "https://www.stlouis-mo.gov/",
            [
                new("St. Louis", "2965000", 0.00m, "https://www.stlouis-mo.gov/"),
            ]),
            new("Jackson County", "29095", 0.0088m, "https://www.jacksongov.org/",
            [
                new("Kansas City", "2938000", 0.03375m, "https://www.kcmo.gov/"),
                new("Independence", "2935000", 0.025m, "https://www.ci.independence.mo.us/"),
                new("Lee's Summit", "2941348", 0.02m, "https://www.lees-summit.mo.us/"),
            ]),
            new("Greene County", "29077", 0.0163m, "https://www.greenecountymo.gov/",
            [
                new("Springfield", "2970000", 0.0213m, "https://www.springfieldmo.gov/"),
                new("Republic", "2961922", 0.0213m, "https://www.republicmo.com/"),
            ]),
        ]),

        new("Montana", "MT", "30", 0.00m, "https://mtrevenue.gov/",
        [
            new("Yellowstone County", "30111", 0.00m, "https://www.yellowstonecountymt.gov/",
            [
                new("Billings", "3007600", 0.00m, "https://www.billingsmt.gov/"),
                new("Lockwood", "3044350", 0.00m, "https://www.yellowstonecountymt.gov/"),
            ]),
            new("Cascade County", "30013", 0.00m, "https://www.cascadecountymt.gov/",
            [
                new("Great Falls", "3030475", 0.00m, "https://www.greatfallsmt.net/"),
            ]),
            new("Missoula County", "30063", 0.00m, "https://www.missoulacounty.us/",
            [
                new("Missoula", "3050200", 0.00m, "https://www.ci.missoula.mt.us/"),
            ]),
        ]),

        new("Nebraska", "NE", "31", 0.055m, "https://revenue.nebraska.gov/about/sales-and-use-tax",
        [
            new("Douglas County", "31055", 0.00m, "https://www.douglascounty-ne.gov/",
            [
                new("Omaha", "3137000", 0.015m, "https://www.omaha.gov/"),
                new("Ralston", "3140100", 0.015m, "https://www.ralstonnebraska.com/"),
                new("Papillion", "3137460", 0.015m, "https://www.papillion.org/"),
            ]),
            new("Lancaster County", "31109", 0.00m, "https://www.lancaster.ne.gov/",
            [
                new("Lincoln", "3128000", 0.0175m, "https://www.lincoln.ne.gov/"),
                new("Waverly", "3151170", 0.01m, "https://www.waverly-ne.gov/"),
            ]),
            new("Sarpy County", "31153", 0.00m, "https://www.sarpy.com/",
            [
                new("Bellevue", "3104920", 0.015m, "https://www.bellevue.net/"),
                new("Gretna", "3120640", 0.015m, "https://www.gretnane.org/"),
            ]),
        ]),

        new("Nevada", "NV", "32", 0.0685m, "https://tax.nv.gov/Sales/",
        [
            new("Clark County", "32003", 0.0365m, "https://www.clarkcountynv.gov/",
            [
                new("Las Vegas", "3240000", 0.0038m, "https://www.lasvegasnevada.gov/"),
                new("Henderson", "3231900", 0.00m, "https://www.cityofhenderson.com/"),
                new("North Las Vegas", "3251800", 0.00m, "https://www.cityofnorthlasvegas.com/"),
                new("Paradise", "32003-PAR", 0.00m, "https://www.clarkcountynv.gov/"),
            ]),
            new("Washoe County", "32031", 0.0365m, "https://www.washoecounty.gov/",
            [
                new("Reno", "3260600", 0.00m, "https://www.reno.gov/"),
                new("Sparks", "3268250", 0.00m, "https://www.cityofsparks.us/"),
            ]),
        ]),

        new("New Hampshire", "NH", "33", 0.00m, "https://www.revenue.nh.gov/",
        [
            new("Hillsborough County", "33011", 0.00m, "https://www.hillsboroughcountynh.org/",
            [
                new("Manchester", "3345140", 0.00m, "https://www.manchesternh.gov/"),
                new("Nashua", "3349460", 0.00m, "https://www.nashuanh.gov/"),
            ]),
            new("Rockingham County", "33015", 0.00m, "https://www.co.rockingham.nh.us/",
            [
                new("Derry", "3317620", 0.00m, "https://www.derry-nh.org/"),
                new("Salem", "3362820", 0.00m, "https://www.salemnhplanning.org/"),
            ]),
            new("Merrimack County", "33013", 0.00m, "https://www.merrimackcounty.net/",
            [
                new("Concord", "3314200", 0.00m, "https://www.concordnh.gov/"),
                new("Pembroke", "3353460", 0.00m, "https://www.town.pembroke.nh.us/"),
            ]),
        ]),

        new("New Jersey", "NJ", "34", 0.06625m, "https://www.nj.gov/treasury/taxation/su_overview.shtml",
        [
            new("Bergen County", "34003", 0.00m, "https://www.co.bergen.nj.us/",
            [
                new("Newark", "3451000", 0.03125m, "https://www.newarknj.gov/"),
                new("Jersey City", "3436000", 0.00m, "https://www.jerseycitynj.gov/"),
                new("Fort Lee", "3424300", 0.00m, "https://www.fortleenj.org/"),
            ]),
            new("Essex County", "34013", 0.00m, "https://www.essexcountynj.org/",
            [
                new("Newark", "34013-NEW", 0.03125m, "https://www.newarknj.gov/"),
                new("East Orange", "3421480", 0.00m, "https://www.eastorange-nj.gov/"),
            ]),
            new("Hudson County", "34017", 0.00m, "https://www.hudsoncountynj.org/",
            [
                new("Jersey City", "34017-JC", 0.00m, "https://www.jerseycitynj.gov/"),
                new("Bayonne", "3403280", 0.00m, "https://www.bayonnenj.org/"),
            ]),
            new("Middlesex County", "34023", 0.00m, "https://www.co.middlesex.nj.us/",
            [
                new("New Brunswick", "3450820", 0.00m, "https://www.cityofnewbrunswick.org/"),
                new("Woodbridge", "3481330", 0.00m, "https://www.twp.woodbridge.nj.us/"),
            ]),
        ]),

        new("New Mexico", "NM", "35", 0.05125m, "https://www.tax.newmexico.gov/businesses/gross-receipts-tax/",
        [
            new("Bernalillo County", "35001", 0.021875m, "https://www.bernco.gov/",
            [
                new("Albuquerque", "3502000", 0.00m, "https://www.cabq.gov/"),
                new("Rio Rancho", "3564930", 0.00m, "https://www.rrnm.gov/"),
            ]),
            new("Santa Fe County", "35049", 0.015m, "https://www.santafecounty.gov/",
            [
                new("Santa Fe", "3570500", 0.003125m, "https://www.santafenm.gov/"),
                new("Edgewood", "3522660", 0.0125m, "https://www.edgewood-nm.gov/"),
            ]),
            new("Dona Ana County", "35013", 0.018125m, "https://www.donaanacounty.org/",
            [
                new("Las Cruces", "3539700", 0.00m, "https://www.las-cruces.org/"),
                new("Sunland Park", "3572150", 0.00m, "https://www.sunlandpark-nm.gov/"),
            ]),
        ]),

        new("New York", "NY", "36", 0.04m, "https://www.tax.ny.gov/bus/st/stidx.htm",
        [
            new("New York County", "36061", 0.045m, "https://www.nyc.gov/site/finance/tax/",
            [
                new("Manhattan", "3651000", 0.00m, "https://www.nyc.gov/"),
            ]),
            new("Kings County", "36047", 0.045m, "https://www.nyc.gov/site/finance/tax/",
            [
                new("Brooklyn", "3636000", 0.00m, "https://www.nyc.gov/"),
            ]),
            new("Queens County", "36081", 0.045m, "https://www.nyc.gov/site/finance/tax/",
            [
                new("Queens", "3663726", 0.00m, "https://www.nyc.gov/"),
            ]),
            new("Bronx County", "36005", 0.045m, "https://www.nyc.gov/site/finance/tax/",
            [
                new("Bronx", "3605130", 0.00m, "https://www.nyc.gov/"),
            ]),
            new("Erie County", "36029", 0.0475m, "https://www.erie.gov/finance/",
            [
                new("Buffalo", "3611000", 0.00m, "https://www.buffalony.gov/"),
                new("Cheektowaga", "3613806", 0.00m, "https://www.tocny.org/"),
                new("Tonawanda", "3674763", 0.00m, "https://www.tonawanda.ny.us/"),
            ]),
            new("Monroe County", "36055", 0.04m, "https://www.monroecounty.gov/",
            [
                new("Rochester", "3663000", 0.00m, "https://www.cityofrochester.gov/"),
                new("Irondequoit", "3637044", 0.00m, "https://www.irondequoit.org/"),
            ]),
            new("Onondaga County", "36067", 0.04m, "https://www.ongov.net/finance/",
            [
                new("Syracuse", "3673000", 0.00m, "https://www.syrgov.net/"),
                new("Cicero", "36067-CIC", 0.00m, "https://www.ciceronewyork.net/"),
            ]),
        ]),

        new("North Carolina", "NC", "37", 0.0475m, "https://www.ncdor.gov/taxes-forms/sales-and-use-tax",
        [
            new("Mecklenburg County", "37119", 0.0275m, "https://www.mecknc.gov/",
            [
                new("Charlotte", "3712000", 0.00m, "https://www.charlottenc.gov/"),
                new("Huntersville", "3733120", 0.00m, "https://www.huntersville.org/"),
                new("Concord", "3714700", 0.00m, "https://www.concordnc.gov/"),
            ]),
            new("Wake County", "37183", 0.0275m, "https://www.wake.gov/",
            [
                new("Raleigh", "3755000", 0.00m, "https://raleighnc.gov/"),
                new("Cary", "3710740", 0.00m, "https://www.carync.gov/"),
                new("Durham", "3719000", 0.00m, "https://durhamnc.gov/"),
            ]),
            new("Guilford County", "37081", 0.0225m, "https://www.guilfordcountync.gov/",
            [
                new("Greensboro", "3728000", 0.00m, "https://www.greensboro-nc.gov/"),
                new("High Point", "3731400", 0.00m, "https://www.highpointnc.gov/"),
            ]),
            new("Forsyth County", "37067", 0.0225m, "https://www.forsyth.cc/",
            [
                new("Winston-Salem", "3774440", 0.00m, "https://www.cityofws.org/"),
                new("Kernersville", "3736140", 0.00m, "https://www.kernersvillenc.com/"),
            ]),
        ]),

        new("North Dakota", "ND", "38", 0.05m, "https://www.tax.nd.gov/business/sales-and-use-tax",
        [
            new("Cass County", "38017", 0.00m, "https://www.casscountynd.gov/",
            [
                new("Fargo", "3825700", 0.02m, "https://www.fargond.gov/"),
                new("West Fargo", "3885500", 0.02m, "https://www.westfargond.gov/"),
            ]),
            new("Burleigh County", "38015", 0.00m, "https://www.burleighco.com/",
            [
                new("Bismarck", "3807200", 0.01m, "https://www.bismarcknd.gov/"),
                new("Lincoln", "3845260", 0.02m, "https://www.lincolnnorthdakota.com/"),
            ]),
            new("Grand Forks County", "38035", 0.00m, "https://www.gfcounty.nd.gov/",
            [
                new("Grand Forks", "3832060", 0.02m, "https://www.grandforksgov.com/"),
            ]),
        ]),

        new("Ohio", "OH", "39", 0.0575m, "https://tax.ohio.gov/wps/portal/gov/tax/business/ohio-business-taxes/sales-and-use",
        [
            new("Franklin County", "39049", 0.0125m, "https://treasurer.franklincountyohio.gov/",
            [
                new("Columbus", "3918000", 0.0175m, "https://www.columbus.gov/"),
                new("Dublin", "3921532", 0.00m, "https://dublinohiousa.gov/"),
                new("Hilliard", "3935728", 0.00m, "https://www.hilliardohio.gov/"),
            ]),
            new("Cuyahoga County", "39035", 0.025m, "https://treasurer.cuyahogacounty.gov/",
            [
                new("Cleveland", "3916000", 0.02m, "https://www.clevelandohio.gov/"),
                new("Parma", "3960242", 0.00m, "https://cityofparma-oh.gov/"),
                new("Lakewood", "3943554", 0.00m, "https://www.onelakewood.com/"),
            ]),
            new("Hamilton County", "39061", 0.014m, "https://www.hamiltoncountyohio.gov/",
            [
                new("Cincinnati", "3915000", 0.015m, "https://www.cincinnati-oh.gov/"),
                new("Norwood", "3958198", 0.00m, "https://www.norwood-ohio.com/"),
            ]),
            new("Summit County", "39153", 0.0163m, "https://www.summitagov.com/",
            [
                new("Akron", "3901000", 0.023m, "https://www.akronohio.gov/"),
                new("Cuyahoga Falls", "3919778", 0.00m, "https://www.cuyahogafalls.org/"),
            ]),
        ]),

        new("Oklahoma", "OK", "40", 0.045m, "https://www.ok.gov/tax/Businesses/Tax_Types/Sales_Tax/",
        [
            new("Oklahoma County", "40109", 0.02875m, "https://www.oklahomacounty.org/",
            [
                new("Oklahoma City", "4055000", 0.00m, "https://www.okc.gov/"),
                new("Edmond", "4023200", 0.0375m, "https://www.edmondok.com/"),
                new("Midwest City", "4047950", 0.03m, "https://www.midwestcitytransparency.com/"),
            ]),
            new("Tulsa County", "40143", 0.02167m, "https://www.tulsacounty.org/",
            [
                new("Tulsa", "4075000", 0.035m, "https://www.cityoftulsa.org/"),
                new("Broken Arrow", "4009908", 0.035m, "https://www.brokenarrowok.gov/"),
                new("Owasso", "4054650", 0.03m, "https://www.cityofowasso.com/"),
            ]),
            new("Cleveland County", "40027", 0.02875m, "https://www.clevelandcountyok.com/",
            [
                new("Norman", "4052500", 0.0375m, "https://www.normanok.gov/"),
                new("Moore", "4049200", 0.04m, "https://www.cityofmoore.com/"),
            ]),
        ]),

        new("Oregon", "OR", "41", 0.00m, "https://www.oregon.gov/dor/programs/businesses/Pages/Sales-Tax.aspx",
        [
            new("Multnomah County", "41051", 0.00m, "https://www.multco.us/",
            [
                new("Portland", "4159000", 0.00m, "https://www.portland.gov/"),
                new("Gresham", "4131250", 0.00m, "https://greshamoregon.gov/"),
            ]),
            new("Washington County", "41067", 0.00m, "https://www.co.washington.or.us/",
            [
                new("Hillsboro", "4134100", 0.00m, "https://www.hillsboro-oregon.gov/"),
                new("Beaverton", "4105350", 0.00m, "https://www.beavertonoregon.gov/"),
                new("Tigard", "4173650", 0.00m, "https://www.tigard-or.gov/"),
            ]),
            new("Lane County", "41039", 0.00m, "https://www.lanecounty.org/",
            [
                new("Eugene", "4123850", 0.00m, "https://www.eugene-or.gov/"),
                new("Springfield", "4171950", 0.00m, "https://www.springfield-or.gov/"),
            ]),
        ]),

        new("Pennsylvania", "PA", "42", 0.06m, "https://www.revenue.pa.gov/TaxTypes/SUT/Pages/default.aspx",
        [
            new("Philadelphia County", "42101", 0.02m, "https://www.phila.gov/services/payments-assistance-taxes/",
            [
                new("Philadelphia", "4260000", 0.00m, "https://www.phila.gov/"),
            ]),
            new("Allegheny County", "42003", 0.01m, "https://www.alleghenycounty.us/",
            [
                new("Pittsburgh", "4261000", 0.00m, "https://pittsburghpa.gov/"),
                new("Mt. Lebanon", "4251960", 0.00m, "https://www.mtlebanon.org/"),
                new("Bethel Park", "4206296", 0.00m, "https://www.bethelpark.net/"),
            ]),
            new("Montgomery County", "42091", 0.00m, "https://www.montcopa.org/",
            [
                new("Norristown", "4254688", 0.00m, "https://www.norristownpa.gov/"),
                new("King of Prussia", "42091-KOP", 0.00m, "https://www.montcopa.org/"),
                new("Plymouth Meeting", "42091-PM", 0.00m, "https://www.plymouthtownship.org/"),
            ]),
            new("Bucks County", "42017", 0.00m, "https://www.buckscounty.gov/",
            [
                new("Levittown", "4242472", 0.00m, "https://www.twp.bristol.pa.us/"),
                new("Newtown Township", "4252736", 0.00m, "https://www.newtowntownshipbucks.com/"),
            ]),
        ]),

        new("Rhode Island", "RI", "44", 0.07m, "https://tax.ri.gov/tax-sections/sales-use-excise-tax",
        [
            new("Providence County", "44007", 0.00m, "https://www.providenceri.gov/",
            [
                new("Providence", "4459000", 0.00m, "https://www.providenceri.gov/"),
                new("Cranston", "4418580", 0.00m, "https://www.cranston.ri.gov/"),
                new("Pawtucket", "4454640", 0.00m, "https://www.pawtucketri.com/"),
            ]),
            new("Kent County", "44003", 0.00m, "https://www.kentcountyri.org/",
            [
                new("Warwick", "4480780", 0.00m, "https://www.warwickri.gov/"),
                new("West Warwick", "4483960", 0.00m, "https://www.westwarwickri.org/"),
            ]),
        ]),

        new("South Carolina", "SC", "45", 0.06m, "https://dor.sc.gov/tax/sales",
        [
            new("Richland County", "45079", 0.01m, "https://www.richlandcountysc.gov/",
            [
                new("Columbia", "4516000", 0.00m, "https://www.columbiasc.gov/"),
                new("Forest Acres", "4525810", 0.00m, "https://www.forestacres.org/"),
            ]),
            new("Greenville County", "45045", 0.01m, "https://www.greenvillecounty.org/",
            [
                new("Greenville", "4530850", 0.00m, "https://www.greenvillesc.gov/"),
                new("Mauldin", "4544755", 0.01m, "https://www.cityofmauldin.org/"),
                new("Simpsonville", "4566715", 0.01m, "https://www.simpsonville.com/"),
            ]),
            new("Charleston County", "45019", 0.01m, "https://www.charlestoncounty.org/",
            [
                new("Charleston", "4513330", 0.00m, "https://www.charleston-sc.gov/"),
                new("North Charleston", "4550875", 0.00m, "https://www.northcharleston.org/"),
                new("Goose Creek", "4530025", 0.00m, "https://www.goosecreek.com/"),
            ]),
        ]),

        new("South Dakota", "SD", "46", 0.042m, "https://dor.sd.gov/businesses/taxes/sales-use-tax/",
        [
            new("Minnehaha County", "46099", 0.00m, "https://www.minnehahacounty.org/",
            [
                new("Sioux Falls", "4659020", 0.02m, "https://www.siouxfalls.org/"),
                new("Brandon", "4607580", 0.02m, "https://www.cityofbrandon.org/"),
            ]),
            new("Pennington County", "46103", 0.00m, "https://www.penningtoncounty.com/",
            [
                new("Rapid City", "4652980", 0.02m, "https://www.rcgov.org/"),
                new("Box Elder", "4607100", 0.02m, "https://www.cityofboxelder.com/"),
            ]),
            new("Brown County", "46013", 0.00m, "https://www.brown.sd.us/",
            [
                new("Aberdeen", "4600100", 0.02m, "https://www.aberdeen.sd.us/"),
            ]),
        ]),

        new("Tennessee", "TN", "47", 0.07m, "https://www.tn.gov/revenue/taxes/sales-and-use-tax.html",
        [
            new("Shelby County", "47157", 0.0225m, "https://www.shelbycountytn.gov/",
            [
                new("Memphis", "4748000", 0.00m, "https://www.memphistn.gov/"),
                new("Germantown", "4727740", 0.00m, "https://www.germantown-tn.gov/"),
                new("Collierville", "4715920", 0.00m, "https://www.collierville.com/"),
            ]),
            new("Davidson County", "47037", 0.0225m, "https://www.nashville.gov/",
            [
                new("Nashville", "4752006", 0.00m, "https://www.nashville.gov/"),
            ]),
            new("Knox County", "47093", 0.0225m, "https://www.knoxcounty.org/",
            [
                new("Knoxville", "4740000", 0.00m, "https://www.knoxvilletn.gov/"),
                new("Farragut", "4726220", 0.00m, "https://www.townoffarragut.org/"),
            ]),
            new("Hamilton County", "47065", 0.0225m, "https://www.hamiltontn.gov/",
            [
                new("Chattanooga", "4714000", 0.00m, "https://www.chattanooga.gov/"),
                new("East Ridge", "4721500", 0.00m, "https://www.eastridgetn.gov/"),
            ]),
        ]),

        new("Texas", "TX", "48", 0.0625m, "https://comptroller.texas.gov/taxes/sales/",
        [
            new("Harris County", "48201", 0.00m, "https://comptroller.texas.gov/taxes/sales/rates/",
            [
                new("Houston", "4835000", 0.02m, "https://www.houstontx.gov/finance/"),
                new("Pasadena", "4856348", 0.02m, "https://www.ci.pasadena.tx.us/"),
                new("Pearland", "4856644", 0.02m, "https://www.pearlandtx.gov/"),
                new("Baytown", "4807132", 0.02m, "https://www.baytown.org/"),
            ]),
            new("Dallas County", "48113", 0.00m, "https://comptroller.texas.gov/taxes/sales/rates/",
            [
                new("Dallas", "4819000", 0.02m, "https://dallascityhall.com/"),
                new("Garland", "4829000", 0.02m, "https://www.garlandtx.gov/"),
                new("Irving", "4837000", 0.02m, "https://www.cityofirving.org/"),
                new("Richardson", "4861592", 0.02m, "https://www.cor.net/"),
            ]),
            new("Tarrant County", "48439", 0.00m, "https://comptroller.texas.gov/taxes/sales/rates/",
            [
                new("Fort Worth", "4827000", 0.02m, "https://www.fortworthtexas.gov/"),
                new("Arlington", "4804000", 0.02m, "https://www.arlingtontx.gov/"),
                new("Mansfield", "4846452", 0.02m, "https://www.mansfieldtexas.gov/"),
            ]),
            new("Bexar County", "48029", 0.00m, "https://comptroller.texas.gov/taxes/sales/rates/",
            [
                new("San Antonio", "4865000", 0.02m, "https://www.sanantonio.gov/"),
                new("Leon Valley", "4841836", 0.02m, "https://leonvalleytexas.gov/"),
                new("Converse", "4816084", 0.02m, "https://www.conversetx.net/"),
            ]),
            new("Travis County", "48453", 0.00m, "https://comptroller.texas.gov/taxes/sales/rates/",
            [
                new("Austin", "4805000", 0.02m, "https://www.austintexas.gov/"),
                new("Round Rock", "4863500", 0.02m, "https://www.roundrocktexas.gov/"),
                new("Cedar Park", "4813024", 0.02m, "https://www.cedarparktexas.gov/"),
            ]),
        ]),

        new("Utah", "UT", "49", 0.0485m, "https://tax.utah.gov/sales",
        [
            new("Salt Lake County", "49035", 0.0135m, "https://www.slco.org/",
            [
                new("Salt Lake City", "4967000", 0.00m, "https://www.slc.gov/"),
                new("West Valley City", "4982950", 0.00m, "https://www.wvc-ut.gov/"),
                new("Sandy City", "4967440", 0.00m, "https://www.sandy.utah.gov/"),
                new("West Jordan", "4981620", 0.00m, "https://www.wjordan.com/"),
            ]),
            new("Utah County", "49049", 0.0075m, "https://www.utahcounty.gov/",
            [
                new("Provo", "4962470", 0.00m, "https://www.provo.org/"),
                new("Orem", "4956290", 0.00m, "https://www.orem.org/"),
                new("American Fork", "4901310", 0.00m, "https://www.americanfork.gov/"),
            ]),
            new("Davis County", "49011", 0.01m, "https://www.daviscountyutah.gov/",
            [
                new("Layton", "4943660", 0.00m, "https://www.laytoncity.org/"),
                new("Bountiful", "4907690", 0.00m, "https://www.bountifulutah.gov/"),
            ]),
        ]),

        new("Vermont", "VT", "50", 0.06m, "https://tax.vermont.gov/business/sales-and-use-tax",
        [
            new("Chittenden County", "50007", 0.00m, "https://www.chittendencounty.org/",
            [
                new("Burlington", "5010675", 0.00m, "https://www.burlingtonvt.gov/"),
                new("South Burlington", "5067950", 0.00m, "https://www.southburlingtonvt.gov/"),
                new("Williston", "5079275", 0.00m, "https://www.willistonvt.org/"),
            ]),
            new("Washington County", "50023", 0.00m, "https://www.washingtonco.state.vt.us/",
            [
                new("Montpelier", "5046000", 0.00m, "https://www.montpelier-vt.org/"),
                new("Barre City", "5003700", 0.00m, "https://www.barrecity.org/"),
            ]),
            new("Rutland County", "50021", 0.00m, "https://www.rutlandcounty.org/",
            [
                new("Rutland City", "5059225", 0.00m, "https://www.rutlandcity.com/"),
            ]),
        ]),

        new("Virginia", "VA", "51", 0.053m, "https://www.tax.virginia.gov/sales-tax",
        [
            new("Fairfax County", "51059", 0.007m, "https://www.fairfaxcounty.gov/",
            [
                new("Fairfax City", "5127672", 0.00m, "https://www.fairfaxva.gov/"),
                new("Reston", "51059-RES", 0.00m, "https://www.fairfaxcounty.gov/"),
                new("Herndon", "5135624", 0.00m, "https://www.herndon-va.gov/"),
            ]),
            new("Arlington County", "51013", 0.007m, "https://www.arlingtonva.us/",
            [
                new("Arlington", "5101000", 0.00m, "https://www.arlingtonva.us/"),
            ]),
            new("Virginia Beach City", "51810", 0.007m, "https://www.vbgov.com/",
            [
                new("Virginia Beach", "5182000", 0.00m, "https://www.vbgov.com/"),
            ]),
            new("Chesterfield County", "51041", 0.007m, "https://www.chesterfield.gov/",
            [
                new("Chester", "5115672", 0.00m, "https://www.chesterfield.gov/"),
                new("Midlothian", "5151984", 0.00m, "https://www.chesterfield.gov/"),
            ]),
            new("Richmond City", "51760", 0.007m, "https://www.rva.gov/",
            [
                new("Richmond", "5167000", 0.00m, "https://www.rva.gov/"),
            ]),
        ]),

        new("Washington", "WA", "53", 0.065m, "https://dor.wa.gov/taxes-rates/retail-sales-tax",
        [
            new("King County", "53033", 0.036m, "https://kingcounty.gov/",
            [
                new("Seattle", "5363000", 0.036m, "https://www.seattle.gov/"),
                new("Bellevue", "5305210", 0.022m, "https://bellevuewa.gov/"),
                new("Kent", "5335275", 0.022m, "https://www.kentwa.gov/"),
                new("Renton", "5357745", 0.022m, "https://rentonwa.gov/"),
            ]),
            new("Pierce County", "53053", 0.029m, "https://www.piercecountywa.gov/",
            [
                new("Tacoma", "5370000", 0.036m, "https://www.cityoftacoma.org/"),
                new("Lakewood", "5338038", 0.024m, "https://www.cityoflakewood.us/"),
                new("Federal Way", "5322300", 0.022m, "https://cityoffederalway.com/"),
            ]),
            new("Snohomish County", "53061", 0.028m, "https://snohomishcountywa.gov/",
            [
                new("Everett", "5322640", 0.028m, "https://www.everettwa.gov/"),
                new("Marysville", "5343955", 0.027m, "https://www.marysvillewa.gov/"),
            ]),
            new("Spokane County", "53063", 0.024m, "https://www.spokanecounty.org/",
            [
                new("Spokane", "5367000", 0.027m, "https://my.spokanecity.org/"),
                new("Spokane Valley", "5367167", 0.024m, "https://www.spokanevalley.org/"),
            ]),
        ]),

        new("West Virginia", "WV", "54", 0.06m, "https://tax.wv.gov/Business/SalesAndUseTax/",
        [
            new("Kanawha County", "54039", 0.00m, "https://www.kanawha.us/",
            [
                new("Charleston", "5414176", 0.00m, "https://www.cityofcharleston.org/"),
                new("South Charleston", "5471204", 0.00m, "https://www.southcharlestonwv.us/"),
            ]),
            new("Berkeley County", "54003", 0.00m, "https://www.berkeleywv.org/",
            [
                new("Martinsburg", "5450116", 0.00m, "https://www.martinsburgwv.gov/"),
                new("Inwood", "54003-INW", 0.00m, "https://www.berkeleywv.org/"),
            ]),
            new("Monongalia County", "54061", 0.00m, "https://www.monongaliacounty.gov/",
            [
                new("Morgantown", "5455756", 0.00m, "https://www.morgantownwv.gov/"),
                new("Star City", "5472028", 0.00m, "https://www.starcitywv.org/"),
            ]),
        ]),

        new("Wisconsin", "WI", "55", 0.05m, "https://www.revenue.wi.gov/Pages/SalesAndUse/home.aspx",
        [
            new("Milwaukee County", "55079", 0.005m, "https://county.milwaukee.gov/",
            [
                new("Milwaukee", "5553000", 0.00m, "https://city.milwaukee.gov/"),
                new("Wauwatosa", "5584175", 0.00m, "https://www.wauwatosa.net/"),
                new("West Allis", "5585175", 0.00m, "https://www.westalliswi.gov/"),
            ]),
            new("Dane County", "55025", 0.005m, "https://www.countyofdane.com/",
            [
                new("Madison", "5548000", 0.005m, "https://www.cityofmadison.com/"),
                new("Middleton", "5551000", 0.005m, "https://www.cityofmiddleton.us/"),
                new("Sun Prairie", "5576600", 0.005m, "https://www.cityofsunprairie.com/"),
            ]),
            new("Waukesha County", "55133", 0.00m, "https://www.waukesha-wi.gov/",
            [
                new("Waukesha", "5584025", 0.00m, "https://www.waukesha-wi.gov/"),
                new("Brookfield", "5510075", 0.005m, "https://www.ci.brookfield.wi.us/"),
                new("New Berlin", "5554875", 0.00m, "https://www.newberlin.org/"),
            ]),
        ]),

        new("Wyoming", "WY", "56", 0.04m, "https://revenue.wyo.gov/sales-use-tax",
        [
            new("Laramie County", "56021", 0.01m, "https://www.laramiecounty.com/",
            [
                new("Cheyenne", "5613900", 0.01m, "https://www.cheyennecity.org/"),
                new("Burns", "5609430", 0.00m, "https://www.laramiecounty.com/"),
            ]),
            new("Natrona County", "56025", 0.01m, "https://www.natrona.net/",
            [
                new("Casper", "5612055", 0.02m, "https://www.casperwy.gov/"),
                new("Mills", "5650760", 0.02m, "https://www.millswy.gov/"),
            ]),
            new("Campbell County", "56005", 0.02m, "https://www.ccgov.net/",
            [
                new("Gillette", "5632200", 0.02m, "https://www.gillettewy.gov/"),
            ]),
        ]),

        new("District of Columbia", "DC", "11", 0.06m, "https://otr.cfo.dc.gov/page/sales-and-use-taxes",
        [
            new("District of Columbia", "11001", 0.00m, "https://otr.cfo.dc.gov/",
            [
                new("Washington", "1150000", 0.00m, "https://dc.gov/"),
            ]),
        ]),
    ];
}