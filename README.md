# TaxRateCollector

A Blazor Server application that builds and maintains an exhaustively researched, evidence-backed master table of US sales and excise tax rates for all jurisdictions (Country → State → County → City). Every tax rate row stores the raw source document — API response, PDF, or website capture — and a SHA-256 content hash to prove its veracity without requiring a live network call.

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Prerequisites](#prerequisites)
3. [Project Structure](#project-structure)
4. [Getting Started (Local Dev)](#getting-started-local-dev)
5. [Database Migrations](#database-migrations)
6. [Data Model](#data-model)
7. [Evidence & Provenance System](#evidence--provenance-system)
8. [Scraper Framework](#scraper-framework)
9. [Settings](#settings)
10. [UI Pages](#ui-pages)
11. [Exports (Master Table)](#exports-master-table)
12. [NUnit Tests](#nunit-tests)
13. [Planned: Roles & Subscriptions](#planned-roles--subscriptions)
14. [Deploying to Azure](#deploying-to-azure)
15. [Adding a New State Scraper](#adding-a-new-state-scraper)

---

## Architecture Overview

```
TaxRateCollector.Core           Domain entities, enums, interfaces, constants
TaxRateCollector.Infrastructure EF Core, migrations, seeder, scrapers, services
TaxRateCollector.Frontend       Blazor Server UI, pages, settings, exports
TaxRateCollector.UnitTests      NUnit 4 — hierarchy, rate calculation, evidence hash
```

**Stack:**

| Layer | Technology |
|---|---|
| UI | ASP.NET Core 10, Blazor Server (InteractiveServer) |
| ORM | EF Core 10 |
| Database (dev) | SQLite |
| Database (prod) | Azure SQL (planned) |
| XLSX export | ClosedXML 0.105 |
| HTML scraping | HtmlAgilityPack |
| CSV parsing | CsvHelper |
| Logging | Serilog.AspNetCore |
| Testing | NUnit 4, EF Core InMemory |

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git
- (Optional) Visual Studio 2022 17.10+ or Rider 2024.1+

---

## Project Structure

```
TaxRateCollector/
├── TaxRateCollector.Core/
│   ├── Constants/
│   │   └── TaxRateConstants.cs         JurisdictionTier, TaxSourceType, TaxCategory string constants
│   ├── Entities/
│   │   ├── ChangeLogEntry.cs           Rate-change audit log
│   │   ├── ExciseTaxRate.cs            Sin/excise tax (alcohol, tobacco, cannabis, etc.)
│   │   ├── Jurisdiction.cs             Self-referential hierarchy node (Country/State/County/City)
│   │   ├── JurisdictionData.cs         Canon-pattern domain models (JSON-first DTOs)
│   │   ├── ScrapeRun.cs                Scrape run metadata (status, timestamps, counts)
│   │   ├── SourceDocument.cs           Evidence doc attached to a TaxRate row
│   │   └── TaxRate.cs                  Tax rate row (rate, effective date, raw value, IsCurrent)
│   ├── Enums/
│   │   ├── ChangeType.cs
│   │   ├── JurisdictionType.cs         Country=0, State=1, County=2, City=3, District=4
│   │   ├── ProductCategory.cs          Alcohol, Cigarettes, Cannabis, Fuel, Hotel, etc.
│   │   ├── ScrapeStatus.cs             Running, Completed, Failed, Manual
│   │   └── SourceType.cs               Api, Pdf, Csv, Website, Manual
│   └── Interfaces/
│       ├── ICanonEntity.cs             Marker interface for canon-pattern domain objects
│       ├── IDiffEngine.cs
│       ├── IScrapeOrchestrator.cs
│       ├── IScrapeStrategy.cs          Per-state scrape plug-in contract
│       └── ITaxCalculator.cs
│
├── TaxRateCollector.Infrastructure/
│   ├── Data/
│   │   └── AppDbContext.cs             EF Core DbContext, fluent configuration
│   ├── Migrations/
│   │   ├── 20260411172034_InitialCreate.*
│   │   ├── 20260415000001_AddHierarchyAndSourceDocument.*
│   │   ├── 20260415000002_AddExciseTaxRates.*
│   │   └── AppDbContextModelSnapshot.cs
│   ├── Scrapers/
│   │   ├── ScrapeOrchestrator.cs       Loops jurisdictions, dispatches to matching IScrapeStrategy
│   │   ├── Sanitizer.cs                Rate string cleaning / normalization
│   │   └── Strategies/
│   │       ├── IllinoisTableScraper.cs HTML table parser for tax.illinois.gov
│   │       ├── CaliforniaCsvScraper.cs CSV parser for CDTFA rate file
│   │       └── TexasExcelScraper.cs    XLSX parser for comptroller.texas.gov rate file
│   ├── Seeding/
│   │   └── JurisdictionSeeder.cs       Seeds ~700 jurisdictions with 2024 rates on first run
│   └── Services/
│       ├── AlertService.cs             In-process alert bus (toast notifications)
│       ├── DiffEngine.cs               Compares scraped rate to current; creates ChangeLogEntry
│       ├── ScrapeSchedulerService.cs   IHostedService: runs scrapers on configurable cadence
│       ├── SettingsService.cs          Reads/writes %APPDATA%\MindAttic\TaxRateCollector\settings.json
│       └── TaxCalculator.cs            Walks ParentId chain to compute combined rate
│
├── TaxRateCollector.Frontend/
│   ├── Components/
│   │   ├── App.razor                   Root component + theme init script
│   │   ├── Layout/
│   │   │   ├── MainLayout.razor        3-tab shell: Jurisdictions | Master Table | Settings
│   │   │   └── NavMenu.razor
│   │   └── Pages/
│   │       ├── Jurisdictions.razor     Lazy-loading hierarchy tree, inline rate edit, evidence panel
│   │       ├── MasterTable.razor       Full dataset table with filter, sort, and export
│   │       └── Settings.razor          Theme, USPS API key, scraper preferences
│   ├── wwwroot/
│   │   ├── app.css                     CSS custom properties (light/dark tokens), page styles
│   │   └── site.js                     applyTheme(), initTheme(), downloadBase64File()
│   └── Program.cs                      DI registration, migrate+seed on startup
│
└── TaxRateCollector.UnitTests/
    ├── EvidenceTests/
    │   └── EvidenceValidationTests.cs  SHA-256 hashing, SourceDocument integrity, MIME mapping
    └── JurisdictionTests/
        └── HierarchyTests.cs           Combined rate calculation, hierarchy structure, rate uniqueness
```

---

## Getting Started (Local Dev)

```bash
# 1. Clone the repo
git clone https://github.com/mindattic/TaxRateCollector.git
cd TaxRateCollector

# 2. Restore packages
dotnet restore

# 3. Run the frontend (migrations + seeder run automatically on first launch)
dotnet run --project TaxRateCollector.Frontend

# 4. Open in browser
#    https://localhost:5001  (or whatever port the console prints)
```

The SQLite database file (`taxrates.db`) is created in the frontend content root on first run. All migrations are applied automatically and the jurisdiction seeder populates the initial dataset (~700 jurisdictions) if the Country node does not yet exist.

---

## Database Migrations

The EF Core tools target is `TaxRateCollector.Infrastructure`. Run all migration commands from the repo root:

```bash
# Apply migrations manually (not normally needed — Program.cs does this at startup)
dotnet ef database update \
  --project TaxRateCollector.Infrastructure \
  --startup-project TaxRateCollector.Frontend

# Create a new migration after changing entities
dotnet ef migrations add <MigrationName> \
  --project TaxRateCollector.Infrastructure \
  --startup-project TaxRateCollector.Frontend

# Roll back to a specific migration
dotnet ef database update <MigrationName> \
  --project TaxRateCollector.Infrastructure \
  --startup-project TaxRateCollector.Frontend

# Drop the database entirely (dev only)
dotnet ef database drop \
  --project TaxRateCollector.Infrastructure \
  --startup-project TaxRateCollector.Frontend
```

**Migration history:**

| Migration | Description |
|---|---|
| `20260411172034_InitialCreate` | Jurisdictions, TaxRates, ScrapeRuns, ChangeLog |
| `20260415000001_AddHierarchyAndSourceDocument` | ParentId self-ref FK on Jurisdiction; SourceDocuments table |
| `20260415000002_AddExciseTaxRates` | ExciseTaxRates and ExciseSourceDocuments tables |

---

## Data Model

### Jurisdiction hierarchy

```
Country (US, FipsCode="US")
  └── State (IL, FipsCode="17")
        └── County (Cook County, FipsCode="17031")
              └── City (Chicago, FipsCode="1714000")
```

Each `Jurisdiction` row has a nullable `ParentId` foreign key pointing to its parent. The root Country node has `ParentId = null`. The self-referential FK uses `DeleteBehavior.Restrict` to prevent accidental cascade-deletes of an entire subtree.

**Combined rate** for a purchase = sum of `TaxRate.Rate` for the city + its county + its state. The Country-level rate is always 0 (the US has no federal sales tax). The `TaxCalculator` service and the Master Table page both walk the `ParentId` chain to compute this sum.

### Key entities

**`Jurisdiction`** — a node in the hierarchy.  
Fields: `JurisdictionName`, `JurisdictionType` (enum), `StateCode`, `FipsCode` (unique index), `SourceUrl`, `IsActive`, `ParentId`.

**`TaxRate`** — a rate row tied to a jurisdiction and a scrape run.  
Fields: `Rate` (decimal), `RateType` ("General", "Reduced", etc.), `RawValue` (original string from source, e.g. `"6.25%"`), `EffectiveDate`, `ScrapedAt`, `IsCurrent`, optional navigation to `SourceDocument`.  
Only one row per jurisdiction should have `IsCurrent = true`. When a rate changes, the old row is set `IsCurrent = false` and a new row is inserted.

**`SourceDocument`** — evidence attached to a `TaxRate`.  
Fields: `SourceType` (Api/Pdf/Csv/Website/Manual), `SourceUrl`, `MimeType`, `FetchedAt`, `ContentHash` (SHA-256 hex), `RawContent` (full API JSON body or base64-encoded PDF/HTML).

**`ExciseTaxRate`** — sin/excise rate keyed by `ProductCategory` enum.  
Fields: `Rate`, `RateType` (`"percentage"` or `"flat"`), `Unit` (e.g. `"per pack"`), optional navigation to `ExciseSourceDocument`.

**`ScrapeRun`** — tracks each automated or manual scrape batch.  
`Status` enum: `Running`, `Completed`, `Failed`, `Manual`. All UI-entered rates reference a shared `ScrapeStatus.Manual` run created once by the seeder.

**`ChangeLogEntry`** — written by `DiffEngine` when a newly scraped rate differs from the current stored rate.  
Fields: `OldRate`, `NewRate`, `DetectedAt`, `ChangeType`, `Acknowledged`.

---

## Evidence & Provenance System

Every `TaxRate` row optionally links to a `SourceDocument` that stores the raw artifact used to establish the rate. The integrity guarantee works as follows:

1. **Capture** — when a rate is scraped, the raw source (full API JSON response, base64 PDF, or raw HTML) is stored in `SourceDocument.RawContent`.
2. **Hash** — `ContentHash` is set to `SHA256(UTF8(RawContent))` encoded as a lowercase 64-character hex string.
3. **Verify** — at any time, re-hashing `RawContent` and comparing to `ContentHash` proves the stored document has not been altered. Mismatched hashes indicate tampering.
4. **Audit** — `FetchedAt` (ISO 8601 UTC) records when the document was captured. `SourceUrl` records the origin. Together these make a rate independently verifiable by any third party without a live network call.

The `TaxSourceProvenance` domain model (in `JurisdictionData.cs`) mirrors this for the JSON/DTO layer, with `RawResponse`, `DocumentHash`, `SourceUri`, `ContentType`, and `RetrievedAt`.

### Evidence sources

| `SourceType` | `MimeType` | `RawContent` format |
|---|---|---|
| `Api` | `application/json` | Raw JSON response body |
| `Pdf` | `application/pdf` | Base64-encoded PDF bytes |
| `Csv` | `text/csv` | Raw CSV text |
| `Website` | `text/html` | Raw HTML or base64 PDF print |
| `Manual` | `text/plain` | Free-text note |

### Wayback Machine fallback (planned)

When a live `.gov` URL returns 404, the scraper will query `https://web.archive.org/cdx/search/cdx` for the most recent cached snapshot, fetch that, and store it as evidence. Enable this in Settings → "Wayback Machine fallback".

---

## Scraper Framework

Each state has one or more `IScrapeStrategy` implementations registered in DI. The orchestrator loops over jurisdictions and calls `strategy.CanHandle(jurisdiction)` to find the right plug-in.

```csharp
public interface IScrapeStrategy
{
    string StrategyKey { get; }
    bool CanHandle(Jurisdiction jurisdiction);
    Task<IReadOnlyList<RawScrapeResult>> ScrapeAsync(
        Jurisdiction jurisdiction,
        CancellationToken ct = default);
}
```

**Existing strategies:**

| Strategy | Source format | Target domain |
|---|---|---|
| `IllinoisTableScraper` | HTML table | `tax.illinois.gov` |
| `CaliforniaCsvScraper` | CSV download | `cdtfa.ca.gov` |
| `TexasExcelScraper` | XLSX download | `comptroller.texas.gov` |

The `Sanitizer` helper normalises raw rate strings like `"6.25%"`, `"$0.231/pack"`, or `"0.0625"` to a nullable `decimal`.

The `ScrapeSchedulerService` (`IHostedService`) checks each jurisdiction on its configured cadence (`DefaultUpdateFrequencyDays`) and triggers the orchestrator. After each run the `DiffEngine` compares the new rate to `IsCurrent = true` and writes a `ChangeLogEntry` if they differ.

---

## Adding a New State Scraper

1. Create `TaxRateCollector.Infrastructure/Scrapers/Strategies/MyStateScraper.cs`
2. Implement `IScrapeStrategy`: set `StrategyKey`, implement `CanHandle` (return `true` when `jurisdiction.StateCode == "XX"`), implement `ScrapeAsync`
3. Register in `Program.cs`:
   ```csharp
   builder.Services.AddScoped<IScrapeStrategy, MyStateScraper>();
   ```
4. Update `Jurisdiction.SourceUrl` for the target state row in `JurisdictionSeeder.cs` to point at the canonical `.gov` source URL
5. Write a unit test in `TaxRateCollector.UnitTests` verifying the parser output against a fixture response

---

## Settings

Settings are persisted at `%APPDATA%\MindAttic\TaxRateCollector\settings.json` and managed by `SettingsService` (singleton).

| Key | Type | Default | Description |
|---|---|---|---|
| `theme` | string | `"light"` | `"light"` or `"dark"` |
| `usps_api_key` | string | `""` | USPS Address API key |
| `default_update_frequency_days` | int | `90` | How often to re-scrape a jurisdiction |
| `evidence_auto_fetch` | bool | `false` | Auto-capture evidence on rate save |
| `wayback_machine_fallback` | bool | `true` | Fall back to archive.org if live URL 404s |

**Theme** is also persisted to `localStorage` (key `trc-theme`) so it takes effect before Blazor's interactive mode initialises, preventing a flash of the wrong theme on page load.

---

## UI Pages

### Jurisdictions (`/`)

Lazy-loading hierarchy tree. States load on page init. Counties load when a state node is expanded. Cities load when a county node is expanded. Each node shows:

- Tier badge (State / County / City)
- Jurisdiction name and FIPS code
- Current tax rate (editable inline — saves via the "Manual" ScrapeRun)
- Evidence badge (green = has evidence, grey = none)
- Expandable evidence panel: source type, URL, fetch timestamp, SHA-256 hash, raw content preview

City nodes also show the **combined rate** (`∑ X.XXX%`), which is the actual effective sales tax rate for a purchase made there.

Rate saves work as follows:
1. Look up or create a `ScrapeRun` with `Status = Manual`
2. Set `IsCurrent = false` on the existing rate for the jurisdiction
3. Insert a new `TaxRate` row with `IsCurrent = true`

### Master Table (`/master`)

Full dataset table with:

- Text search (name, FIPS, state code)
- Filters: tier, state, evidence status
- Sort on any column header
- Export to CSV, XLSX, or SQL INSERT statements (all download via `downloadBase64File` JS interop — no temp files on server)

### Settings (`/settings`)

- Light / Dark theme toggle (writes both `settings.json` and `localStorage`)
- USPS API key input
- Scraping frequency (days between automated re-scrapes)
- Evidence auto-fetch toggle
- Wayback Machine fallback toggle

---

## Exports (Master Table)

| Format | Library | Description |
|---|---|---|
| CSV | System.Text / StringBuilder | Flat comma-separated, all columns |
| XLSX | ClosedXML 0.105 | Formatted workbook with header row |
| SQL | StringBuilder | `INSERT INTO TaxRates (...)` statements, portable to any RDBMS |

---

## NUnit Tests

```bash
dotnet test TaxRateCollector.UnitTests
```

**`EvidenceTests/EvidenceValidationTests.cs`**

| Test | What it verifies |
|---|---|
| `Hash_SameContent_ProducesSameHash` | Deterministic SHA-256 |
| `Hash_DifferentContent_ProducesDifferentHash` | Different content → different hash |
| `Hash_EmptyString_ProducesKnownHash` | Known `e3b0c...` value |
| `Hash_64CharHexOutput` | Output is 64 lowercase hex chars |
| `SourceDocument_HashMatchesContent_PassesVerification` | Re-hash of RawContent matches ContentHash |
| `SourceDocument_TamperedContent_FailsVerification` | Changed content does not match original hash |
| `SourceDocument_Base64Pdf_RoundTrips` | Base64 encode → store → decode → original bytes |
| `SourceType_MimeMapping_IsConsistent` | Enum→MIME string switch is complete |
| `SourceUrl_GovernmentDomains_AreValid` | .gov HTTPS URLs parse correctly |
| `SourceUrl_InvalidUrls_FailValidation` | Empty, non-URL, ftp:// all rejected |
| `ExciseTaxRate_FlatRate_RoundTrips` | Flat-rate properties survive round-trip |
| `ExciseTaxRate_PercentageRate_IsInBounds` | Percentage rate in [0, 1] |
| `TaxSourceProvenance_EmptyByDefault` | Default strings are empty, not null |
| `TaxSourceProvenance_WithApiResponse_IsPopulated` | Hash length and round-trip verification |

**`JurisdictionTests/HierarchyTests.cs`**

| Test | What it verifies |
|---|---|
| `CombinedRate_City_SumsStatePlusCountyPlusCity` | IL 6.25% + Cook 1.75% + Chicago 2.25% = 10.25% |
| `CombinedRate_StateOnly_ReturnsSingleRate` | TX 6.25% with no county/city |
| `CombinedRate_ZeroRateCountyAndCity_EqualsStateRate` | Oregon all-zero scenario |
| `Hierarchy_CountyParentIsState` | ParentId FK chain is correct |
| `Hierarchy_SeederDoesNotSeedIfCountryExists` | Idempotency guard works |
| `TaxRate_OnlyOneCurrentRatePerJurisdiction` | Rate retirement (IsCurrent=false) works |
| `SeederConstants_KnownStateFipsCodes` | FIPS codes for CA/IL/TX match US Census values |
| `RateRange_ValidValues_AreInExpectedBounds` | 0% – 15% accepted |
| `RateRange_OutOfBoundsValues_FailValidation` | -1% and 50% rejected |

---

## Planned: Roles & Subscriptions

- **ASP.NET Core Identity** with two roles: `Administrator` (full write access) and `Subscriber` (read-only Master Table).
- Admin credentials stored in `secrets.json` / `.env` for local development.
- **PayPal subscription** at $0.01/month (test tier). On successful payment webhook, the user is granted the `Subscriber` role.
- Reference the [StreetSamurai repo](https://github.com/mindattic/StreetSamurai) for the existing PayPal webhook + role-grant pattern.

---

## Deploying to Azure

> Full CI/CD pipeline is not yet implemented. These are the planned steps.

### Dockerfile (to be created)

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish TaxRateCollector.Frontend -c Release -o /app/publish

FROM base AS final
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TaxRateCollector.Frontend.dll"]
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
az webapp deploy --resource-group rg-taxratecollector \
  --name taxratecollector --src-path ./publish.zip --type zip
```

### GitHub Actions CI/CD

Reference `.github/workflows/` in the StreetSamurai repo for the reusable pipeline template. Key stages:

1. `dotnet restore && dotnet build`
2. `dotnet test TaxRateCollector.UnitTests`
3. `dotnet publish -c Release -o publish/`
4. Upload artifact + deploy via `azure/webapps-deploy@v3`

### Production database

Swap `UseSqlite` for `UseSqlServer` in `Program.cs` and store the connection string in Azure Key Vault:

```csharp
builder.Services.AddDbContextFactory<AppDbContext>(opts =>
    opts.UseSqlServer(builder.Configuration.GetConnectionString("AzureSql")));
```

### Evidence document storage

Large `SourceDocument.RawContent` blobs (base64 PDFs, full HTML) can exceed SQL row-size limits. The long-term plan is to store raw content in **Azure Blob Storage** and keep only the blob URI + SHA-256 hash in the database row.

---

## USPS Address Validation (planned)

Every jurisdiction will be validated against the USPS Address API using the [USPSAddressValidator](https://github.com/mindattic/USPSAddressValidator) library (modern JSON endpoint). A background job will iterate all jurisdictions and set `Jurisdiction.UspsValidated = true` + `UspsValidatedAt` on success. A migration for these two columns has not been created yet — see [TODO.md](TODO.md) for the full backlog.

---

## Target Corpus

| Tier | Count |
|---|---|
| Country | 1 |
| States + DC | 51 |
| Counties | ~3,144 |
| Cities / municipalities | ~10,000 |
| **Total** | **~14,000** |

The seeder currently covers ~700 jurisdictions (full state list + representative counties and cities). Expanding to complete coverage is the top data-completeness priority.

---

## License

Internal tool — MindAttic proprietary. Not for public distribution.
