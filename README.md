# TaxRateCollector

**Provably accurate US sales tax rates — for billing systems that can't afford to be wrong.**

Most tax-rate APIs hand you a number. TaxRateCollector hands you the number *and the official government PDF, CSV, or API response it came from* — hashed, timestamped, and stored alongside every rate so your audit team can trace every cent back to the statute that authorized it.

### Why teams use it

- **Evidence on every row.** Each `TaxRate` is bound to a `SourceDocument` holding the raw `.gov` artifact, its SHA-256 content hash, and the `FetchedAt` timestamp. Rates are independently verifiable without a live network call.
- **All 14,000+ US jurisdictions.** Country → State → County → City hierarchy seeded from the US Census Bureau gazetteers. ZIP-to-jurisdiction lookup covers ~33,000 ZCTAs out of the box.
- **SSUTA-aligned product taxonomy.** Categorize taxable items against the Streamlined Sales & Use Tax Agreement Appendix C — the same legal framework 24 member states already use — with documented overrides for non-member states like CA, TX, NY, and FL.
- **Continuous re-scraping with change detection.** A background scheduler re-checks each jurisdiction's official `.gov` source on a configurable cadence. The diff engine writes a `ChangeLogEntry` the moment a rate moves.
- **Pay only for what you bill in.** Subscribers pick the states *and* product categories that match their footprint at $0.01 each per month — no flat enterprise contract, no rates for jurisdictions you don't operate in.
- **Plugs into your stack.** Blazor Server UI for your tax team, plus CSV / XLSX / SQL / HTML exports for your billing system.

### Built for compliance

Tax compliance is not a "best guess" problem. When an auditor asks why you charged 8.25% on a transaction in Beverly Hills, "our API said so" is not a defense. TaxRateCollector gives you the original CDTFA PDF, hashed at fetch time, with a timestamp — the kind of paper trail that ends audits instead of starting them.

