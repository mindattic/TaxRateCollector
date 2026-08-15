# TaxRateCollector

**Provably accurate US sales tax rates — for billing systems that can't afford to be wrong.**

Most tax-rate APIs hand you a number. TaxRateCollector hands you the number *and the official
government PDF, CSV, or API response it came from* — SHA-256 hashed, timestamped, and stored
alongside every rate so an audit team can trace every cent back to the statute that authorized it.

- **Evidence on every row.** Each `TaxRate` is bound to a `SourceDocument` holding the raw `.gov`
  artifact, its SHA-256 content hash, and the `FetchedAt` timestamp.
- **All US jurisdictions.** Country → State → County → City hierarchy seeded from US Census Bureau
  gazetteers, with ZIP-to-jurisdiction lookup over ~33,000 ZCTAs.
- **SSUTA-aligned product taxonomy.** ~200 SST taxonomy nodes (Streamlined Sales & Use Tax
  Agreement Appendix C), with documented per-state overrides for non-member states.
- **Excise / sin taxes modeled properly.** Per-unit, per-volume, per-proof-gallon, per-weight, and
  percentage-of-wholesale bases; brackets; per-unit caps; compound tax-on-tax; ABV gating;
  origin/destination sourcing — not just a flat percentage.
- **Continuous re-scraping with change detection.** A background scheduler re-checks each
  jurisdiction's source on a configurable cadence; `DiffEngine` writes a `ChangeLogEntry` the
  moment a rate moves.
- **AI-assisted discovery, human-approved.** `RecursiveRateScraper` walks a jurisdiction hierarchy
  and uses an LLM (routed through `MindAttic.Legion`, never a hard-coded vendor SDK) to extract
  candidate rate laws with evidence; every AI-extracted rate lands `NeedsReview=true` until an
  admin approves or rejects it.
- **Plugs into a billing stack.** Blazor Server UI plus CSV / XLSX / SQL / HTML exports.

This README covers day-to-day build/run/test mechanics. For architecture, invariants, and verified
state, the canonical source of truth is **[docs/BIBLE.md](docs/BIBLE.md)** — read that first for how
to *think about* the system; this file is how to *operate* it. See also
**[docs/AMENDMENTS.md](docs/AMENDMENTS.md)** (things that changed since the bible was written),
**[docs/USER_STORIES.md](docs/USER_STORIES.md)** (test-cited feature status), and
**[docs/rfc/](docs/rfc/)** (design notes not yet folded into canon).

---

## What it is / is not

