namespace TaxRateCollector.Infrastructure.Seeding;

public static class SstTaxonomyData
{
    public static readonly IReadOnlyList<TaxCategoryDef> Definitions =
    [
        // ── Goods ─────────────────────────────────────────────────────────────
        new("Goods",               "Goods",    false, 1),
        new("Services",            "Services", false, 1),

        // Food & Food Ingredients group
        new("Food & Food Ingredients",          "Goods", false, 1,  "Goods",
            "Products fit for human consumption, excluding alcoholic beverages, candy, dietary supplements, prepared food, and soft drinks."),
        new("Alcoholic Beverages",              "Goods", true,  1,  "Food & Food Ingredients",
            "Beverages containing more than one-half of one percent of alcohol by volume, suitable for human consumption."),
        new("Candy",                            "Goods", true,  2,  "Food & Food Ingredients",
            "A preparation of sugar, honey, or other natural or artificial sweeteners combined with chocolate, fruit, nuts, or other ingredients or flavorings in the form of bars, drops, or pieces. Does not include flour as an ingredient."),
        new("Dietary Supplements",              "Goods", true,  3,  "Food & Food Ingredients",
            "Any product, other than tobacco, intended to supplement the diet that contains one or more vitamins, minerals, herbs, botanicals, amino acids, or dietary substances."),
        new("Food & Food Ingredients (Unprepared)", "Goods", true, 4, "Food & Food Ingredients",
            "Substances, whether in liquid, concentrated, solid, frozen, dried, or dehydrated form, that are sold for ingestion or chewing by humans and are consumed for their taste or nutritional value. Excludes alcoholic beverages, candy, dietary supplements, prepared food, and soft drinks."),
        new("Food Sold Through Vending Machines","Goods", true,  5,  "Food & Food Ingredients",
            "Food dispensed through a machine or other mechanical device that accepts payment."),
        new("Prepared Food",                    "Goods", true,  6,  "Food & Food Ingredients",
            "Food sold in a heated state or heated by the seller; two or more food ingredients mixed or combined by the seller for sale as a single item; or food sold with eating utensils provided by the seller."),
        new("Soft Drinks",                      "Goods", true,  7,  "Food & Food Ingredients",
            "Beverages that contain natural or artificial sweeteners and do not contain milk or milk products, soy, rice, or similar milk substitutes, or more than fifty percent of vegetable or fruit juice by volume."),

        // Drugs & Medical group
        new("Drugs & Medical",                  "Goods", false, 2,  "Goods",
            "Prescription and non-prescription drugs, medical devices, and related equipment."),
        new("Prescription Drugs",               "Goods", true,  1,  "Drugs & Medical",
            "Drugs for human use that can be legally dispensed only by prescription. Exempt in all 45 states with a general sales tax."),
        new("Non-Prescription Drugs (OTC)",     "Goods", true,  2,  "Drugs & Medical",
            "Drugs for human use that do not require a prescription. Taxability varies by state."),
        new("Grooming & Hygiene Products",      "Goods", true,  3,  "Drugs & Medical",
            "Soaps, cleaning solutions, shampoo, toothpaste, mouthwash, antiperspirants, and sun-tan lotions or screen. Taxable in most states even where drugs are exempt."),
        new("Durable Medical Equipment",        "Goods", true,  4,  "Drugs & Medical",
            "Equipment including repair and replacement parts that can withstand repeated use, is primarily and customarily used to serve a medical purpose, and is appropriate for use in the home."),
        new("Mobility-Enhancing Equipment",     "Goods", true,  5,  "Drugs & Medical",
            "Equipment primarily and customarily used to provide or increase the ability to move from one place to another — wheelchairs, walkers, crutches, etc."),
        new("Prosthetic Devices",               "Goods", true,  6,  "Drugs & Medical",
            "Replacement, corrective, or supportive device worn on or in the body to artificially replace a missing portion of the body or to prevent or correct physical deformity or malfunction."),

        // Clothing group
        new("Clothing",                         "Goods", false, 3,  "Goods",
            "Human wearing apparel and accessories suitable for general use."),
        new("Clothing (General)",               "Goods", true,  1,  "Clothing",
            "All human wearing apparel suitable for general use. Exempt in MN, NJ, NY (items under $110), and PA; taxable in all other states."),
        new("Clothing Accessories or Equipment","Goods", true,  2,  "Clothing",
            "Incidental items worn on the person or in conjunction with clothing — briefcases, handbags, wallets, watches, hair accessories."),
        new("Fur Clothing",                     "Goods", true,  3,  "Clothing",
            "Articles of clothing made from the hide of animals with the fur or hair still attached. Taxable in most states regardless of clothing exemptions."),
        new("Protective Equipment",             "Goods", true,  4,  "Clothing",
            "Items worn as protection from injury or disease — hard hats, safety glasses, steel-toed boots, hazmat suits. Not sport/recreational equipment."),
        new("Sport or Recreational Equipment",  "Goods", true,  5,  "Clothing",
            "Items designed for use while participating in a sport or while engaged in exercise or recreational activity — helmets, pads, uniforms."),

        // Digital Products group
        new("Digital Products",                 "Goods", false, 4,  "Goods",
            "Products delivered electronically — audio, video, books, and software."),
        new("Digital Audio-Visual Works",       "Goods", true,  1,  "Digital Products",
            "Works that combine audio and visual components delivered electronically — movies, TV shows, video games."),
        new("Digital Audio Works",              "Goods", true,  2,  "Digital Products",
            "Works that result in sounds delivered electronically — music, ringtones, podcasts, audiobooks."),
        new("Digital Books",                    "Goods", true,  3,  "Digital Products",
            "Works that are generally recognized in the ordinary and usual sense as books delivered electronically — e-books, digital magazines, digital newspapers."),
        new("Other Digital Products",           "Goods", true,  4,  "Digital Products",
            "Electronic products not otherwise classified as digital audio-visual works, digital audio works, or digital books."),
        new("Prewritten Computer Software (Downloaded)", "Goods", true,  5,  "Digital Products",
            "Prewritten programs delivered electronically. Taxed in approximately 36 states; some states treat as a service."),
        new("Prewritten Computer Software (Physical Media)", "Goods", true, 6, "Digital Products",
            "Prewritten programs sold on tangible media (CD, USB). Taxable as tangible personal property in virtually all states."),

        // ── Services ──────────────────────────────────────────────────────────
        new("Telecommunications",              "Services", false, 1, "Services",
            "Voice, data, and related communications services as defined by the SSUTA."),
        new("Telecommunications Services",     "Services", true,  1, "Telecommunications",
            "Electronic transmission, conveyance, or routing of voice, data, audio, video, or any other information or signals to a point, or between or among points."),
        new("Ancillary Services",              "Services", true,  2, "Telecommunications",
            "Services associated with or incidental to the provision of telecommunications — conference bridging, detailed billing, directory assistance, vertical services, voice mail."),
        new("Internet Access",                 "Services", true,  3, "Telecommunications",
            "A service that enables users to access content, information, electronic mail, or other services offered over the Internet. Generally exempt under ITFA."),
        new("Prepaid Calling Service",         "Services", true,  4, "Telecommunications",
            "The right to access telecommunications services that must be paid for in advance using an access number or authorization code."),
        new("Prepaid Wireless Calling Service","Services", true,  5, "Telecommunications",
            "The right to utilize mobile wireless service, as well as any ancillary services, which is paid for in advance."),

        new("Other Services",                  "Services", false, 2, "Services",
            "Services subject to sales tax in one or more states."),
        new("Accommodations",                  "Services", true,  1, "Other Services",
            "Lodging for a period of less than 30 continuous days — hotel, motel, short-term rental. Taxed in most states."),
        new("Motor Vehicle Parking",           "Services", true,  2, "Other Services",
            "Charges for the right to park a motor vehicle. Taxed in approximately 30 states; also subject to local parking taxes."),
        new("Laundry & Dry Cleaning Services", "Services", true,  3, "Other Services",
            "Coin laundry, dry cleaning, garment pressing. Taxed in most states."),
        new("Admission Charges",               "Services", true,  4, "Other Services",
            "Fees for admission to places of amusement, entertainment, recreation, or sports events. Taxed in most states."),
        new("Motor Vehicle Towing",            "Services", true,  5, "Other Services",
            "Towing and impound services for motor vehicles. Taxability varies by state."),
    ];
}

public sealed record TaxCategoryDef(
    string Name, string TopLevel, bool IsLeaf, int Sort,
    string? ParentName = null, string KnownDescription = "");
