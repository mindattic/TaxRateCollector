# TaxRateCollector

Blazor Server app that keeps a US sales and excise tax rate database where every rate carries the official .gov source document it came from, SHA-256 hashed and timestamped.

[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/) [![Blazor Server](https://img.shields.io/badge/Blazor-Server-512BD4)](https://learn.microsoft.com/aspnet/core/blazor/) [![EF Core 10](https://img.shields.io/badge/EF%20Core-10-512BD4)](https://learn.microsoft.com/ef/core/) [![SQL Server](https://img.shields.io/badge/SQL%20Server-LocalDB%20and%20Azure%20SQL-CC2927)](https://learn.microsoft.com/sql/) [![Status](https://img.shields.io/badge/status-active%20development-yellow)](docs/BIBLE.md)

```text
   official .gov source (PDF / CSV / XLSX / HTML / API)
                     │  scrape or AI-assisted discovery
                     ▼
   SourceDocument  ── raw bytes + SHA-256 ContentHash + FetchedAt + SourceUrl
                     │  evidence first (TRC-LAW-1)
                     ▼
   TaxRate  ── bound to a Jurisdiction (Country → State → County → City)
                     │  DiffEngine compares against IsCurrent rows (TRC-LAW-4)
                     ▼
   ChangeLogEntry  ── only when a rate is new, changed, removed or restructured
                     │
                     ▼
   Blazor UI: rate tree, calculator, review queue, change log, CSV / XLSX / SQL / HTML exports
```

Most tax-rate APIs hand you a number. TaxRateCollector keeps the number together with the government PDF, CSV or API response it came from, so an audit can trace every rate back to the source that published it.

## Why

- Answer an auditor with a document, not a shrug: each `TaxRate` is bound to a `SourceDocument` holding the raw `.gov` artifact, its SHA-256 content hash and the `FetchedAt` timestamp.
- Know the moment a rate moves: a background scheduler re-checks each jurisdiction's source on a configurable cadence, and `DiffEngine` writes a `ChangeLogEntry` only when something actually changed.
- Model excise and "sin" taxes as they are written, not as a flat percentage: per-unit, per-volume, per-proof-gallon, per-weight and percentage-of-wholesale bases, brackets, per-unit caps, compound tax-on-tax, ABV gating and origin/destination sourcing.
- Let AI find candidate rates without letting it publish them: every AI-extracted rate lands with `NeedsReview=true` until an admin approves or rejects it.
- Drop the data into a billing stack with CSV, XLSX, SQL and HTML exports.

## Features

### Jurisdictions and taxonomy

- A self-referential Country, State, County, City hierarchy (`Jurisdiction`) seeded from US Census Bureau gazetteers, with ZIP-to-jurisdiction lookup over the Census ZCTA crosswalk (~33,000 ZCTAs).
- An SSUTA-aligned product taxonomy: ~200 SST taxonomy nodes from the Streamlined Sales and Use Tax Agreement Appendix C, with documented per-state overrides for non-member states.
- 51 `StateTaxProfile` rows (50 states plus DC).

### Rate collection

- Structured scrapers per source format (`IScrapeStrategy`): California CSV, Illinois HTML table, Texas XLSX.
- Per-state bulk scrapers (`IStateBulkScraper`): Wisconsin general sales tax, plus alcohol and excise scrapers for Wisconsin, Illinois, Minnesota, Iowa, Indiana, Michigan, North Dakota, South Dakota, Ohio, Montana, Idaho and Oregon.
- AI-assisted discovery: `RecursiveRateScraper` walks a jurisdiction hierarchy and asks an LLM, routed through `MindAttic.Legion`, to extract candidate rate laws with evidence attached.

### Working with the data

- A lazy-loading jurisdiction tree with inline rate editing and a drag-and-drop evidence panel.
- A point-of-sale calculator: ZIP to tiers to combined total.
- A review queue for AI-extracted rates, scrape-run history with pause and resume, a change log and an application log viewer.
- Exports to CSV, XLSX, SQL and HTML from the Jurisdictions page.
- Subscriber access by state and category, checked out through PayPal.

## Quick start

Prerequisites:

- .NET 10 SDK.
- SQL Server LocalDB (`sqllocaldb create MSSQLLocalDB`) for local development.
- Optional: an Anthropic key resolvable through the shared MindAttic credential store (`MindAttic.Vault`) for AI rate-law extraction. Without one, `StubRateLawExtractor` is used and nothing throws.

```powershell
git clone https://github.com/mindattic/TaxRateCollector.git
cd TaxRateCollector
dotnet restore

# Optional dev admin (Program.cs reads these via IConfiguration)
$env:DEV_ADMIN_EMAIL = "admin@example.com"
$env:DEV_ADMIN_PASSWORD = "..."

# Migrations and seeders run automatically on first launch
dotnet run --project TaxRateCollector.Blazor
```

Open the URL printed in the console (see `TaxRateCollector.Blazor/Properties/launchSettings.json` for the exact port). In Development only, `GET /dev-login` signs the dev admin in directly. Then open `/setup` to run the data import pipeline.

On first launch `Program.cs`:

1. Applies EF Core migrations (`db.Database.MigrateAsync()`).
2. Seeds `TaxCategories` (~200 SST nodes) and `Jurisdictions` (Country + 51 states).
3. Ensures Census counties and cities are present, importing automatically if there are fewer than 3,000 counties or 5,000 cities. This can take 20 to 40 minutes on a cold cache.
4. Ensures ZIP crosswalks are present, importing automatically if there are fewer than 30,000 ZIP rows (15 to 30 minutes cold).
5. Seeds `StateTaxProfiles` (51 profiles), `PricingConfig` and `PayPalConfig`.
6. Ensures the `Administrator` and `Approver` Identity roles exist, and seeds the dev admin user (when `DEV_ADMIN_EMAIL` and `DEV_ADMIN_PASSWORD` are set) and demo subscribers (`DemoSubscriberSeeder`).

For a fully seeded database without the long import, restore the local `.bacpac` backup (see [Database](#database)).

## What it is and is not

| It is | It is not |
|---|---|
| A Blazor Server app plus a background Worker that maintain an evidence-backed master table of US sales and excise tax rates | A live public tax-calculation REST or GraphQL API; there is no such endpoint yet ([TRC-US-G1](docs/USER_STORIES.md), backlog) |
| Backed by SQL Server (LocalDB in dev, Azure SQL in prod) via EF Core 10 | A SQLite app; SQL Server is the only datastore ([TRC-LAW-8](docs/BIBLE.md#TRC-LAW-8)) |
| A soft-delete system: rates retire (`IsCurrent=false`), jurisdictions deactivate (`IsActive=false`) | A hard-delete system ([TRC-LAW-2](docs/BIBLE.md#TRC-LAW-2)) |
| Provider-agnostic for LLM calls, via `MindAttic.Legion` | Locked to any one LLM vendor SDK ([TRC-LAW-3](docs/BIBLE.md#TRC-LAW-3)) |

## How it works

```text
                  +------------------------------+
                  |  TaxRateCollector.Blazor      |  Blazor Server UI (InteractiveServer),
                  |  (front door: interactive)    |  pages, exports, DI composition root
                  +---------------+---------------+
                                  |
   +------------------------------+------------------------------+
   |                              |                              |
+--v---------------+   +----------v-----------+   +--------------v-------+
| TaxRateCollector |   | TaxRateCollector      |   | TaxRateCollector     |
| .Worker          |   | .Infrastructure       |   | .UnitTests           |
| (front door:     |   | EF Core 10, scrapers, |   | NUnit 4, InMemory +  |
|  background)     |   | seeders, services     |   | LocalDB integration  |
+--------+---------+   +----------+------------+   +----------------------+
         |                        |
         |             +----------v-----------+
         +------------>| TaxRateCollector.Core |  Entities, Enums, Interfaces, Options
                       | (nouns + contracts)   |
                       +----------------------+
                                  |
                          +-------v--------+
                          |  SQL Server    |  LocalDB (dev) / Azure SQL (prod)
                          +----------------+
```

The Blazor host and the Worker are two front doors over one engine: both register the same `Core` and `Infrastructure` DI graph and read the same `DefaultConnection` connection string. The Worker exists so unattended re-scraping does not compete with interactive traffic. The Blazor host also runs its own hosted `ScrapeWorkerService` for on-demand scrape jobs started from the UI, while `TaxRateCollector.Worker` runs the unattended cadence (`MonthlySchedulerService` + `ScrapeJobWorker`) as a standalone process or service.

### Stack

| Layer | Technology |
|---|---|
| Target framework | `net10.0` (all five projects) |
| UI | ASP.NET Core 10, Blazor Server (`InteractiveServer` render mode) |
| ORM | EF Core 10 (`Microsoft.EntityFrameworkCore.SqlServer`) |
| Database | SQL Server LocalDB (dev) or Azure SQL (prod); no SQLite path |
| PDF extraction | `UglyToad.PdfPig` 1.7.0-custom-5 |
| XLSX export | `ClosedXML` 0.105.0 |
| HTML scraping | `HtmlAgilityPack` 1.12.4 |
| CSV parsing | `CsvHelper` 33.1.0 |
| Logging | `Serilog.AspNetCore` and `Serilog.Extensions.Hosting`, console plus an EF-backed sink |
| Auth | ASP.NET Core Identity (`IdentityUser`, `IdentityRole`, cookie auth) |
| LLM extraction | `MindAttic.Legion` 25.0.0 (`LegionClient`), never a raw vendor SDK |
| Credentials | `MindAttic.Vault` 4.0.0 |
| Testing | NUnit 4.3.2, `Microsoft.EntityFrameworkCore.InMemory`, LocalDB integration, `Microsoft.Data.SqlClient` |

### Data pipeline

Rate data flows through three complementary paths:

1. Structured strategy scrapers (`IScrapeStrategy`), one class per known source format, dispatched by `ScrapeOrchestrator`: `CaliforniaCsvScraper` (CSV), `IllinoisTableScraper` (HTML table), `TexasExcelScraper` (XLSX).
2. Per-state bulk scrapers (`IStateBulkScraper`), one class per state's `.gov` page format and statute set: Wisconsin (general sales tax and alcohol), Illinois, Minnesota, Iowa, Indiana, Michigan, North Dakota, South Dakota, Ohio, Montana, Idaho and Oregon (alcohol and excise).
3. AI-assisted discovery (`RecursiveRateScraper` + `IRateLawExtractor`) walks a jurisdiction hierarchy (State, County, City, District), fetches raw source content and asks an LLM (via `MindAttic.Legion` in `ClaudeRateLawExtractor`, or `StubRateLawExtractor` when no key is configured) to extract a structured candidate rate law with evidence attached. Extracted rates are saved with `NeedsReview=true` and surfaced on the Review page, where an admin approves them (`IsCurrent=true`) or rejects them (removed).

Every path converges on the same two invariants:

- Evidence first ([TRC-LAW-1](docs/BIBLE.md#TRC-LAW-1)): a scraped or extracted rate is backed by a `SourceDocument` (raw content, SHA-256 `ContentHash`, `FetchedAt`, `SourceUrl`) before it counts as validated for export.
- Change detection ([TRC-LAW-4](docs/BIBLE.md#TRC-LAW-4)): `DiffEngine` compares each new scrape against the current `IsCurrent` rows and writes a `ChangeLogEntry` only for new, changed, removed or structurally altered rates. Unchanged rates produce no entry.

Two schedulers run the pipeline, in different processes:

- `TaxRateCollector.Blazor`'s hosted `ScrapeWorkerService` + `ScrapeJobCoordinator` executes scrape jobs started interactively from the Setup and ScrapeRuns pages without blocking request handling.
- `TaxRateCollector.Worker`'s `MonthlySchedulerService` + `ScrapeJobWorker` is a standalone background host for the unattended re-scrape cadence.

## Data import pipeline

The Setup page runs these steps in order:

| Step | What it does |
|---|---|
| 1. Validate URLs | HTTP HEAD checks all Census and SST source URLs |
| 2. Import SST taxonomy | Downloads the SSUTA Agreement PDF and refreshes `TaxCategory` descriptions from Appendix C via `SstTaxonomyImportService` and `SstDefinitionParser` |
| 3. Import Census jurisdictions | Census Gazetteer ZIPs become counties and cities via `CensusJurisdictionImportService` and `CensusGazetteerParser` (about 20 to 40 minutes on first run) |
| 4. Import ZIP crosswalks | Census ZCTA files link ZIPs to jurisdictions via `ZipImportService` and `ZipCrosswalkParser` (about 15 to 30 minutes on first run); city names are enriched via the USPS CityStateLookup API and `UspsValidated` is stored when a USPS key is configured |
| 5. Assign source URLs | Set `Jurisdiction.SourceUrl` for each state on the Jurisdictions page |
| 6. Run scrape | `ScrapeOrchestrator` dispatches every active jurisdiction with a source URL to the matching `IScrapeStrategy` or `IStateBulkScraper` |

Census gazetteer files are cached to `%APPDATA%\MindAttic\TaxRateCollector\cache\` after the first download. Per [TRC-US-B3](docs/USER_STORIES.md), full-corpus population (~3,144 counties and 10,000+ cities) is a live-download integration step that the default unit test run does not assert; run it and verify manually, or through the `Category=Integration` tests against LocalDB.

## Command line switches

`TaxRateCollector.Blazor/Program.cs` also branches on its arguments, so the same executable doubles as an operational CLI:

| Switch | Effect |
|---|---|
| `--populate` | Truncates billing, log, rate, jurisdiction and category tables, runs the full seed and import pipeline fresh, then exits without serving requests. |
| `--zero-rates` | Deletes all `SourceDocuments`, zeroes every `TaxRate.Rate` and deletes evidence files on disk: a demo or test reset that keeps the jurisdiction hierarchy. |
| `--scrape --state <codes> [--category <name>]` | Runs one or more registered `IStateBulkScraper` implementations outside the UI. `<codes>` is a state list such as `WI IL MN`; `--state all` runs every registered scraper. |

## Pages

Blazor Server pages in `TaxRateCollector.Blazor/Components/Pages/`:

| Page | Purpose |
|---|---|
| `Jurisdictions.razor` | Lazy-loading Country, State, County, City tree; inline rate editing; evidence panel with drag-and-drop upload; CSV, XLSX, SQL and HTML exports |
| `Rates.razor` | Master rate table view with CSV export |
| `TaxCalc.razor` | Point-of-sale combined-rate calculator (ZIP to tiers to total) |
| `Setup.razor` | Data import pipeline (URL validation, SST, Census and ZIP import, source URL assignment, scrape trigger) and `.bacpac` export |
| `ZipImport.razor` | ZIP crosswalk import UI |
| `Discovery.razor` | Drives `IDiscoveryService` and `RecursiveRateScraper` AI-assisted rate discovery |
| `Review.razor` | Approve or reject AI-extracted (`NeedsReview=true`) rates |
| `ScrapeRuns.razor` | `ScrapeRun` history: status, counts, pause and resume |
| `ChangeLog.razor` | `ChangeLogEntry` history from `DiffEngine` |
| `Logs.razor` | Application log viewer (Serilog EF sink) |
| `Glossary.razor` | Domain glossary |
| `Settings.razor` | Theme and font, USPS key, scraper options, backed by `SettingsService` |
| `Subscribe.razor` | PayPal subscription checkout |
| `Account.razor`, `Login.razor`, `Register.razor` | Identity auth flows |
| `TermsViolation.razor`, `Error.razor`, `NotFound.razor` | Error and status pages |

## Roles and access tiers

Two different mechanisms gate access; do not conflate them:

- ASP.NET Core Identity roles: only `Administrator` and `Approver` exist as real Identity roles (created in `Program.cs` on startup). They gate write actions such as rate edits, evidence, the setup pipeline, settings and scrape approval.
- Subscriber tier, a domain concept rather than an Identity role: `Subscriber`, `SubscribedState` and `SubscribedCategory` gate read access by state and category. A subscriber needs both a `SubscribedState` and a `SubscribedCategory` row for a state and category combination to see its rates. The seeded pricing is $0.01 per state per month plus $0.01 per category, checked out via PayPal (`IPayPalService`).
- "View as" preview (`ViewAsService`, admin only) renders the UI as `Subscriber` or `Guest` without losing the admin session, for demoing the paid and unpaid experience. `ViewAsRole.Guest` is purely a UI impersonation mode with rates redacted, and `ViewAsService.DemoSubscribedStateCodes` fakes a west and east coast subscription for the preview.

## Database

### Connection

Both `TaxRateCollector.Blazor` and `TaxRateCollector.Worker` read `ConnectionStrings:DefaultConnection` from configuration. `appsettings.Development.json` in each project ships:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=TaxRateCollector;Trusted_Connection=True;MultipleActiveResultSets=True;"
}
```

Production resolves the same key from environment variables or Azure Key Vault with no code changes (`UseSqlServer` throughout).

### Migrations

EF Core tooling targets `TaxRateCollector.Infrastructure`, with `TaxRateCollector.Blazor` as the startup project:

```powershell
dotnet ef database update `
    --project TaxRateCollector.Infrastructure `
    --startup-project TaxRateCollector.Blazor

dotnet ef migrations add <Name> `
    --project TaxRateCollector.Infrastructure `
    --startup-project TaxRateCollector.Blazor
```

The on-disk migration history in `TaxRateCollector.Infrastructure/Migrations/` runs from `20260418044705_InitialCreate` through `20260529033323_FixBillingTaxRatePrecision` (16 migrations). Use `dotnet ef migrations list` for the authoritative history.

### Bacpac backup

`TaxRateCollector.bacpac` at the repo root is a SQL Server data-tier application package: a portable snapshot of schema and data. Restore it with `sqlpackage` (ships with SQL Server, SSMS or the `Microsoft.SqlPackage` dotnet tool):

```powershell
sqlpackage /Action:Import `
    /SourceFile:TaxRateCollector.bacpac `
    /TargetServerName:"(localdb)\MSSQLLocalDB" `
    /TargetDatabaseName:TaxRateCollector
```

This is the fastest way to a fully seeded local database without the Setup pipeline's 20 to 40 minute Census and ZIP import. The file is gitignored (`/TaxRateCollector.bacpac`): it exists in a working copy that made one, not in a fresh clone, and is never committed.

## Configuration

Settings live in `%APPDATA%\MindAttic\TaxRateCollector\settings.json`, read and written by `SettingsService`.

Key settings: `theme`, `font`, `font_size`, `default_update_frequency_days` (90), `evidence_auto_fetch`, `wayback_machine_fallback` (the setting exists; the Wayback capture path is not built yet, see `TRC-US-C3`), all Census and SST source URLs, and `AutoApprove` (whether scraped rates skip the `NeedsReview` gate).

Evidence files land in `%APPDATA%\MindAttic\TaxRateCollector\evidence\` (`EvidenceFileStore`) and are served back to the browser via `GET /evidence/{filename}`, which is path-traversal guarded and requires authorization.

Credentials (the Anthropic key for LLM extraction, the USPS key, PayPal) resolve through the shared MindAttic credential store (`MindAttic.Vault`) or `IConfiguration`, never hard-coded ([TRC-LAW-5](docs/BIBLE.md#TRC-LAW-5)).

## Extending the scrapers

```csharp
public interface IScrapeStrategy
{
    string StrategyKey { get; }
    bool CanHandle(Jurisdiction jurisdiction);
    Task<IReadOnlyList<RawScrapeResult>> ScrapeAsync(Jurisdiction jurisdiction, CancellationToken ct = default);
}
```

To add a state with a single-format source: implement `IScrapeStrategy`, register it in `TaxRateCollector.Blazor/Program.cs` (`AddScoped<IScrapeStrategy, ...>()`) and set `Jurisdiction.SourceUrl` for that state.

For a full-state bulk import (general sales tax plus excise), implement `IStateBulkScraper` instead. The twelve `*AlcoholScraper` classes and `WisconsinSalesTaxScraper` in `TaxRateCollector.Infrastructure/Scrapers/` show the pattern; register it the same way (`AddScoped<IStateBulkScraper, ...>()`).

## Project layout

```text
TaxRateCollector/
├── TaxRateCollector.Core/                  Domain layer — no EF, no I/O
│   ├── Entities/        Jurisdiction, TaxRate, TaxCategory, SourceDocument, ExciseTaxRate,
│   │                     StateTaxProfile, ScrapeRun, ZipCodeRecord, ZipCodeDistrict,
│   │                     ChangeLogEntry, Subscriber/SubscribedState/SubscribedCategory,
│   │                     BillingRecord, PricingConfig, PayPalConfig, LogEntry, DiscoveryResult,
│   │                     JurisdictionData (ICanonEntity)
│   ├── Enums/            JurisdictionType, RateBasis, TaxType, CategoryTaxability, ChangeType,
│   │                     SourceType, SourceConfidence, ScrapeStatus, ProductCategory,
│   │                     LocalTaxAuthorityType, SourcingRule, SaleContext, RemittancePoint,
│   │                     RateAdjustmentFrequency, BillingStatus
│   ├── Interfaces/       IScrapeOrchestrator, IScrapeStrategy, IStateBulkScraper, IDiffEngine,
│   │                     ITaxCalculator, IRecursiveRateScraper (+ IRateLawExtractor),
│   │                     IEvidenceFileStore, IPayPalService, IDiscoveryService,
│   │                     IWebDirectoryScanner, ICensusJurisdictionImportService,
│   │                     IZipImportService, ISstTaxonomyImportService, ICanonEntity
│   ├── Constants/        TaxRateConstants
│   └── Options/          AnthropicOptions
├── TaxRateCollector.Infrastructure/
│   ├── Data/AppDbContext.cs                EF Core context — all tables
│   ├── Migrations/                         20260418044705_InitialCreate … 20260529033323_FixBillingTaxRatePrecision
│   ├── Seeding/          JurisdictionSeeder, TaxCategorySeeder, StateTaxProfileSeeder,
│   │                     SstTaxonomyData, DemoSubscriberSeeder
│   ├── Scrapers/         ScrapeOrchestrator, ScraperHttpHelper, Sanitizer, 12 per-state
│   │                     AlcoholScraper classes (WI/IL/MN/IA/IN/MI/ND/SD/OH/MT/ID/OR),
│   │                     WisconsinSalesTaxScraper
│   │   └── Strategies/   CaliforniaCsvScraper, IllinoisTableScraper, TexasExcelScraper
│   └── Services/         TaxCalculator, DiffEngine, RecursiveRateScraper,
│                         ClaudeRateLawExtractor / StubRateLawExtractor, EvidenceFileStore,
│                         CensusJurisdictionImportService, ZipImportService,
│                         SstTaxonomyImportService, CensusGazetteerParser, ZipCrosswalkParser,
│                         SstDefinitionParser, ScrapeSchedulerService, ScrapeWorkerService,
│                         ScrapeJobCoordinator, SettingsService, AlertService, PayPalService,
│                         DiscoveryService, WebDirectoryScanner
├── TaxRateCollector.Blazor/
│   ├── Program.cs                          DI composition root; migrate+seed on startup;
│   │                                       --populate / --zero-rates / --scrape CLI modes
│   └── Components/Pages/  Jurisdictions, Rates, TaxCalc, Setup, ZipImport, Discovery, Review,
│                           ScrapeRuns, ChangeLog, Logs, Glossary, Settings, Subscribe, Account,
│                           Login, Register, TermsViolation, Error, NotFound
├── TaxRateCollector.Worker/                 MonthlySchedulerService, ScrapeJobWorker, Worker.cs
├── TaxRateCollector.UnitTests/              AdminTools, DataCollectionTests, DataSourceTests,
│                                            EvidenceTests, ExportTests, JurisdictionTests,
│                                            LoggingTests, PayPalTests, ScraperTests, ServiceTests,
│                                            SetupTests, StartupTests, SubscriptionTests,
│                                            TaxCalcTests, Helpers/
├── TaxRateCollector.Frontend/               Effectively empty — see Limitations
├── TaxRateCollector.slnx                    Solution (does NOT include .Frontend)
├── docs/                                    Codex canon (BIBLE / USER_STORIES / AMENDMENTS / rfc)
├── index.htm, package.json                  Not part of the app (see Limitations)
└── tools/                                   codex.ps1 (Codex digest/doctor), build-readme.ps1
```

## Building and testing

```powershell
# Build everything (TreatWarningsAsErrors is not set globally; check individual .csproj files)
dotnet build TaxRateCollector.slnx

# Run the Blazor host
dotnet run --project TaxRateCollector.Blazor

# Run the background Worker
dotnet run --project TaxRateCollector.Worker

# Unit tests (no SQL required: EF Core InMemory and pure logic)
dotnet test TaxRateCollector.UnitTests --filter "Category!=Integration"

# Integration tests (requires LocalDB with migrations applied)
dotnet test TaxRateCollector.UnitTests --filter Category=Integration
```

Last verified state, recorded on 2026-10-03 in [docs/BIBLE.md](docs/BIBLE.md#TRC-§6) (the authoritative, dated snapshot): build clean, 748 of 748 unit tests passing (`Category!=Integration`).

## Deployment

```powershell
az group create --name rg-taxratecollector --location eastus
az appservice plan create --name asp-taxratecollector `
    --resource-group rg-taxratecollector --sku B1 --is-linux
az webapp create --name taxratecollector `
    --resource-group rg-taxratecollector `
    --plan asp-taxratecollector --runtime "DOTNETCORE:10.0"
```

Store the SQL connection string in Azure Key Vault and reference it via `ConnectionStrings:DefaultConnection`. The app uses `UseSqlServer` throughout, so no code changes are needed to run against Azure SQL instead of LocalDB.

## Limitations

- There is no public rate API yet: a `GET /api/rates` REST endpoint, webhooks on rate change and a GraphQL endpoint are backlog only.
- Full-corpus jurisdiction population is not proven by the default test run (see [Data import pipeline](#data-import-pipeline)).
- `TaxRateCollector.Frontend/` is effectively empty: it holds only a stale `bin/Debug/net10.0/TaxRateCollector.Frontend.exe` build artifact, no source, and it is not referenced by `TaxRateCollector.slnx`.
- `TODO.md` is a working backlog, not test-verified status. Where it disagrees with [the user stories](docs/USER_STORIES.md) (which cite tests), the user stories are correct.
- `package.json` at the repo root declares `build` and `deploy` scripts that point at a `scripts/cli/` directory that does not exist, so `npm run build` and `npm run deploy` do not work. The root `index.htm` is not part of the application and nothing serves it. The project page is this README on GitHub.

## Roadmap

The largest remaining items, in the project's stated priority order (full backlog in [TODO.md](TODO.md) and [the user stories](docs/USER_STORIES.md)). All are planned, not built:

1. Data completeness: populate all ~3,144 US counties and 10,000+ cities by running the existing importer (Setup step 3) and verifying the result.
2. USPS batch validation: validate every `Jurisdiction` row against the USPS Address Validator API as a background job, not only incidentally through ZIP import.
3. Evidence capture: capture a `.gov` page as a PDF or HTML snapshot with a SHA-256 hash, add a Wayback Machine fallback for dead URLs, and cross-check extracted rate values with PDF OCR.
4. Shared SST bulk scraper: one class covering all 24 Streamlined Sales Tax member states instead of per-state classes, per [docs/rfc/0001-sst-bulk-scraper.md](docs/rfc/0001-sst-bulk-scraper.md).
5. Public integration surface: the REST endpoint, webhooks and GraphQL endpoint listed above.
6. Export completeness: include excise tax rates in the Master Table export, which currently covers general sales tax only.

## Documentation

This README covers building, running and operating the app. For architecture, invariants and verified state, the canonical source is the Codex canon in `docs/`:

- [docs/BIBLE.md](docs/BIBLE.md): what the system is and is not, the laws (`TRC-LAW-n`), verified state and the active frontier. Read this first for how to think about the system.
- [docs/AMENDMENTS.md](docs/AMENDMENTS.md): pending decisions not yet folded into the bible (normally empty).
- [User stories](docs/USER_STORIES.md): test-cited feature status and the priority backlog.
- [docs/rfc](docs/rfc/): design notes not yet folded into canon.
- [docs/BIBLE.digest.md](docs/BIBLE.digest.md): generated by `tools/codex.ps1 digest`; never hand-edit.
- [AGENTS.md](AGENTS.md): instructions for AI agents working in this repo.

## License

This repository has no license file. All rights reserved (MindAttic proprietary).

Part of [MindAttic](https://mindattic.com) — see more projects at [github.com/mindattic](https://github.com/mindattic). Related: [MindAttic.Legion](https://github.com/mindattic/MindAttic.Legion) (provider-agnostic LLM routing), [MindAttic.Vault](https://github.com/mindattic/MindAttic.Vault) (credential store).
