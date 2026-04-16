using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.Infrastructure.Seeding;

/// <summary>
/// Seeds the initial product/service tax category hierarchy.
/// Idempotent — skipped entirely if any TaxCategory rows already exist.
///
/// Goods
///   Food & Beverage
///     Groceries / Unprepared Food   — exempt in ~32 states; reduced in ~6 more
///     Prepared Food                 — taxed at full rate in all taxing states
///     Candy & Confectionery         — taxed even when groceries are exempt
///     Non-Alcoholic Beverages       — varies (soda often taxed; juice sometimes exempt)
///     Dietary Supplements           — varies by state
///   Clothing & Apparel
///     Clothing                      — exempt under $110 in MN, NJ, NY, PA
///     Footwear                      — follows clothing rules
///     School Uniforms               — sometimes exempt during sales-tax holidays
///   Pharmaceuticals & Medical
///     Prescription Drugs            — exempt in all 45 taxing states
///     OTC Medications               — exempt in most; taxed in IL, MN, NJ
///     Medical Devices & Equipment   — typically exempt
///   Electronics & Technology
///     Computers & Tablets           — taxed; occasional holiday exemptions
///     Mobile Phones                 — taxed
///     Prewritten Software (Downloaded) — taxed in ~36 states
///   Home & Garden
///     Furniture & Home Furnishings  — taxed
///     Appliances                    — taxed
///     Agricultural Supplies         — often exempt for farm use
///   Automotive
///     Vehicle Sales                 — special registration/use tax; not standard sales tax
///     Parts & Accessories           — taxed
///     Motor Fuel                    — excise; see ExciseTaxRates table
///   Construction & Building Materials — typically taxed; contractor rules vary
/// Services
///   Professional Services           — exempt in most states
///     Legal Services                — exempt in all 45 taxing states
///     Accounting / CPA Services     — exempt in most; taxed in NM, OH, TX
///     Consulting & Management       — taxed in NM, OH, TX; otherwise exempt
///   Personal Care Services
///     Haircuts & Barbershops        — taxed in NM, TX, WA; exempt elsewhere
///     Beauty Salon Services         — varies by state
///     Laundry & Dry Cleaning        — taxed in most states
///   Digital & Technology Services
///     Streaming Video / Audio       — taxed in ~30 states
///     SaaS / Cloud Software         — taxed in ~20+ states; rapidly expanding
///     Web Hosting / IaaS            — taxed in some states (TX, OH)
///   Construction & Repair Services
///     Residential Repair & Renovation — taxed on materials; labor varies
///     Commercial Cleaning           — taxed in ~20 states
///   Entertainment & Recreation
///     Admissions & Event Tickets    — taxed in most states
///     Fitness & Gym Memberships     — taxed in ~25 states
///     Amusement Park Admissions     — taxed in most states
///   Healthcare Services             — exempt in all 45 taxing states
///     Medical Services              — exempt
///     Dental Services               — exempt
///   Transportation Services
///     Rideshare (Uber/Lyft)         — taxed in some states/cities
///     Parking                       — taxed in ~30 states
/// </summary>
public static class TaxCategorySeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.TaxCategories.AnyAsync()) return;

        // ── Level 0: Top-level types ──────────────────────────────────────────
        var goods    = Cat("Goods",    "Goods",    false, "Physical products subject to sales tax", 1);
        var services = Cat("Services", "Services", false, "Services which may be subject to sales tax", 2);
        db.TaxCategories.AddRange(goods, services);
        await db.SaveChangesAsync();

        // ── Level 1: Goods sub-groups ─────────────────────────────────────────
        var foodBev     = Cat("Food & Beverage",              "Goods",    false, "Food and drink products", 1, goods.Id);
        var clothing    = Cat("Clothing & Apparel",            "Goods",    false, "Wearable items", 2, goods.Id);
        var pharma      = Cat("Pharmaceuticals & Medical",     "Goods",    false, "Drugs, OTCs, and medical devices", 3, goods.Id);
        var electronics = Cat("Electronics & Technology",      "Goods",    false, "Computers, phones, software", 4, goods.Id);
        var homeGarden  = Cat("Home & Garden",                 "Goods",    false, "Furniture, appliances, agricultural supplies", 5, goods.Id);
        var automotive  = Cat("Automotive",                    "Goods",    false, "Vehicles, parts, and motor fuel", 6, goods.Id);
        var construction= Cat("Construction & Building Materials","Goods", false, "Raw materials and supplies used in construction", 7, goods.Id);
        db.TaxCategories.AddRange(foodBev, clothing, pharma, electronics, homeGarden, automotive, construction);

        // ── Level 1: Services sub-groups ─────────────────────────────────────
        var professional  = Cat("Professional Services",           "Services", false, "Legal, accounting, consulting", 1, services.Id);
        var personalCare  = Cat("Personal Care Services",          "Services", false, "Haircuts, salons, laundry", 2, services.Id);
        var digitalSvc    = Cat("Digital & Technology Services",   "Services", false, "Streaming, SaaS, cloud", 3, services.Id);
        var constructSvc  = Cat("Construction & Repair Services",  "Services", false, "Repair labor and commercial cleaning", 4, services.Id);
        var entertainment = Cat("Entertainment & Recreation",      "Services", false, "Admissions, gyms, amusement parks", 5, services.Id);
        var healthcare    = Cat("Healthcare Services",             "Services", false, "Medical and dental — universally exempt", 6, services.Id);
        var transport     = Cat("Transportation Services",         "Services", false, "Rideshare and parking", 7, services.Id);
        db.TaxCategories.AddRange(professional, personalCare, digitalSvc, constructSvc, entertainment, healthcare, transport);
        await db.SaveChangesAsync();

        // ── Level 2: Food & Beverage leaves ──────────────────────────────────
        db.TaxCategories.AddRange(
            Cat("Groceries / Unprepared Food",  "Goods", true,
                "Raw, unheated food for home preparation. Exempt in ~32 states (AZ, CA, CO, GA, IL, LA, MA, MI, MN, NJ, NY, PA, SC, TX, VA, WA, WI and others); reduced rate in MO (1.225%), TN (4%), VA (2.5%).",
                1, foodBev.Id),
            Cat("Prepared Food",               "Goods", true,
                "Restaurant meals, hot foods, food sold with eating utensils. Taxed at full rate in all states that have a sales tax.",
                2, foodBev.Id),
            Cat("Candy & Confectionery",        "Goods", true,
                "Candy and similar sweetened products. Often taxed even when staple groceries are exempt (e.g., IL, TX).",
                3, foodBev.Id),
            Cat("Non-Alcoholic Beverages",      "Goods", true,
                "Soda, juice, sports drinks. Soda often taxed; 100% juice sometimes treated as food and exempt.",
                4, foodBev.Id),
            Cat("Dietary Supplements",          "Goods", true,
                "Vitamins, protein powders, herbal supplements. Treated as food (exempt) in some states; taxed in others.",
                5, foodBev.Id)
        );

        // ── Level 2: Clothing & Apparel leaves ───────────────────────────────
        db.TaxCategories.AddRange(
            Cat("Clothing",         "Goods", true,
                "General wearing apparel. Exempt in MN, NJ, NY (under $110/item), PA; taxed elsewhere.",
                1, clothing.Id),
            Cat("Footwear",         "Goods", true,
                "Shoes and boots. Follows clothing exemption rules in most states.",
                2, clothing.Id),
            Cat("School Uniforms",  "Goods", true,
                "Uniforms required by schools. Some states include in annual back-to-school tax-holiday exemptions.",
                3, clothing.Id)
        );

        // ── Level 2: Pharmaceuticals & Medical leaves ─────────────────────────
        db.TaxCategories.AddRange(
            Cat("Prescription Drugs",          "Goods", true,
                "FDA-approved prescription medications. Exempt in all 45 states with a general sales tax.",
                1, pharma.Id),
            Cat("OTC Medications",             "Goods", true,
                "Over-the-counter medicines, aspirin, cold remedies. Exempt in most states; taxed in IL, MN, NJ.",
                2, pharma.Id),
            Cat("Medical Devices & Equipment", "Goods", true,
                "Wheelchairs, crutches, prosthetics, durable medical equipment. Typically exempt.",
                3, pharma.Id)
        );

        // ── Level 2: Electronics & Technology leaves ──────────────────────────
        db.TaxCategories.AddRange(
            Cat("Computers & Tablets",                "Goods", true,
                "Desktop and laptop computers, tablets. Taxed in most states; some states have periodic exemptions.",
                1, electronics.Id),
            Cat("Mobile Phones",                       "Goods", true,
                "Smartphones and cellular devices. Taxed in most states (often also subject to telecom taxes).",
                2, electronics.Id),
            Cat("Prewritten / Downloaded Software",    "Goods", true,
                "Software sold or delivered electronically. Taxed in ~36 states; treated as a service in others.",
                3, electronics.Id)
        );

        // ── Level 2: Home & Garden leaves ─────────────────────────────────────
        db.TaxCategories.AddRange(
            Cat("Furniture & Home Furnishings", "Goods", true,
                "Sofas, beds, tables. Taxed in all states with a sales tax.",
                1, homeGarden.Id),
            Cat("Appliances",                   "Goods", true,
                "Refrigerators, dishwashers, washing machines. Taxed; some states have Energy Star exemption days.",
                2, homeGarden.Id),
            Cat("Agricultural Supplies",        "Goods", true,
                "Seeds, fertilizer, farm equipment and livestock feed purchased for farming use. Often exempt.",
                3, homeGarden.Id)
        );

        // ── Level 2: Automotive leaves ────────────────────────────────────────
        db.TaxCategories.AddRange(
            Cat("Vehicle Sales",       "Goods", true,
                "Automobile and truck sales. Subject to state use/registration tax rather than standard sales tax in most states.",
                1, automotive.Id),
            Cat("Auto Parts & Accessories", "Goods", true,
                "Replacement parts, tires, accessories. Taxed at general sales tax rate.",
                2, automotive.Id),
            Cat("Motor Fuel",          "Goods", true,
                "Gasoline and diesel. Subject to excise taxes (see ExciseTaxRates); also subject to general sales tax in some states.",
                3, automotive.Id)
        );

        // Construction Materials leaf (Level 1 is already leaf-adjacent)
        construction.IsLeaf = true;
        construction.Description = "Lumber, concrete, drywall, and raw building materials. Typically taxed; contractors may owe use tax on materials incorporated into real property.";

        // ── Level 2: Professional Services leaves ─────────────────────────────
        db.TaxCategories.AddRange(
            Cat("Legal Services",                "Services", true,
                "Attorney fees, court filings. Exempt in all 45 taxing states.",
                1, professional.Id),
            Cat("Accounting / CPA Services",     "Services", true,
                "Tax preparation, auditing, bookkeeping. Exempt in most states; taxed in NM, OH, TX.",
                2, professional.Id),
            Cat("Consulting & Management",       "Services", true,
                "Business consulting, management advisory. Taxed in NM, OH, TX; otherwise exempt.",
                3, professional.Id)
        );

        // ── Level 2: Personal Care leaves ─────────────────────────────────────
        db.TaxCategories.AddRange(
            Cat("Haircuts & Barbershops",   "Services", true,
                "Hair cutting, styling. Taxed in NM, TX, WA; exempt in the remaining ~42 taxing states.",
                1, personalCare.Id),
            Cat("Beauty Salon Services",    "Services", true,
                "Facials, manicures, massages. Varies widely; taxed in about half of taxing states.",
                2, personalCare.Id),
            Cat("Laundry & Dry Cleaning",   "Services", true,
                "Coin laundry, dry cleaning. Taxed in most states (~30 of 45).",
                3, personalCare.Id)
        );

        // ── Level 2: Digital & Technology Services leaves ─────────────────────
        db.TaxCategories.AddRange(
            Cat("Streaming Video / Audio",   "Services", true,
                "Netflix, Spotify, Hulu subscriptions. Taxed in ~30 states; rapid expansion post-2019.",
                1, digitalSvc.Id),
            Cat("SaaS / Cloud Software",     "Services", true,
                "Software-as-a-Service subscriptions. Taxed in ~22 states; classification varies (service vs. license).",
                2, digitalSvc.Id),
            Cat("Web Hosting / IaaS",        "Services", true,
                "Managed hosting, cloud compute. Taxed in TX, OH, and a few other states.",
                3, digitalSvc.Id)
        );

        // ── Level 2: Construction & Repair Services leaves ────────────────────
        db.TaxCategories.AddRange(
            Cat("Residential Repair & Renovation", "Services", true,
                "Home repair labor. Materials always taxed; labor is taxed in some states (e.g., NM, SD, WA).",
                1, constructSvc.Id),
            Cat("Commercial Cleaning",              "Services", true,
                "Janitorial and commercial cleaning services. Taxed in ~20 states.",
                2, constructSvc.Id)
        );

        // ── Level 2: Entertainment leaves ────────────────────────────────────
        db.TaxCategories.AddRange(
            Cat("Admissions & Event Tickets", "Services", true,
                "Concert, sports, theater tickets. Taxed in most states with a sales tax.",
                1, entertainment.Id),
            Cat("Fitness & Gym Memberships",  "Services", true,
                "Health club dues. Taxed in ~25 states; exempt in others.",
                2, entertainment.Id),
            Cat("Amusement Park Admissions",  "Services", true,
                "Theme parks, fairs, exhibitions. Taxed in most states.",
                3, entertainment.Id)
        );

        // ── Level 2: Healthcare Services leaves ──────────────────────────────
        db.TaxCategories.AddRange(
            Cat("Medical Services",  "Services", true,
                "Physician, hospital, and outpatient care. Exempt in all 45 states with a general sales tax.",
                1, healthcare.Id),
            Cat("Dental Services",   "Services", true,
                "Dental examinations, cleanings, procedures. Exempt in all 45 taxing states.",
                2, healthcare.Id)
        );

        // ── Level 2: Transportation Services leaves ───────────────────────────
        db.TaxCategories.AddRange(
            Cat("Rideshare (Uber / Lyft)", "Services", true,
                "App-based ride services. Taxed in some states/cities (e.g., Chicago rideshare tax).",
                1, transport.Id),
            Cat("Parking",                 "Services", true,
                "Parking garage and lot fees. Taxed in ~30 states; also subject to local parking taxes.",
                2, transport.Id)
        );

        await db.SaveChangesAsync();
    }

    private static TaxCategory Cat(
        string name, string topLevel, bool isLeaf, string description,
        int sort, int? parentId = null) =>
        new()
        {
            Name         = name,
            TopLevelType = topLevel,
            IsLeaf       = isLeaf,
            Description  = description,
            SortOrder    = sort,
            ParentId     = parentId,
        };
}
