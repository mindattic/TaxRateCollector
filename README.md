# TaxRateCollector

**Provably accurate US sales tax rates — for billing systems that can't afford to be wrong.**

Most tax-rate APIs hand you a number. TaxRateCollector hands you the number *and the official government PDF, CSV, or API response it came from* — hashed, timestamped, and stored alongside every rate so your audit team can trace every cent back to the statute that authorized it.

- **Evidence on every row.** Each `TaxRate` is bound to a `SourceDocument` holding the raw `.gov` artifact, its SHA-256 content hash, and the `FetchedAt` timestamp.
- **All 14,000+ US jurisdictions.** Country → State → County → City hierarchy seeded from US Census Bureau gazetteers. ZIP-to-jurisdiction lookup covers ~33,000 ZCTAs.
- **SSUTA-aligned product taxonomy.** ~200 SST taxonomy nodes; documented overrides for non-member states.
- **Continuous re-scraping with change detection.** Background scheduler re-checks each jurisdiction's source on a configurable cadence; `DiffEngine` writes a `ChangeLogEntry` the moment a rate moves.
- **Plugs into your stack.** Blazor Server UI plus CSV / XLSX / SQL / HTML exports.

---

## Architecture

```
TaxRateCollector.Core           Domain entities, enums, interfaces
TaxRateCollector.Infrastructure EF Core, migrations, seeders, importers, scrapers, services
TaxRateCollector.Blazor         Blazor Server UI, pages, exports
TaxRateCollector.Worker         Background host (MonthlySchedulerService, ScrapeJobWorker)
TaxRateCollector.UnitTests      NUnit — hierarchy, seeder correctness, DB population
```

**Stack:**

| Layer | Technology |
| --- | --- |
| UI | ASP.NET Core 10, Blazor Server |
| ORM | EF Core 10 |
| Database | SQL Server LocalDB (dev) / Azure SQL (prod) |
| PDF extraction | UglyToad.PdfPig |
| XLSX export | ClosedXML 0.105 |
| HTML scraping | HtmlAgilityPack |
| CSV parsing | CsvHelper |
| Logging | Serilog.AspNetCore |
| Auth | ASP.NET Core Identity |
| Testing | NUnit 4, EF Core InMemory, LocalDB |

---

## Prerequisites

- .NET 10 SDK
- SQL Server LocalDB (`sqllocaldb create MSSQLLocalDB`)

---

## Project structure

```
TaxRateCollector/
├── TaxRateCollector.Core/
│   ├── Entities/           Jurisdiction, TaxRate, TaxCategory, SourceDocument, StateTaxProfile, ExciseTaxRate, ScrapeRun, ZipCodeRecord, ...billing, changelog, logs
│   ├── Enums/              JurisdictionType, CategoryTaxability, LocalTaxAuthorityType, ScrapeStatus, SourceType
│   └── Interfaces/         IScrapeOrchestrator, IScrapeStrategy, ISstTaxonomyImportService, ...
├── TaxRateCollector.Infrastructure/
│   ├── Data/AppDbContext.cs            All 25 tables
│   ├── Migrations/
│   ├── Seeding/                        JurisdictionSeeder, TaxCategorySeeder, StateTaxProfileSeeder, SstTaxonomyData
│   └── Services/                       CensusJurisdictionImportService, ZipImportService, SstTaxonomyImportService, ScrapeOrchestrator, ScrapeSchedulerService, SettingsService, DiffEngine, TaxCalculator, ...
├── TaxRateCollector.Blazor/
│   └── Components/Pages/              Jurisdictions, Setup, Settings, Glossary, Logs
├── TaxRateCollector.Worker/
└── TaxRateCollector.UnitTests/
    └── SetupTests/                     SstTaxonomyStructureTests, TaxCategorySeederTests, AppSettingsTests, DatabaseBackupTests, DatabasePopulationIntegrationTests
```

---

## Getting started

```powershell
# Restore + run (migrations + seeders run automatically on first launch)
dotnet restore
dotnet run --project TaxRateCollector.Blazor
# -> https://localhost:5001

# Log in as dev admin (set env vars before launch):
# $env:DEV_ADMIN_EMAIL = "admin@example.com"
# $env:DEV_ADMIN_PASSWORD = "..."

# Navigate to /setup to run the data import pipeline
```