A Blazor Server application that builds and maintains an exhaustively researched, evidence-backed master table of US sales and excise tax rates for every jurisdiction (Country → State → County → City).

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Prerequisites](#prerequisites)
3. [Project Structure](#project-structure)
4. [Getting Started](#getting-started)
5. [How the Schema Gets Populated](#how-the-schema-gets-populated)
   - [On Every Startup (automatic)](#on-every-startup-automatic)
   - [Step 1 — SST Product/Service Taxonomy](#step-1--sst-productservice-taxonomy)
   - [Step 2 — Census Jurisdictions (Counties & Cities)](#step-2--census-jurisdictions-counties--cities)
   - [Step 3 — ZIP Code Crosswalks](#step-3--zip-code-crosswalks)
   - [Step 4 — Tax Rate Scraping](#step-4--tax-rate-scraping)
   - [Other Admin-Configured Tables](#other-admin-configured-tables)
6. [SSUTA Membership & Non-Member States](#ssuta-membership--non-member-states)
7. [Database Schema](#database-schema)
8. [Data Source URLs](#data-source-urls)
9. [Settings](#settings)
10. [Evidence & Provenance System](#evidence--provenance-system)
11. [UI Pages](#ui-pages)
12. [Exports](#exports)
13. [Database Migrations](#database-migrations)
14. [NUnit Tests](#nunit-tests)
15. [Scraper Framework](#scraper-framework)
16. [Adding a New State Scraper](#adding-a-new-state-scraper)
17. [Roles & Subscriptions](#roles--subscriptions)
18. [Deploying to Azure](#deploying-to-azure)

---

## Architecture Overview

```
TaxRateCollector.Core           Domain entities, enums, interfaces
TaxRateCollector.Infrastructure EF Core, migrations, seeders, importers, scrapers, services
TaxRateCollector.Blazor         Blazor Server UI, pages, exports
TaxRateCollector.UnitTests      NUnit 4 — hierarchy, seeder correctness, DB population
```

**Stack:**

| Layer | Technology |
|---|---|
| UI | ASP.NET Core 10, Blazor Server (`InteractiveServer`) |
| ORM | EF Core 10 |
| Database (dev) | SQL Server LocalDB |
| Database (prod) | SQL Server / Azure SQL |
| PDF extraction | UglyToad.PdfPig (custom build) |
| XLSX export | ClosedXML 0.105 |
| HTML scraping | HtmlAgilityPack |
| CSV parsing | CsvHelper |
| Logging | Serilog.AspNetCore |
| Auth | ASP.NET Core Identity |
| Testing | NUnit 4, EF Core InMemory, SQL Server LocalDB |

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- SQL Server LocalDB (included with Visual Studio, or `sqllocaldb create MSSQLLocalDB`)
- Git
- (Optional) Visual Studio 2022 17.10+ or Rider 2024.1+

---

## Project Structure

```
TaxRateCollector/
├── TaxRateCollector.Core/
│   ├── Entities/
│   │   ├── Jurisdiction.cs             Self-referential hierarchy node (Country/State/County/City)
│   │   ├── TaxRate.cs                  Rate row with IsCurrent, TaxCategoryId, evidence link
│   │   ├── TaxCategory.cs              SST taxonomy node (hierarchical)
│   │   ├── SourceDocument.cs           Evidence doc (SHA-256, raw content, SourceUrl)
│   │   ├── StateTaxProfile.cs          State-level metadata (agency name/URL, SST membership)
│   │   ├── ExciseTaxRate.cs            Sin/excise tax (alcohol, tobacco, cannabis, fuel, hotel)
│   │   ├── ScrapeRun.cs                Scrape run metadata (status, timestamps, counts)
│   │   ├── ZipCodeRecord.cs            ZIP → State/County/City junction
│   │   └── ...billing, changelog, logs
│   ├── Enums/
│   │   ├── JurisdictionType.cs         Country=0, State=1, County=2, City=3
│   │   ├── CategoryTaxability.cs       Taxable, Exempt, Reduced, Varies
│   │   ├── LocalTaxAuthorityType.cs    Piggyback, HomeRule, SstUniform, Independent
│   │   └── ...ScrapeStatus, SourceType, ProductCategory
│   └── Interfaces/
│       ├── IScrapeOrchestrator.cs
│       ├── IScrapeStrategy.cs
│       ├── ISstTaxonomyImportService.cs
│       └── ...
│
├── TaxRateCollector.Infrastructure/
│   ├── Data/
│   │   └── AppDbContext.cs             EF Core DbContext, all 25 tables
│   ├── Migrations/
│   ├── Seeding/
│   │   ├── JurisdictionSeeder.cs       Seeds Country + 51 States on startup
│   │   ├── TaxCategorySeeder.cs        Seeds SST taxonomy from hardcoded SstTaxonomyData
│   │   ├── StateTaxProfileSeeder.cs    Seeds 51 state tax profiles (hardcoded, verified 2025-01-01)
│   │   └── SstTaxonomyData.cs          Hardcoded SST category hierarchy definitions
│   └── Services/
│       ├── CensusJurisdictionImportService.cs  Downloads Census ZIPs → creates ~3,200 counties + ~30,000 cities
│       ├── ZipImportService.cs                 Downloads Census crosswalks → links ~33,000 ZIPs
│       ├── SstTaxonomyImportService.cs         Downloads SSUTA PDF → refreshes TaxCategory descriptions
│       ├── ScrapeOrchestrator.cs
│       ├── ScrapeSchedulerService.cs           IHostedService background re-scrape
│       ├── SettingsService.cs                  Reads/writes %APPDATA%\MindAttic\TaxRateCollector\settings.json
│       └── ...DiffEngine, AlertService, TaxCalculator
│
├── TaxRateCollector.Blazor/
│   ├── Components/Pages/
│   │   ├── Jurisdictions.razor         Lazy-loading hierarchy tree, inline rate edit, evidence drop zones
│   │   ├── Setup.razor                 6-step admin pipeline to populate the database
│   │   ├── Settings.razor              Theme, URLs, PayPal/pricing config, DB backup
│   │   ├── Glossary.razor              SST term definitions
│   │   └── Logs.razor
│   ├── Services/
│   │   └── ViewAsService.cs            Admin preview roles (Actual / Subscriber / Guest)
│   └── Program.cs                      DI registration, migrate → seed on startup
│
└── TaxRateCollector.UnitTests/
    └── SetupTests/
        ├── SstTaxonomyStructureTests.cs
        ├── TaxCategorySeederTests.cs
        ├── AppSettingsTests.cs
        ├── DatabaseBackupTests.cs
        └── DatabasePopulationIntegrationTests.cs  (Category=Integration, needs LocalDB)
```

---

## Getting Started

```bash
# 1. Clone
git clone https://github.com/mindattic/TaxRateCollector.git
cd TaxRateCollector

# 2. Restore
dotnet restore

# 3. Run (migrations + seeders run automatically)
dotnet run --project TaxRateCollector.Blazor

# 4. Open https://localhost:5001
#    Log in as dev admin (set DEV_ADMIN_EMAIL / DEV_ADMIN_PASSWORD env vars)
#    Navigate to Setup to run the data import pipeline
```

On first launch, `Program.cs` automatically:
1. Applies all pending EF Core migrations
2. Seeds `TaxCategories` (SST taxonomy, ~200 nodes)
3. Seeds `Jurisdictions` (Country + 51 States)
4. Seeds `StateTaxProfiles` (51 profiles)
5. Seeds `PricingConfig` (Id=1, $0.01/state)
6. Seeds `PayPalConfig` (Id=1, sandbox mode)
7. Creates the dev admin Identity user (if env vars are set)

The rest of the data — counties, cities, ZIP codes, actual tax rates — is populated through the **Setup** pipeline described below.

---

## How the Schema Gets Populated

This is the most important section. The database has ~25 tables that fill up in distinct phases.

### On Every Startup (automatic)

These run in `Program.cs` before the app accepts requests. They are all idempotent — they check before inserting and skip if data already exists.

| Service | Source | Output | Notes |
|---|---|---|---|
| `TaxCategorySeeder` | `SstTaxonomyData.cs` (hardcoded) | ~200 `TaxCategory` rows | The SST product/service hierarchy (Goods → Food → Candy, etc.) |
| `JurisdictionSeeder` | Hardcoded array in code | 1 Country + 51 States | Provides the root `ParentId` targets for Census imports |
| `StateTaxProfileSeeder` | Hardcoded, verified 2025-01-01 | 51 `StateTaxProfile` rows | State rate, SST membership, revenue agency name + URL |
| Config seeders | Hardcoded defaults | 1 `PricingConfig`, 1 `PayPalConfig` | Only inserts if table is empty |

**`TaxCategorySeeder`** uses the hardcoded definitions in `SstTaxonomyData.cs` — the same set the `SstTaxonomyImportService` (Step 1 of Setup) can optionally refresh descriptions for from the live SSUTA PDF.

**`StateTaxProfileSeeder`** contains all 50 states + DC with:
- State code, state name, `GeneralSalesTaxRate`
- `IsSstMember` (from the SST Governing Board member list)
- `LocalTaxAuthorityType` (Piggyback / HomeRule / SstUniform / Independent)
- State revenue agency name and official URL
- Notes on local tax caps where applicable

### Step 1 — SST Product/Service Taxonomy

**Service:** `SstTaxonomyImportService`  
**Trigger:** Setup → "Import SST Taxonomy" button (or "Run All")  
**Source:** SSUTA Agreement PDF, Appendix C (downloaded at runtime)  
**Default URL:** `https://www.streamlinedsalestax.org/docs/default-source/agreement/ssuta/ssuta-as-amended-through-12-20-24-with-hyperlinks-and-compiler-notes-at-end-clean-for-posting.pdf`

**What it does:**
1. Downloads the SSUTA Agreement PDF using `UglyToad.PdfPig`
2. Locates Appendix C (pages between "Appendix C" and "Appendix D" headings)
3. Extracts defined terms using the regex pattern `"Term" means <definition>`
4. Maps extracted terms to the existing `TaxCategory` hierarchy
5. Upserts `TaxCategory.Description` fields with the official PDF definitions
6. Falls back to hardcoded `KnownDescription` for any term not found in the PDF

This step is optional — the startup seeder already populates the category tree. This step just enriches descriptions from the authoritative PDF source and can be re-run whenever a new version of the SSUTA is published.

### Step 2 — Census Jurisdictions (Counties & Cities)

**Service:** `CensusJurisdictionImportService`  
**Trigger:** Setup → "Import Census Jurisdictions" button (or "Run All")  
**Sources:** US Census Bureau — all free, no API key required

| File | URL | Contents |
|---|---|---|
| County Gazetteer | `2025_Gaz_counties_national.zip` | 3,143 county names + 5-digit FIPS codes |
| Place Gazetteer | `2025_Gaz_place_national.zip` | ~30,000 city/place names + 7-digit FIPS codes |
| ZCTA→County crosswalk | `tab20_zcta520_county20_natl.txt` | Resolves place→county via largest land-area intersection |
| ZCTA→Place crosswalk | `tab20_zcta520_place20_natl.txt` | Used to re-parent cities to correct counties |

**What it does:**
1. Downloads and caches the four Census files to `%APPDATA%\MindAttic\TaxRateCollector\cache\`
2. Parses county FIPS codes and names → creates `Jurisdiction` rows with `JurisdictionType=County`, parented to the correct State
3. Parses place FIPS codes and names → creates `Jurisdiction` rows with `JurisdictionType=City`
4. Uses the ZCTA crosswalks to derive the correct county for each city (majority land-area intersection)
5. Creates placeholder `TaxRate` rows (0.000%) for all new jurisdictions across all `TaxCategory` leaves
6. Records a `ScrapeRun` entry for the import

**Result:** ~3,200 counties + ~30,000 cities, all correctly parented in the hierarchy.  
**Duration:** 20–40 minutes on first run. Cached files speed up re-runs.

### Step 3 — ZIP Code Crosswalks

**Service:** `ZipImportService`  
**Trigger:** Setup → "Import ZIP Codes" button (or "Run All")  
**Sources:** Same Census ZCTA crosswalk files (already cached from Step 2)

| File | URL | Contents |
|---|---|---|
| ZCTA→County crosswalk | `tab20_zcta520_county20_natl.txt` | ZIP → primary county FIPS |
| ZCTA→Place crosswalk | `tab20_zcta520_place20_natl.txt` | ZIP → primary city FIPS |

**What it does:**
1. Reads the two crosswalk files (downloads if not cached)
2. Selects the primary county for each ZIP by largest `AREALAND_PART` value
3. Selects the primary city for each ZIP by largest `AREALAND_PART` value
4. Unions ~33,000 unique ZIPs from both files
5. Resolves each ZIP's `StateJurisdictionId`, `CountyJurisdictionId`, `CityJurisdictionId` by matching FIPS codes to `Jurisdiction` rows created in Step 2
6. Bulk-inserts `ZipCodeRecord` rows (skips ZIPs already imported — idempotent)
7. Optionally enriches city names via the USPS CityStateLookup API if `usps_api_key` is configured in Settings

**Result:** ~33,000 `ZipCodeRecord` rows, each pointing at three jurisdiction rows.  
**Duration:** 15–30 minutes on first run.

**How ZIP lookup works at query time:**

```
Customer enters ZIP 90210
    ↓
ZipCodeRecord lookup → State=CA + County=06037 (LA County) + City=Beverly Hills
    ↓
Query TaxRates for those 3 JurisdictionIds (filtered by TaxCategoryId)
    ↓
Sum: 7.25% (CA) + 1.00% (LA County) + 0.00% (BH) = 8.25%
    ↓
Apply CategoryTaxability rule (e.g., Groceries in CA → Exempt → 0%)
    ↓
Final: ItemPrice × 0%  (CA exempts unprepared food)
```

ZIP codes carry no rates themselves — they are purely a lookup index into the jurisdiction hierarchy.

### Step 4 — Tax Rate Scraping

**Service:** `ScrapeOrchestrator` / `IScrapeStrategy` implementations  
**Trigger:** Setup → "Run Scrape" button, or the `ScrapeSchedulerService` background service  
**Sources:** Official `.gov` tax rate pages — one `IScrapeStrategy` per state/format

Before scraping can run, each jurisdiction's `SourceUrl` must be set (the URL of that state's official tax rate page). Step 5 of Setup guides the admin through assigning these URLs. The Discovery card on the Setup page can auto-probe jurisdictions to suggest source URLs.

**What it does:**
1. Loops all `Jurisdiction` rows where `IsActive = true` and `SourceUrl` is set
2. Calls `strategy.CanHandle(jurisdiction)` to find the right scraper plug-in
3. Scraper downloads the source (HTML table, CSV, XLSX, API) and normalizes rates via `Sanitizer`
4. `DiffEngine` compares the new rate to the current `IsCurrent = true` row
5. If changed: sets old row `IsCurrent = false`, inserts new `TaxRate` row, writes `ChangeLogEntry`
6. Attaches the raw source artifact as a `SourceDocument` with SHA-256 hash

**Existing strategies:**

| Strategy | Source format | Target |
|---|---|---|
| `IllinoisTableScraper` | HTML table | `tax.illinois.gov` |
| `CaliforniaCsvScraper` | CSV download | `cdtfa.ca.gov` |
| `TexasExcelScraper` | XLSX download | `comptroller.texas.gov` |

### Other Admin-Configured Tables

These tables are populated through the **Settings** page (`/settings`), not the Setup pipeline:

| Table | How populated |
|---|---|
| `PricingConfigs` | Seeded with `PricePerState = $0.01` on startup. Editable in Settings → Pricing. |
| `PayPalConfigs` | Seeded with empty credentials in sandbox mode. Fill in `ClientId`, `ClientSecret`, `WebhookId` in Settings → PayPal. |
| `AspNetUsers` | Created via `/register` or by the dev admin seeder (`DEV_ADMIN_EMAIL` / `DEV_ADMIN_PASSWORD` env vars). |
| `Subscribers` | Created when a user completes PayPal checkout. |
| `SubscribedStates` | Added per-state when a subscriber selects states. |
| `BillingRecords` | Created by the PayPal webhook handler on successful payment. |
| `LogEntries` | Written automatically by Serilog as the app runs. |
| `ChangeLog` | Written by `DiffEngine` whenever a scrape detects a rate change. |
| `ScrapeRuns` | Created by `ScrapeOrchestrator` and the Census importer. A shared `Status=Manual` run is used for all UI-entered rates. |

---

## SSUTA Membership & Non-Member States

The SST product taxonomy seeded in `TaxCategories` is the authoritative classification for **24 SSUTA member states**. It applies as a useful scaffold for all other states too, but taxability per category for non-members must come from each state's own statutes — not from the Taxability Matrix.

### States with No Sales Tax

Alaska, Delaware, Montana, New Hampshire, and Oregon levy no state sales tax. All five still appear in `Jurisdictions` and `StateTaxProfiles` (with `GeneralSalesTaxRate = 0`). No rate scraping is needed. Note: some Alaskan municipalities impose local sales taxes independently.

### 24 SSUTA Full Member States

AR, GA, IN, IA, KS, KY, MI, MN, NE, NV, NJ, NC, ND, OH, OK, RI, SD, TN, UT, VT, WA, WV, WI, WY

For these states, SSUTA Appendix C definitions apply uniformly. Local jurisdictions within each member state must use the state's SST-derived taxability base (see `LocalTaxAuthorityType = SstUniform`).

### ~21 Non-Member States (have sales tax, not in SSUTA)

These states define their own taxability rules. The seeded `TaxCategory` tree is still used as a classification scaffold, but the `StateCategoryRule` rows for these states must be populated from each state's own statutes rather than from the SST Taxability Matrix.

| State | Notable Difference |
|---|---|
| California | Defines "candy" and "dietary supplements" differently; complex district taxes layered on top of state rate |
| Texas | Own definitions for food, software, and services; origin-based sourcing for intrastate sales |
| Florida | No personal income tax; broad sales tax base with unique service exemptions |
| New York | Clothing under $110 exempt; complex state + NYC + county layers; locality-specific rules |
| Illinois | Two-tier structure: 1% rate on food & drugs, 6.25% general rate |
| Pennsylvania | Clothing fully exempt, most food exempt; taxable items differ significantly from SST |
| Arizona | Transaction privilege tax — technically a tax on the *seller's* privilege of doing business, not a sales tax |
| Louisiana | State + parish (county) structure; unique food and drug definitions |
| Colorado | ~70 home-rule cities each administer their own sales tax independently |
| Virginia | Reduced 2.5% food rate; state definitions diverge from SST in several categories |

---

## Database Schema

All 25 tables grouped by domain:

### Tax Rates
| Table | Description |
|---|---|
| `Jurisdictions` | Self-referential hierarchy: Country → State → County → City |
| `TaxRates` | Rate rows — one `IsCurrent=true` per (JurisdictionId, TaxCategoryId) |
| `SourceDocuments` | Evidence files attached to TaxRate rows |
| `ExciseTaxRates` | Excise/specific taxes (alcohol, fuel, tobacco, hotel) |
| `ExciseSourceDocuments` | Evidence for excise rates |
| `ChangeLog` | Detected rate changes (old rate → new rate, timestamp) |
| `ScrapeRuns` | Metadata for each scrape or import batch |

### Product/Service Taxonomy
| Table | Description |
|---|---|
| `TaxCategories` | SST taxonomy hierarchy (root: Goods / Services) |
| `TaxCategoryRules` | Per-jurisdiction taxability overrides for a category |

### Geographic Lookup
| Table | Description |
|---|---|
| `ZipCodes` | ~33,000 ZIPs → (StateJurisdictionId, CountyJurisdictionId, CityJurisdictionId) |

### State Profiles
| Table | Description |
|---|---|
| `StateTaxProfiles` | State-level metadata: rate, SST membership, authority type, agency URL |
| `StateCategoryRules` | State-specific taxability rules per category |

### Subscription / Billing
| Table | Description |
|---|---|
| `PricingConfigs` | Singleton (Id=1): PricePerState, Currency |
| `PayPalConfigs` | Singleton (Id=1): ClientId, ClientSecret, Mode, WebhookId |
| `Subscribers` | Customer accounts |
| `SubscribedStates` | States subscribed to per customer |
| `BillingRecords` | Invoice history |

### Infrastructure
| Table | Description |
|---|---|
| `LogEntries` | Serilog structured log output |
| `AspNetUsers` / `AspNetRoles` / (5 more) | ASP.NET Core Identity tables |

---

## Data Source URLs

All external URLs are stored in `%APPDATA%\MindAttic\TaxRateCollector\settings.json` and editable from Settings → Data Source URLs. The defaults point to the free 2025 Census Bureau files and the current SSUTA agreement.

| Setting Key | Default URL | Used By |
|---|---|---|
| `census_county_gaz_url` | `https://www2.census.gov/geo/docs/maps-data/data/gazetteer/2025_Gazetteer/2025_Gaz_counties_national.zip` | Step 2 Census importer |
| `census_place_gaz_url` | `https://www2.census.gov/geo/docs/maps-data/data/gazetteer/2025_Gazetteer/2025_Gaz_place_national.zip` | Step 2 Census importer |
| `census_zcta_county_url` | `https://www2.census.gov/geo/docs/maps-data/data/rel2020/zcta520/tab20_zcta520_county20_natl.txt` | Steps 2 & 3 |
| `census_zcta_place_url` | `https://www2.census.gov/geo/docs/maps-data/data/rel2020/zcta520/tab20_zcta520_place20_natl.txt` | Steps 2 & 3 |
| `sst_agreement_url` | `https://www.streamlinedsalestax.org/docs/default-source/agreement/ssuta/ssuta-as-amended-through-12-20-24-with-hyperlinks-and-compiler-notes-at-end-clean-for-posting.pdf` | Step 1 SST importer |
| `sst_taxability_matrix_url` | `https://sst.streamlinedsalestax.org/TM` | Validated in Setup Step 1 URL check |
| `sst_member_states_url` | `https://www.streamlinedsalestax.org/about-us/state-information` | Validated in Setup Step 1 URL check |

Setup → Step 1 "Validate Data Source URLs" runs HTTP HEAD checks against all of these before allowing the import pipeline to proceed.

**Cache directory:** `%APPDATA%\MindAttic\TaxRateCollector\cache\`  
The four Census files are cached locally after the first download. Use the "Clear cache" buttons in Setup if you need to force a fresh download.

---

## Settings

Settings file: `%APPDATA%\MindAttic\TaxRateCollector\settings.json`  
Managed by `SettingsService` (singleton). Created with defaults on first run.

| Key | Type | Default | Description |
|---|---|---|---|
| `theme` | string | `"light"` | UI theme (`"light"`, `"dark"`, `"tutor"`, `"llm"`, `"samurai"`) |
| `font` | string | `"outfit"` | Font family |
| `font_size` | int | `14` | Base font size (px) |
| `usps_api_key` | string | `""` | USPS CityStateLookup API key (optional — enriches ZIP city names) |
| `default_update_frequency_days` | int | `90` | How often to re-scrape a jurisdiction |
| `evidence_auto_fetch` | bool | `false` | Auto-capture evidence PDFs on rate save |
| `wayback_machine_fallback` | bool | `true` | Use Wayback Machine if a source URL returns 404 |
| `census_*_url` | string | (see above) | All four Census Bureau source URLs |
| `sst_*_url` | string | (see above) | SSUTA PDF URL and SST web page URLs |

Theme is also written to `localStorage` (key `trc-theme`) so it applies before Blazor's interactive mode initialises, preventing a flash of the wrong theme on page load.

---

## Evidence & Provenance System

Every `TaxRate` row can link to a `SourceDocument` that stores the raw artifact used to establish the rate:

1. **Capture** — raw source stored in `SourceDocument.RawContent` (API JSON, base64 PDF, or HTML)
2. **Hash** — `ContentHash = SHA256(UTF8(RawContent))` as a 64-character lowercase hex string
3. **Verify** — re-hashing `RawContent` and comparing to `ContentHash` proves the document has not been altered
4. **Audit** — `FetchedAt` (ISO 8601 UTC) + `SourceUrl` make the rate independently verifiable without a live network call

Admins can also manually drag-and-drop evidence files (`.pdf`, `.csv`, `.html`, `.xlsx`, `.json`) onto any jurisdiction's detail row or evidence panel. The file is hashed on upload and stored in `%APPDATA%\MindAttic\TaxRateCollector\evidence\`.

| SourceType | MimeType | RawContent format |
|---|---|---|
| `Api` | `application/json` | Raw JSON response body |
| `Pdf` | `application/pdf` | Base64-encoded PDF bytes |
| `Csv` | `text/csv` | Raw CSV text |
| `Website` | `text/html` | Raw HTML |
| `Manual` | `text/plain` | Free-text note or uploaded file reference |

---

## UI Pages

### `/` — Jurisdictions

Lazy-loading hierarchy tree. States load on page init; counties load on state expand; cities load on county expand. Each node shows:
- Tier badge, name, FIPS code
- Current tax rate (editable inline — creates a new `IsCurrent=true` row, retires the old one)
- Cumulative rate range (`∑ min% – max%`) on state/county nodes
- Drag-and-drop evidence zones on each rate row and in the dedicated evidence panel

**Product category picker** — Goods / Services tab bar at the top filters the entire tree to rates for that SST category. Switching tabs reloads all rates with a frosted-glass spinner overlay so stale data remains visible during the reload.

**Role picker** — Fixed overlay (lower-right corner, admin only) to preview the UI as Visitor / Subscriber / Admin. Switching roles reloads the tree in the appropriate access level.

### `/setup` — Setup

Six-step admin pipeline for first-time database population:

| Step | What it does |
|---|---|
| 1. Validate URLs | HTTP HEAD checks all Census + SST source URLs |
| 2. Import SST Taxonomy | Downloads SSUTA PDF, refreshes `TaxCategory` descriptions from Appendix C |
| 3. Import Census Jurisdictions | Downloads Census Gazetteer ZIPs → creates ~3,200 counties + ~30,000 cities |
| 4. Import ZIP Code Crosswalks | Downloads Census ZCTA TXTs → links ~33,000 ZIPs to jurisdictions |
| 5. Assign Source URLs | Manual: set `Jurisdiction.SourceUrl` for each state/county via the Jurisdictions page |
| 6. Run Scrape | Runs `ScrapeOrchestrator` across all jurisdictions with source URLs assigned |

"Run All Steps" executes steps 1–4 in sequence, skipping any already completed.

### `/settings` — Settings

- Theme, font, font size
- All Census + SST source URLs (with per-URL "Test" button)
- USPS API key
- Scrape frequency and evidence auto-fetch toggles
- PayPal credentials (ClientId, ClientSecret, Mode, WebhookId)
- Pricing config (PricePerState)
- Database backup (runs `sqlpackage /Action:Export` to produce a `.bacpac`)

### `/glossary` — Glossary

SST-defined terms from the `TaxCategory.Description` fields, displayed alphabetically.

### `/logs` — Logs

Recent `LogEntry` records from Serilog, filterable by level.

---

## Exports

From the Jurisdictions page export dropdown:

| Format | Description |
|---|---|
| CSV | Flat comma-separated, all columns |
| XLSX | ClosedXML formatted workbook with styled header row |
| SQL | `INSERT INTO` statements, portable to any RDBMS |
| HTML | Standalone self-styled HTML table |

All exports download via `downloadBase64File` JS interop — no temp files on the server.

---

## Database Migrations

The EF Core tools target is `TaxRateCollector.Infrastructure`. Run from the repo root:

```bash
# Apply migrations (not normally needed — Program.cs does this at startup)
dotnet ef database update \
  --project TaxRateCollector.Infrastructure \
  --startup-project TaxRateCollector.Blazor

# Create a new migration after changing entities
dotnet ef migrations add <MigrationName> \
  --project TaxRateCollector.Infrastructure \
  --startup-project TaxRateCollector.Blazor

# Drop and recreate (dev only)
dotnet ef database drop \
  --project TaxRateCollector.Infrastructure \
  --startup-project TaxRateCollector.Blazor
```

**Migration history:**

| Migration | Description |
|---|---|
| `InitialCreate` | All core tables: Jurisdictions, TaxRates, TaxCategories, StateTaxProfiles, ZipCodes, Identity, billing, logs |
| `AddPerCategoryRateIndex` | Filtered index on `TaxRates (JurisdictionId, TaxCategoryId)` where `IsCurrent=1` |
| `AddTaxCategoryToTaxRate` | Added `TaxCategoryId` FK on `TaxRates` |
| `ClearTaxCategories` | Clears taxonomy for re-import |
| `AddStateTaxProfiles` | Adds `StateTaxProfiles` and `StateCategoryRules` tables |

---

## NUnit Tests

```bash
# Unit tests only (no SQL Server required)
dotnet test TaxRateCollector.UnitTests

# Integration tests (requires SQL Server LocalDB with migrations applied)
dotnet test TaxRateCollector.UnitTests --filter Category=Integration
```

### `SetupTests/SstTaxonomyStructureTests.cs`
Pure unit tests against `SstTaxonomyData.Definitions` — no database needed.

| Test | Verifies |
|---|---|
| `Roots_AreGoodsAndServices` | Exactly two root nodes named Goods and Services |
| `AllLeaves_HaveParent` | No leaf category is a root |
| `AllParents_Exist` | Every `ParentName` reference resolves to a definition |
| `NoDuplicateNames` | No two definitions share the same name |
| `NoCircularReferences` | Depth-first walk finds no cycles |
| `AllHaveNonEmptyTopLevelType` | Every node has `TopLevelType = "Goods"` or `"Services"` |
| `SortOrders_ArePositive` | All `SortOrder` values > 0 |
| + 4 more | Leaf counts, parent counts, name length limits |

### `SetupTests/TaxCategorySeederTests.cs`
EF Core InMemory database — fast, no SQL Server needed.

| Test | Verifies |
|---|---|
| `SeedAsync_PopulatesCategories` | Count > 0 after seed |
| `SeedAsync_CountMatchesDefinitions` | Count equals `SstTaxonomyData.Definitions.Length` |
| `SeedAsync_HasExactlyTwoRoots` | Two rows with `ParentId = null` |
| `SeedAsync_IsIdempotent` | Second call does not add rows |
| `SeedAsync_AllLeaves_HaveParent` | No leaf without a parent row |
| `SeedAsync_AllHaveValidTopLevelType` | Only "Goods" or "Services" |
| `SeedAsync_AllHavePositiveSortOrder` | `SortOrder > 0` for all rows |
| `SeedAsync_AllHaveNonEmptyNames` | No blank names |

### `SetupTests/AppSettingsTests.cs`
Verifies `AppSettings` defaults and URL validity.

| Test group | Verifies |
|---|---|
| Default values | Theme="light", Font="outfit", FontSize=14, UpdateFrequency=90 |
| All URLs non-empty and HTTPS | Seven source URLs are all `https://` |
| Census URLs → census.gov | Hostname contains `census.gov` |
| SST URLs → streamlinedsalestax.org | Hostname matches |
| JSON round-trip | Serialize → deserialize preserves all values |
| Unknown key ignored | Extra JSON key does not throw |

### `SetupTests/DatabaseBackupTests.cs`
Unit tests for connection string parsing in `Settings.razor CreateBackup()`.

| Test | Verifies |
|---|---|
| `Parse_LocalDbConnectionString` | Extracts `(localdb)\MSSQLLocalDB` and `TaxRateCollector` |
| `Parse_DataSourceKeyword` | Recognises `Data Source=` alias for `Server=` |
| `Parse_InitialCatalogKeyword` | Recognises `Initial Catalog=` alias for `Database=` |
| `Parse_EmptyString` | Returns empty strings without throwing |
| `Parse_KeysAreCaseInsensitive` | `SERVER=` and `server=` both work |
| `Parse_TrailingSemicolon` | Handled cleanly |
| `Parse_TestConnectionString` | `TestDbConnection.ConnectionString` has both keys |
| `SqlPackage_IsAvailableOnPath` _(Integration)_ | `sqlpackage /version` exits 0 |

### `SetupTests/DatabasePopulationIntegrationTests.cs`
Requires SQL Server LocalDB with migrations applied. Run with `--filter Category=Integration`.

Covers:
- `TaxCategories` populated, exactly two roots (Goods + Services), no leaf roots, valid `TopLevelType`
- `StateTaxProfiles` exactly 51 rows, all have 2-char state codes, non-negative rates, non-empty state names
- `Jurisdictions` has US country row, exactly 51 state rows
- `PricingConfig` and `PayPalConfig` have at least one row each, `PricePerState > 0`
- `TaxCategorySeeder.SeedAsync` and `StateTaxProfileSeeder.SeedAsync` are idempotent against the real DB

---

## Scraper Framework

Each state has one or more `IScrapeStrategy` implementations registered in DI:

```csharp
public interface IScrapeStrategy
{
    string StrategyKey { get; }
    bool CanHandle(Jurisdiction jurisdiction);
    Task<IReadOnlyList<RawScrapeResult>> ScrapeAsync(
        Jurisdiction jurisdiction, CancellationToken ct = default);
}
```

The `Sanitizer` helper normalises raw rate strings like `"6.25%"`, `"0.0625"`, or `"$0.231/pack"` to `decimal?`.

The `ScrapeSchedulerService` (`IHostedService`) re-scrapes each jurisdiction on the configured cadence (`default_update_frequency_days`). After each run, `DiffEngine` compares the new rate to `IsCurrent=true` and writes a `ChangeLogEntry` if they differ.

---

## Adding a New State Scraper

1. Create `TaxRateCollector.Infrastructure/Scrapers/Strategies/MyStateScraper.cs`
2. Implement `IScrapeStrategy`: set `StrategyKey`, implement `CanHandle` (return `true` when `jurisdiction.StateCode == "XX"`), implement `ScrapeAsync`
3. Register in `Program.cs`:
   ```csharp
   builder.Services.AddScoped<IScrapeStrategy, MyStateScraper>();
   ```
4. Set `Jurisdiction.SourceUrl` for the state row to the canonical `.gov` source URL (via Settings → Jurisdictions or the Setup source-URL step)
5. Write a unit test verifying parser output against a fixture response

---

## Roles & Subscriptions

ASP.NET Core Identity is implemented with two roles: **Administrator** and **Subscriber**.

- **Admin** — full read/write access to rates, evidence, setup pipeline, settings
- **Subscriber** — read-only access to rate data, filtered by subscribed states
- **Guest** — public view, rates redacted behind a "🔒 Locked" blur

The `ViewAsService` (singleton per user session) lets admins preview the UI as any role without logging out. The role picker overlay (lower-right corner, admin-only) switches between Visitor / Subscriber / Admin preview modes.

**Subscription model:** Pay-per-county at ~$0.01/county/month. Subscribers select states they need; billing is calculated from the county count in those states. PayPal handles checkout. `PricingConfig.PricePerState` is the admin-configurable unit price.

---

## Deploying to Azure

### Dockerfile

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish TaxRateCollector.Blazor -c Release -o /app/publish

FROM base AS final
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TaxRateCollector.Blazor.dll"]
```

### Azure App Service

```bash
az login
az group create --name rg-taxratecollector --location eastus
az appservice plan create --name asp-taxratecollector \
  --resource-group rg-taxratecollector --sku B1 --is-linux
az webapp create --name taxratecollector \
  --resource-group rg-taxratecollector \
  --plan asp-taxratecollector --runtime "DOTNETCORE:10.0"
```

Store the SQL Server connection string in Azure Key Vault and reference it via `builder.Configuration.GetConnectionString("DefaultConnection")`. The app already uses `UseSqlServer` — no code changes needed.

---

## License

Internal tool — MindAttic proprietary. Not for public distribution.