| It IS | It is NOT |
|---|---|
| A Blazor Server app + background Worker that maintain an evidence-backed master table of US sales & excise tax rates | A live public tax-calc REST/GraphQL API — there's no such endpoint yet ([TRC-US-G1](docs/USER_STORIES.md), backlog) |
| Backed by **SQL Server** (LocalDB dev, Azure SQL prod) via EF Core 10 | A SQLite app — `TODO.md` has stale SQLite-era prose; superseded by [TRC-A1](docs/AMENDMENTS.md#TRC-A1) / [TRC-LAW-8](docs/BIBLE.md#TRC-LAW-8) |
| A soft-delete system — rates retire (`IsCurrent=false`), jurisdictions deactivate (`IsActive=false`) | A hard-delete system ([TRC-LAW-2](docs/BIBLE.md#TRC-LAW-2)) |
| Provider-agnostic for LLM calls, via `MindAttic.Legion` | Vendor-locked to any one LLM SDK ([TRC-LAW-3](docs/BIBLE.md#TRC-LAW-3)) |

---

## Architecture

```
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

The Blazor host and the Worker are **two front doors over one engine**: both register the same
`Core` + `Infrastructure` DI graph and read the same `DefaultConnection` connection string. The
Worker exists so unattended re-scraping doesn't compete with interactive traffic — the Blazor host
*also* runs its own hosted `ScrapeWorkerService` for on-demand scrape jobs kicked off from the UI,
while `TaxRateCollector.Worker` runs the unattended cadence (`MonthlySchedulerService` +
`ScrapeJobWorker`) as a standalone process/service.

**Stack:**

| Layer | Technology |
| --- | --- |
| Target framework | `net10.0` (all five projects) |
| UI | ASP.NET Core 10, Blazor Server (`InteractiveServer` render mode) |
| ORM | EF Core 10 (`Microsoft.EntityFrameworkCore.SqlServer` 10.0.5) |
| Database | SQL Server LocalDB (dev) / Azure SQL (prod) — no SQLite path |
| PDF extraction | `UglyToad.PdfPig` 1.7.0-custom-5 |
| XLSX export | `ClosedXML` 0.105.0 |
| HTML scraping | `HtmlAgilityPack` 1.12.4 |
| CSV parsing | `CsvHelper` 33.1.0 |
| Logging | `Serilog.AspNetCore` / `Serilog.Extensions.Hosting`, console + EF-backed sink |
| Auth | ASP.NET Core Identity (`IdentityUser`/`IdentityRole`, cookie auth) |
| LLM extraction | `MindAttic.Legion` 22.0.0 (`LegionClient`) — never a raw vendor SDK |
| Credentials | `MindAttic.Vault` 1.0.0 (`AddMindAtticVaultFiles` / `AddMindAtticVault`) |
| Testing | NUnit 4.3.2, `Microsoft.EntityFrameworkCore.InMemory`, LocalDB integration, `Microsoft.Data.SqlClient` |

---

## Prerequisites

- .NET 10 SDK
- SQL Server LocalDB (`sqllocaldb create MSSQLLocalDB`) for local dev
- An Anthropic key resolvable via the shared MindAttic credential store (`MindAttic.Vault`) for AI
  rate-law extraction — optional; without one, `StubRateLawExtractor` is used and nothing throws

---

## Project structure

```
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
│   │                     "AlcoholScraper" classes (WI/IL/MN/IA/IN/MI/ND/SD/OH/MT/ID/OR),
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
│   │                                       `--populate` / `--zero-rates` / `--scrape` CLI modes
│   └── Components/Pages/  Jurisdictions, Rates, TaxCalc, Setup, ZipImport, Discovery, Review,
│                           ScrapeRuns, ChangeLog, Logs, Glossary, Settings, Subscribe, Account,
│                           Login, Register, TermsViolation, Error, NotFound
├── TaxRateCollector.Worker/                 MonthlySchedulerService, ScrapeJobWorker, Worker.cs
├── TaxRateCollector.UnitTests/
│   └── AdminTools, DataCollectionTests, DataSourceTests, EvidenceTests, ExportTests,
│       JurisdictionTests, LoggingTests, PayPalTests, ScraperTests, ServiceTests, SetupTests,
│       StartupTests, SubscriptionTests, TaxCalcTests, Helpers/
├── TaxRateCollector.Frontend/                Effectively empty — see "Known quirks" below
├── TaxRateCollector.bacpac                  SQL Server data-tier backup (see "Database" below)
├── TaxRateCollector.slnx                    Solution — does NOT include .Frontend
├── docs/                                    Codex canon (BIBLE / AMENDMENTS / USER_STORIES / rfc)
├── index.htm                                Generated mindattic.com landing page (do not hand-edit)
└── tools/                                   codex.ps1 (Codex digest/doctor), build-readme.ps1
```

---

## Getting started

```powershell
# Restore + run (migrations + seeders run automatically on first launch)
dotnet restore
dotnet run --project TaxRateCollector.Blazor
# -> https://localhost:5001 (see TaxRateCollector.Blazor/Properties/launchSettings.json for the exact port)

# Log in as dev admin (set before launch; Program.cs reads these via IConfiguration):
# $env:DEV_ADMIN_EMAIL = "admin@example.com"
# $env:DEV_ADMIN_PASSWORD = "..."
# In Development only, GET /dev-login signs that user in directly (bypasses the login form).

# Navigate to /setup to run the data import pipeline
```

On first launch `Program.cs`:
1. Applies EF Core migrations (`db.Database.MigrateAsync()`).
2. Seeds `TaxCategories` (~200 SST nodes) and `Jurisdictions` (Country + 51 States).
3. Ensures Census counties/cities are present (imports automatically if under 3,000 counties or
   5,000 cities — this can take 20–40 minutes on a cold cache).
4. Ensures ZIP crosswalks are present (imports automatically if under 30,000 ZIP rows — 15–30
   minutes cold).
5. Seeds `StateTaxProfiles` (51 profiles), `PricingConfig`, `PayPalConfig`.
6. Ensures the `Administrator` and `Approver` Identity roles exist; seeds the dev admin user (if
   `DEV_ADMIN_EMAIL`/`DEV_ADMIN_PASSWORD` are set) and demo subscribers (`DemoSubscriberSeeder`).

### Blazor host CLI switches

`Program.cs` also branches on `args`, so the same executable doubles as an operational CLI:

| Switch | Effect |
| --- | --- |
| `--populate` | Truncates billing/log/rate/jurisdiction/category tables, then runs the full seed+import pipeline fresh, and exits (does not start serving requests). |
| `--zero-rates` | Deletes all `SourceDocuments`, zeroes every `TaxRate.Rate`, and deletes evidence files on disk — a reset for demo/test data without touching the jurisdiction hierarchy. |
| `--scrape --state <WI\|IL\|MN\|...\|all> [--category <name>]` | Runs one or more registered `IStateBulkScraper` implementations outside the UI; `--state all` runs every registered scraper. |

Census gazetteer files are cached to `%APPDATA%\MindAttic\TaxRateCollector\cache\` after first
download.

---

## Data import pipeline (Setup page)

| Step | What it does |
| --- | --- |
| 1. Validate URLs | HTTP HEAD checks all Census + SST source URLs |
| 2. Import SST Taxonomy | Downloads the SSUTA Agreement PDF, refreshes `TaxCategory` descriptions from Appendix C via `SstTaxonomyImportService` / `SstDefinitionParser` |
| 3. Import Census Jurisdictions | Census Gazetteer ZIPs → counties + cities via `CensusJurisdictionImportService` / `CensusGazetteerParser` (~20–40 min first run) |
| 4. Import ZIP Crosswalks | Census ZCTA TXTs → ZIPs linked to jurisdictions via `ZipImportService` / `ZipCrosswalkParser` (~15–30 min first run; enriches city names via the USPS CityStateLookup API and persists `UspsValidated` when a USPS key is configured) |
| 5. Assign Source URLs | Set `Jurisdiction.SourceUrl` for each state via the Jurisdictions page |
| 6. Run Scrape | `ScrapeOrchestrator` dispatches every active jurisdiction with a source URL to the matching `IScrapeStrategy` / `IStateBulkScraper` |

Per [docs/USER_STORIES.md](docs/USER_STORIES.md) (`TRC-US-B3`), full-corpus population (~3,144
counties, 10,000+ cities) is a live-download integration step, not asserted by the default unit
test run — run it and verify manually, or via `Category=Integration` tests against LocalDB.

---

## Data pipeline internals (the "Worker" story)

TaxRateCollector's rate data flows through two complementary paths:

**1. Structured strategy scrapers (`IScrapeStrategy`).** One class per known source *format*,
dispatched by `ScrapeOrchestrator`:
- `CaliforniaCsvScraper` — CSV
- `IllinoisTableScraper` — HTML table
- `TexasExcelScraper` — XLSX

**2. Per-state bulk scrapers (`IStateBulkScraper`).** One class per state's specific `.gov` page
format/statute set — currently Wisconsin (general sales tax + alcohol), Illinois, Minnesota, Iowa,
Indiana, Michigan, North Dakota, South Dakota, Ohio, Montana, Idaho, and Oregon (alcohol/excise).
A shared `SstSalesTaxScraper` for the 24 Streamlined Sales Tax member states is designed but not
yet built — see [docs/rfc/0001-sst-bulk-scraper.md](docs/rfc/0001-sst-bulk-scraper.md) and
`TRC-US-E2`.

**3. AI-assisted discovery (`RecursiveRateScraper` + `IRateLawExtractor`).** Walks a jurisdiction
hierarchy (State → County → City → District), fetches raw source content, and asks an LLM (via
`MindAttic.Legion`'s `ClaudeRateLawExtractor`, or `StubRateLawExtractor` when no key is
configured) to extract a structured candidate rate law with evidence attached. Extracted rates are
persisted with `NeedsReview=true` and surfaced on the **Review** page for an admin to approve
(→ `IsCurrent=true`) or reject (→ removed).

**Every path converges on the same two invariants:**
- **Evidence first** ([TRC-LAW-1](docs/BIBLE.md#TRC-LAW-1)): a scraped/extracted rate is backed by
  a `SourceDocument` — raw content + SHA-256 `ContentHash` + `FetchedAt` + `SourceUrl` — before it
  counts as validated for export.
- **Change detection** ([TRC-LAW-4](docs/BIBLE.md#TRC-LAW-4)): `DiffEngine` compares each new
  scrape against the current `IsCurrent` rows and writes a `ChangeLogEntry` only for
  new/changed/removed/structurally-altered rates (visible on the **Change Log** page); unchanged
  rates produce no entry.

**Two schedulers run the pipeline unattended, in different processes:**
- `TaxRateCollector.Blazor`'s own hosted `ScrapeWorkerService` + `ScrapeJobCoordinator` — executes
  scrape jobs kicked off interactively from the UI (Setup / ScrapeRuns pages) without blocking
  request handling.
- `TaxRateCollector.Worker`'s `MonthlySchedulerService` + `ScrapeJobWorker` — a standalone
  background host for the unattended re-scrape cadence, so scheduled runs don't compete with
  interactive Blazor traffic.

---

## Front-ends

### Blazor Server pages (`TaxRateCollector.Blazor/Components/Pages/`)

| Page | Purpose |
| --- | --- |
| `Jurisdictions.razor` | Lazy-loading Country→State→County→City tree; inline rate editing; evidence panel (drag-and-drop upload) |
| `Rates.razor` | Master rate table view |
| `TaxCalc.razor` | Point-of-sale combined-rate calculator (ZIP → tiers → total) |
| `Setup.razor` | Data import pipeline (URL validation, SST/Census/ZIP import, source URL assignment, scrape trigger) |
| `ZipImport.razor` | ZIP crosswalk import UI |
| `Discovery.razor` | Drives `IDiscoveryService` / `RecursiveRateScraper` AI-assisted rate discovery |
| `Review.razor` | Approve/reject AI-extracted (`NeedsReview=true`) rates |
| `ScrapeRuns.razor` | `ScrapeRun` history — status, counts, pause/resume |
| `ChangeLog.razor` | `ChangeLogEntry` history from `DiffEngine` |
| `Logs.razor` | Application log viewer (Serilog EF sink) |
| `Glossary.razor` | Domain glossary |
| `Settings.razor` | Theme/font, USPS key, scraper options — backed by `SettingsService` |
| `Subscribe.razor` | PayPal subscription checkout |
| `Account.razor`, `Login.razor`, `Register.razor` | Identity auth flows |
| `TermsViolation.razor`, `Error.razor`, `NotFound.razor` | Error/status pages |

### Roles and access tiers

Two different mechanisms gate access — don't conflate them:

- **ASP.NET Core Identity roles** — only `Administrator` and `Approver` exist as real Identity
  roles (created in `Program.cs` on startup). These gate write actions (rate edits, evidence,
  setup pipeline, settings, scrape approval).
- **Subscriber tier (domain concept, not an Identity role)** — `Subscriber` /
  `SubscribedState` / `SubscribedCategory` entities gate *read* access by state + category; a
  subscriber needs both a `SubscribedState` and a `SubscribedCategory` row for a given
  state+category combination to see its rates. Subscription is ~$0.01/state/month + $0.01/category,
  checked out via PayPal (`IPayPalService`).
- **"View as" preview** (`ViewAsService`, admin-only) — lets an admin render the UI as
  `Subscriber` or `Guest` without actually losing their session, for demoing the paid/unpaid
  experience. `ViewAsRole.Guest` has no persisted concept of its own; it's purely a UI
  impersonation mode with rates redacted, and `ViewAsService.DemoSubscribedStateCodes` fakes a
  west+east-coast subscription for the preview.

### index.htm / landing page

`index.htm` at the repo root is a generated mindattic.com landing page (README → HTML). It is
**not** part of the Blazor/Worker application and is out of scope for this README's build/run
instructions — do not hand-edit it or the `README.htm` this repo's own `tools/build-readme.ps1`
produces; they're generated artifacts of separate tooling.

---

## Database

### Connection

Both `TaxRateCollector.Blazor` and `TaxRateCollector.Worker` read `ConnectionStrings:DefaultConnection`
from configuration. `appsettings.Development.json` in each project ships:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=TaxRateCollector;Trusted_Connection=True;MultipleActiveResultSets=True;"
}
```

Production resolves the same key from environment variables / Azure Key Vault — no code changes
needed (`UseSqlServer` throughout).

### Migrations

EF Core tooling targets `TaxRateCollector.Infrastructure`, with `TaxRateCollector.Blazor` as the
startup project:

```powershell
dotnet ef database update `
    --project TaxRateCollector.Infrastructure `
    --startup-project TaxRateCollector.Blazor

dotnet ef migrations add <Name> `
    --project TaxRateCollector.Infrastructure `
    --startup-project TaxRateCollector.Blazor
```

The real, on-disk migration history (see [TRC-A2](docs/AMENDMENTS.md#TRC-A2) — an older README
table of "friendly names" here did not match reality and has been removed) runs from
`20260418044705_InitialCreate` through `20260529033323_FixBillingTaxRatePrecision`
(`TaxRateCollector.Infrastructure/Migrations/`). Use `dotnet ef migrations list` for the live,
authoritative history rather than trusting any name in prose.

### `.bacpac` backup

`TaxRateCollector.bacpac` at the repo root is a SQL Server data-tier application package — a
portable snapshot of schema + data. Restore it with `sqlpackage` (ships with SQL Server /
SSMS / the `Microsoft.SqlPackage` dotnet tool):

```powershell
sqlpackage /Action:Import `
    /SourceFile:TaxRateCollector.bacpac `
    /TargetServerName:"(localdb)\MSSQLLocalDB" `
    /TargetDatabaseName:TaxRateCollector
```

This is the fastest way to get a fully-seeded local database without running the Setup pipeline's
20–40 minute Census/ZIP import from scratch. It is explicitly gitignored (`.gitignore` line
`/TaxRateCollector.bacpac`) — it lives on disk in this working copy but is not a tracked file;
treat it as a large local binary, not something to commit or diff.

---

## Settings

Settings file: `%APPDATA%\MindAttic\TaxRateCollector\settings.json` (read/written by
`SettingsService`).

Key settings: `theme`, `font`, `font_size`, `default_update_frequency_days` (90),
`evidence_auto_fetch`, `wayback_machine_fallback` (setting exists; the actual Wayback capture path
is not yet built — see `TRC-US-C3`), all Census and SST source URLs, `AutoApprove` (whether scraped
rates skip the `NeedsReview` gate).

Evidence files land in `%APPDATA%\MindAttic\TaxRateCollector\evidence\` (`EvidenceFileStore`),
served back to the browser via `GET /evidence/{filename}` (path-traversal guarded, requires
authorization).

Credentials (Anthropic key for LLM extraction, USPS key, PayPal) resolve through the shared
MindAttic credential store (`MindAttic.Vault`) or `IConfiguration` — never hard-coded
([TRC-LAW-5](docs/BIBLE.md#TRC-LAW-5)).

---

## Scraper framework

```csharp
public interface IScrapeStrategy
{
    string StrategyKey { get; }
    bool CanHandle(Jurisdiction jurisdiction);
    Task<IReadOnlyList<RawScrapeResult>> ScrapeAsync(Jurisdiction jurisdiction, CancellationToken ct = default);
}
```

Existing strategies: `IllinoisTableScraper` (HTML), `CaliforniaCsvScraper` (CSV),
`TexasExcelScraper` (XLSX). To add a new state: implement `IScrapeStrategy`, register it in
`TaxRateCollector.Blazor/Program.cs` (`AddScoped<IScrapeStrategy, ...>()`), and set
`Jurisdiction.SourceUrl` for that state.

For a full state (general sales tax + excise) bulk import, implement `IStateBulkScraper` instead —
see the twelve `*AlcoholScraper` classes and `WisconsinSalesTaxScraper` in
`TaxRateCollector.Infrastructure/Scrapers/` for the pattern, and register it the same way
(`AddScoped<IStateBulkScraper, ...>()`).

---

## Build / test

```powershell
# Build everything (TreatWarningsAsErrors is NOT globally set here — check individual .csproj files)
dotnet build TaxRateCollector.slnx

# Run the Blazor host
dotnet run --project TaxRateCollector.Blazor

# Run the background Worker
dotnet run --project TaxRateCollector.Worker

# Unit tests (no SQL required — EF Core InMemory / pure logic)
dotnet test TaxRateCollector.UnitTests --filter "Category!=Integration"

# Integration tests (requires LocalDB with migrations applied)
dotnet test TaxRateCollector.UnitTests --filter Category=Integration
```

Test folders: `AdminTools`, `DataCollectionTests`, `DataSourceTests`, `EvidenceTests`,
`ExportTests`, `JurisdictionTests`, `LoggingTests`, `PayPalTests`, `ScraperTests`, `ServiceTests`,
`SetupTests`, `StartupTests`, `SubscriptionTests`, `TaxCalcTests`.

Last-verified state (see [docs/BIBLE.md §6](docs/BIBLE.md#TRC-§6) for the authoritative, dated
snapshot — this file will drift, that one is kept current): build clean, 736/744 unit tests
passing, with 8 known-failing tests around `EvidenceFileStore` type detection and `AlertService`
acknowledge-all — tracked as the top item in the [priority backlog](docs/USER_STORIES.md#priority-backlog).

---

## Deploying to Azure

```powershell
az group create --name rg-taxratecollector --location eastus
az appservice plan create --name asp-taxratecollector `
    --resource-group rg-taxratecollector --sku B1 --is-linux
az webapp create --name taxratecollector `
    --resource-group rg-taxratecollector `
    --plan asp-taxratecollector --runtime "DOTNETCORE:10.0"
```

Store the SQL connection string in Azure Key Vault and reference it via
`ConnectionStrings:DefaultConnection`. The app uses `UseSqlServer` throughout — no code changes
needed to run against Azure SQL instead of LocalDB.

---

## Known quirks worth knowing about

- **`TaxRateCollector.Frontend/` is effectively empty.** It contains only a stale
  `bin/Debug/net10.0/TaxRateCollector.Frontend.exe` build artifact — no source files — and it is
  **not** referenced by `TaxRateCollector.slnx`. Do not expect it to build or run anything; if you
  need it for something, it needs source added first.
- **`TODO.md` is stale in places.** It predates a lot of shipped work: ASP.NET Core Identity +
  roles, PayPal subscription checkout, and per-jurisdiction excise tax rates are all marked
  incomplete there but are implemented and covered by tests per
  [docs/USER_STORIES.md](docs/USER_STORIES.md) — trust the Codex canon (BIBLE / AMENDMENTS /
  USER_STORIES) over `TODO.md` prose when the two disagree, per
  [TRC-A1](docs/AMENDMENTS.md#TRC-A1).
- **`package.json` at the repo root** describes a "README → index.htm renderer" with `build` /
  `deploy` npm scripts, but the `scripts/cli/` directory those scripts point at was removed when
  landing-page deployment moved to the shared `MindAttic.Deploy` tooling — don't expect
  `npm run build`/`npm run deploy` to work as documented in `package.json` today.

## TODO highlights (see [TODO.md](TODO.md) for the full backlog)

The largest remaining items, in the project's own stated priority order:

1. **Data completeness** — expand the seeder from partial coverage to the full ~3,144 US counties
   and 5,000+ cities (the importer exists; running the Setup pipeline's Step 3 is what's pending).
2. **USPS batch validation** — validate every `Jurisdiction` row against the USPS Address
   Validator API directly as a background job, not only incidentally via ZIP import.
3. **Evidence capture** — a web scraper that captures a `.gov` page as PDF/HTML snapshot with
   SHA-256 hash, a Wayback Machine fallback for dead URLs, and PDF OCR cross-check of extracted
   rate values.
4. **Shared SST bulk scraper** — one class covering all 24 Streamlined Sales Tax member states
   instead of per-state classes, per [docs/rfc/0001-sst-bulk-scraper.md](docs/rfc/0001-sst-bulk-scraper.md).
5. **Public integration surface** — a `GET /api/rates` REST endpoint, webhooks on rate change, and
   a GraphQL endpoint are all still ⬜ (backlog only, no code yet).
6. **Export completeness** — include excise tax rates in the Master Table export (currently
   general sales tax only).

---

Internal tool — MindAttic proprietary.