On first launch `Program.cs` automatically applies migrations and seeds: `TaxCategories` (~200 SST nodes), `Jurisdictions` (Country + 51 States), `StateTaxProfiles` (51 profiles), `PricingConfig`, `PayPalConfig`, and the dev admin user.

---

## Data import pipeline (Setup page)

| Step | What it does |
| --- | --- |
| 1. Validate URLs | HTTP HEAD checks all Census + SST source URLs |
| 2. Import SST Taxonomy | Downloads SSUTA Agreement PDF, refreshes `TaxCategory` descriptions from Appendix C |
| 3. Import Census Jurisdictions | Census Gazetteer ZIPs → ~3,200 counties + ~30,000 cities (~20–40 min first run) |
| 4. Import ZIP Crosswalks | Census ZCTA TXTs → ~33,000 ZIPs linked to jurisdictions (~15–30 min first run) |
| 5. Assign Source URLs | Set `Jurisdiction.SourceUrl` for each state via the Jurisdictions page |
| 6. Run Scrape | `ScrapeOrchestrator` runs all jurisdictions with source URLs |

Census files are cached to `%APPDATA%\MindAttic\TaxRateCollector\cache\` after first download.

---

## ZIP lookup

```
Customer enters ZIP 90210
    -> ZipCodeRecord lookup -> State=CA + County=LA County + City=Beverly Hills
    -> Query TaxRates for those 3 JurisdictionIds
    -> Sum: 7.25% (CA) + 1.00% (LA County) + 0.00% (BH) = 8.25%
    -> Apply CategoryTaxability (Groceries in CA -> Exempt)
    -> Final: 0%
```

ZIPs carry no rates — they are a lookup index into the hierarchy.

---

## Evidence and provenance

Each `SourceDocument` stores the raw artifact (`RawContent`), a SHA-256 content hash, `FetchedAt` (ISO 8601 UTC), and `SourceUrl`. Re-hashing `RawContent` against `ContentHash` proves the document has not been altered. Admins can also drag-and-drop evidence files onto any jurisdiction row.

---

## Settings

Settings file: `%APPDATA%\MindAttic\TaxRateCollector\settings.json`

Key settings: `theme`, `font`, `font_size`, `default_update_frequency_days` (90), `evidence_auto_fetch`, `wayback_machine_fallback`, all Census and SST source URLs.

---

## Database migrations

EF Core tools target `TaxRateCollector.Infrastructure`:

```powershell
dotnet ef database update `
    --project TaxRateCollector.Infrastructure `
    --startup-project TaxRateCollector.Blazor

dotnet ef migrations add <Name> `
    --project TaxRateCollector.Infrastructure `
    --startup-project TaxRateCollector.Blazor
```

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

Existing strategies: `IllinoisTableScraper` (HTML), `CaliforniaCsvScraper` (CSV), `TexasExcelScraper` (XLSX). To add a new state: implement `IScrapeStrategy`, register in `Program.cs`, set `Jurisdiction.SourceUrl`.

---

## Tests

```powershell
# Unit tests (no SQL required)
dotnet test TaxRateCollector.UnitTests

# Integration tests (requires LocalDB with migrations applied)
dotnet test TaxRateCollector.UnitTests --filter Category=Integration
```

Test classes: `SstTaxonomyStructureTests`, `TaxCategorySeederTests`, `AppSettingsTests`, `DatabaseBackupTests`, `DatabasePopulationIntegrationTests`.

---

## Roles

- **Admin** — full read/write, setup pipeline, settings
- **Subscriber** — read-only, filtered by subscribed states
- **Guest** — rates redacted behind a lock blur

Subscription model: ~$0.01 per state per month. PayPal handles checkout.

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

Store the SQL connection string in Azure Key Vault and reference via `ConnectionStrings:DefaultConnection`. The app uses `UseSqlServer` — no code changes needed.

---

Internal tool — MindAttic proprietary.
