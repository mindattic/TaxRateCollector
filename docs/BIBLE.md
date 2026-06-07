---
codex: 1
project: TaxRateCollector
code: TRC
layer: bible
status: living
updated: 2026-06-07
---

# TaxRateCollector — Project Bible

> Single source of truth for what TaxRateCollector IS, is NOT, and the rules that keep it coherent.
> README.md says how to build/run; this says how to think about the system.

## 1. The one sentence {#TRC-§1}

TaxRateCollector is a Blazor Server application that maintains a provably accurate, evidence-backed master table of US sales and excise tax rates for every jurisdiction (Country → State → County → City), binding every rate to the hashed, timestamped government source document it came from.

## 2. The product promise {#TRC-§2}

- **Evidence on every row.** Each authoritative rate is bound to a [`SourceDocument`](TaxRateCollector.Core/Entities/SourceDocument.cs) holding the raw `.gov` artifact, its SHA-256 content hash, and a `FetchedAt` timestamp. A rate is independently verifiable without a live network call. See [Law 1](#TRC-LAW-1).
- **Every US jurisdiction.** A self-referential Country → State → County → City hierarchy ([`Jurisdiction`](TaxRateCollector.Core/Entities/Jurisdiction.cs)) seeded from US Census Bureau gazetteers, with ZIP→jurisdiction lookup over ~33,000 ZCTAs.
- **SSUTA-aligned product taxonomy.** Products are classified against the Streamlined Sales & Use Tax Agreement Appendix C taxonomy ([`TaxCategory`](TaxRateCollector.Core/Entities/TaxCategory.cs)), with documented per-state overrides for non-member states.
- **Continuous re-scraping with change detection.** A background host re-checks each jurisdiction's official source on a cadence; the [`DiffEngine`](TaxRateCollector.Infrastructure/Services/DiffEngine.cs) writes a [`ChangeLogEntry`](TaxRateCollector.Core/Entities/ChangeLogEntry.cs) the moment a rate moves. See [Law 4](#TRC-LAW-4).
- **Pay only for the states you bill in.** Subscribers pick states (and categories) at a per-unit price; PayPal handles checkout.
- **Plugs into a billing stack.** Blazor Server UI plus CSV / XLSX / SQL / HTML exports.

## 3. What it is NOT {#TRC-§3}

- **NOT a live tax-calc API for third parties.** There is currently no public REST/GraphQL endpoint — consumers use the UI and file exports. A REST/GraphQL endpoint is a backlog item ([TRC-US-G1](docs/USER_STORIES.md)), not a shipped feature.
- **NOT a "best guess" rate oracle.** A rate without attached, hash-verified evidence is not considered validated for export. "Our API said so" is explicitly rejected as a compliance answer. See [Law 1](#TRC-LAW-1).
- **NOT a SQLite app.** Despite some stale prose in TODO.md, the system runs on SQL Server (LocalDB in dev, SQL Server / Azure SQL in prod) via EF Core 10. There is no SQLite path.
- **NOT a hard-delete system.** Rate history is retired (`IsCurrent=false`), never destroyed; jurisdictions are deactivated (`IsActive=false`), not deleted. See [Law 2](#TRC-LAW-2) and [HOUSE-LAW-2].
- **NOT vendor-locked to one LLM.** AI rate extraction routes through MindAttic.Legion, not a hard-coded vendor SDK. See [Law 3](#TRC-LAW-3).
- **NOT a flat-percentage tax engine.** It models excise structures (per-unit, per-volume, per-proof-gallon, per-weight, percentage-of-wholesale), brackets, caps, compound tax-on-tax, ABV gating, and origin/destination sourcing. See [§4.2](#TRC-§4).

## 4. Architecture canon {#TRC-§4}

```
                  +------------------------------+
                  |  TaxRateCollector.Blazor     |  Blazor Server UI (InteractiveServer),
                  |  (front door: interactive)   |  pages, exports, DI composition root
                  +---------------+--------------+
                                  |
   +------------------------------+------------------------------+
   |                              |                              |
+--v---------------+   +----------v-----------+   +--------------v-------+
| TaxRateCollector |   | TaxRateCollector     |   | TaxRateCollector     |
| .Worker          |   | .Infrastructure      |   | .UnitTests           |
| (front door:     |   | EF Core 10, scrapers,|   | NUnit 4, InMemory +  |
|  background)     |   | seeders, services    |   | LocalDB integration  |
+--------+---------+   +----------+-----------+   +----------------------+
         |                        |
         |             +----------v-----------+
         +------------>| TaxRateCollector.Core|  Entities, Enums, Interfaces, Options
                       | (NOUNS + contracts)  |
                       +----------------------+
                                  |
                          +-------v--------+
                          |  SQL Server    |  LocalDB (dev) / Azure SQL (prod)
                          +----------------+
```

The Blazor host and the Worker are two **front doors over one engine** ([HOUSE-LAW-6](#TRC-§5)): both register the same `Core` + `Infrastructure` graph and read the same connection string. The Worker exists so unattended re-scraping does not compete with interactive traffic.

### 4.1 Projects
| Project | Responsibility |
|---|---|
| [`TaxRateCollector.Core`](TaxRateCollector.Core/) | Domain entities, enums, interfaces, options. No EF, no I/O. |
| [`TaxRateCollector.Infrastructure`](TaxRateCollector.Infrastructure/) | EF Core `AppDbContext`, migrations, seeders, importers, scrapers, services. |
| [`TaxRateCollector.Blazor`](TaxRateCollector.Blazor/) | Blazor Server UI, pages, exports, DI composition root, startup migrate→seed. |
| [`TaxRateCollector.Worker`](TaxRateCollector.Worker/) | Background host (`MonthlySchedulerService`, `ScrapeJobWorker`) for unattended re-scrapes. |
| [`TaxRateCollector.UnitTests`](TaxRateCollector.UnitTests/) | NUnit 4 — unit + LocalDB integration (`Category=Integration`). |

### 4.2 Domain model (NOUNS)
| Entity | Role |
|---|---|
| [`Jurisdiction`](TaxRateCollector.Core/Entities/Jurisdiction.cs) | Self-referential hierarchy node (`ParentId`); `JurisdictionType` Country/State/County/City; `FipsCode`, `StateCode`, `SourceUrl`, `IsActive`, `IsHomeRuleAdministered`. |
| [`TaxRate`](TaxRateCollector.Core/Entities/TaxRate.cs) | One rate law: `Rate` + `RateBasis`, `TaxType`, brackets (`Min/MaxTaxableAmount`), `FlatCapPerUnit`, `IsCompound`, `IsIncludedInPrice`, ABV gating, `IsCurrent`, `SourceConfidence`. |
| [`TaxCategory`](TaxRateCollector.Core/Entities/TaxCategory.cs) | SST product/service taxonomy node (Goods/Services tree). |
| [`SourceDocument`](TaxRateCollector.Core/Entities/SourceDocument.cs) | Evidence artifact: raw content, SHA-256 `ContentHash`, `SourceUrl`, `FetchedAt`, `SourceType`. |
| [`ExciseTaxRate`](TaxRateCollector.Core/Entities/ExciseTaxRate.cs) | Excise/specific taxes (alcohol, tobacco, cannabis, fuel, hotel). |
| [`StateTaxProfile`](TaxRateCollector.Core/Entities/StateTaxProfile.cs) | Per-state metadata: general rate, SST membership, `LocalTaxAuthorityType`, revenue-agency name + URL. **Authoritative home: [`StateTaxProfileSeeder`](TaxRateCollector.Infrastructure/Seeding/StateTaxProfileSeeder.cs).** |
| [`ScrapeRun`](TaxRateCollector.Core/Entities/ScrapeRun.cs) | Metadata for a scrape/import batch (status, counts, pause/resume). |
| [`ZipCodeRecord`](TaxRateCollector.Core/Entities/ZipCodeRecord.cs) | ZIP → (State, County, City) jurisdiction junction. |
| [`ChangeLogEntry`](TaxRateCollector.Core/Entities/ChangeLogEntry.cs) | A detected rate change (old → new), written by the diff engine. |
| [`Subscriber`](TaxRateCollector.Core/Entities/Subscriber.cs) / [`SubscribedState`](TaxRateCollector.Core/Entities/SubscribedState.cs) / [`SubscribedCategory`](TaxRateCollector.Core/Entities/SubscribedCategory.cs) / [`BillingRecord`](TaxRateCollector.Core/Entities/BillingRecord.cs) | Subscription + billing. `SubscribedCategory` gates per-category access; a subscriber needs both `SubscribedState` and `SubscribedCategory` for a state+category combination. |
| [`ZipCodeDistrict`](TaxRateCollector.Core/Entities/ZipCodeDistrict.cs) | Junction mapping a ZIP to one or more overlapping special taxing district jurisdictions (e.g. RTA, MPEA in Chicago). One ZIP → many `ZipCodeDistrict` rows. |
| [`JurisdictionData`](TaxRateCollector.Core/Entities/JurisdictionData.cs) ([`ICanonEntity`](TaxRateCollector.Core/Interfaces/ICanonEntity.cs)) | JSON-first canon projection of a jurisdiction's rate + provenance (string GUID id, `type`, `aliases`). |

### 4.3 Key services (VERBS)
| Service / Interface | Responsibility |
|---|---|
| [`ITaxCalculator`](TaxRateCollector.Core/Interfaces/ITaxCalculator.cs) → [`TaxCalculator`](TaxRateCollector.Infrastructure/Services/TaxCalculator.cs) | Walks the jurisdiction chain, sums rate lines, applies basis/brackets/caps/compound/sourcing. |
| [`IScrapeOrchestrator`](TaxRateCollector.Core/Interfaces/IScrapeOrchestrator.cs) → [`ScrapeOrchestrator`](TaxRateCollector.Infrastructure/Scrapers/ScrapeOrchestrator.cs) | Loops active jurisdictions, dispatches to the right [`IScrapeStrategy`](TaxRateCollector.Core/Interfaces/IScrapeStrategy.cs). |
| [`IScrapeStrategy`](TaxRateCollector.Core/Interfaces/IScrapeStrategy.cs) | One plug-in per source format: `CaliforniaCsvScraper`, `IllinoisTableScraper`, `TexasExcelScraper`. |
| `IStateBulkScraper` | Per-state bulk excise/sales scrapers (Wisconsin, Illinois, Minnesota, Iowa, Indiana, Michigan, the Dakotas, Ohio, Montana, Idaho, Oregon). |
| [`IDiffEngine`](TaxRateCollector.Core/Interfaces/IDiffEngine.cs) → [`DiffEngine`](TaxRateCollector.Infrastructure/Services/DiffEngine.cs) | Compares new scrape to current `IsCurrent` rows; emits `ChangeLogEntry` rows. |
| [`IRecursiveRateScraper`](TaxRateCollector.Core/Interfaces/IRecursiveRateScraper.cs) → [`RecursiveRateScraper`](TaxRateCollector.Infrastructure/Services/RecursiveRateScraper.cs) | Walks a jurisdiction hierarchy (State → County → City → District), discovers and AI-extracts rate laws with evidence via `IRateLawExtractor`, persists new `TaxRate` rows flagged `NeedsReview=true`. |
| `IRateLawExtractor` (defined in [`IRecursiveRateScraper.cs`](TaxRateCollector.Core/Interfaces/IRecursiveRateScraper.cs)) → [`ClaudeRateLawExtractor`](TaxRateCollector.Infrastructure/Services/ClaudeRateLawExtractor.cs) | AI extraction of structured rate laws from raw source content, **via `MindAttic.Legion` `LegionClient`** ([Law 3](#TRC-LAW-3)); `StubRateLawExtractor` is the keyless fallback. |
| [`IEvidenceFileStore`](TaxRateCollector.Core/Interfaces/IEvidenceFileStore.cs) → [`EvidenceFileStore`](TaxRateCollector.Infrastructure/Services/EvidenceFileStore.cs) | Hashes + stores uploaded/captured evidence under `%APPDATA%\MindAttic\TaxRateCollector\evidence\`. |
| [`ICensusJurisdictionImportService`](TaxRateCollector.Core/Interfaces/ICensusJurisdictionImportService.cs) / [`IZipImportService`](TaxRateCollector.Core/Interfaces/IZipImportService.cs) / [`ISstTaxonomyImportService`](TaxRateCollector.Core/Interfaces/ISstTaxonomyImportService.cs) | Setup-pipeline importers (Census gazetteers, ZCTA crosswalks, SSUTA PDF). |
| [`SettingsService`](TaxRateCollector.Infrastructure/Services/SettingsService.cs) | Reads/writes `%APPDATA%\MindAttic\TaxRateCollector\settings.json`; resolves the Anthropic key from the shared MindAttic credential store ([Law 5](#TRC-LAW-5), [HOUSE-LAW-3](#TRC-§5)). |
| `ScrapeWorkerService` / `ScrapeJobCoordinator` | Hosted background re-scrape execution + coordination. |
| [`IPayPalService`](TaxRateCollector.Core/Interfaces/IPayPalService.cs) → [`PayPalService`](TaxRateCollector.Infrastructure/Services/PayPalService.cs) | Subscription checkout + webhook billing. |

## 5. The Laws {#TRC-§5}

> This project **inherits the MindAttic House Rules** at [`../MindAttic.HouseRules.md`](../MindAttic.HouseRules.md)
> (`HOUSE-LAW-1` … `HOUSE-LAW-9`) by reference. They are not restated here. Relevant inherited laws:
> [HOUSE-LAW-1] whole-number versioning · [HOUSE-LAW-2] soft-disable never hard-delete ·
> [HOUSE-LAW-3] credentials via MindAttic.Vault · [HOUSE-LAW-4] provider-agnostic LLMs via
> MindAttic.Legion · [HOUSE-LAW-6] one engine, many front doors · [HOUSE-LAW-8] done is verified ·
> [HOUSE-LAW-9] psst only on request.
>
> The laws below are **TaxRateCollector-specific** and govern correctness of tax data.

### TRC-LAW-1 — No rate without evidence {#TRC-LAW-1}
Every authoritative `TaxRate` must be traceable to a `SourceDocument` whose `ContentHash = SHA256(RawContent)` and whose `FetchedAt` + `SourceUrl` make it independently verifiable offline. A rate lacking hash-verified evidence is **not** valid for export. "The API said so" is not a compliance answer.

### TRC-LAW-2 — Rates are retired, never overwritten {#TRC-LAW-2}
A rate change sets the prior row `IsCurrent=false` and inserts a new `IsCurrent=true` row; there is exactly one current row per `(JurisdictionId, TaxCategoryId[, ProductCategory])`. Jurisdictions deactivate (`IsActive=false`), never delete. (Specialises [HOUSE-LAW-2].) *(verified by `TaxRate_OnlyOneCurrentRatePerJurisdiction`.)*

### TRC-LAW-3 — LLM extraction routes through MindAttic.Legion {#TRC-LAW-3}
AI rate-law extraction calls go through `MindAttic.Legion` (`LegionClient`); no code path embeds a vendor SDK directly, and a missing key degrades gracefully to `StubRateLawExtractor` (no throw). (Specialises [HOUSE-LAW-4].)

### TRC-LAW-4 — Every rate change is logged {#TRC-LAW-4}
Whenever the `DiffEngine` detects a changed, removed, or structurally-altered rate versus the current run, it writes a `ChangeLogEntry`. Unchanged rates produce no entry. *(verified by `RateChanged_CreatesRateChangedEntry`, `UnchangedRate_CreatesNoEntry`, `StructuralChange_RateBasisChanges_CreatesStructuralChangeEntry`.)*

### TRC-LAW-5 — Secrets live in the MindAttic credential store, never in code {#TRC-LAW-5}
API keys (Anthropic, USPS, PayPal) resolve through `SettingsService` from `%APPDATA%\MindAttic\...` (the shared MindAttic credential store) or `IConfiguration` — never hard-coded or committed. (Specialises [HOUSE-LAW-3].) *(URL/settings defaults verified by `AppSettingsTests`.)*

### TRC-LAW-6 — Taxes are cumulative up the chain {#TRC-LAW-6}
A point-of-sale rate is the sum of every applicable tier (State + County + City + districts), filtered by category taxability and sourcing rule. ZIP codes carry no rate themselves — they are a lookup index into the jurisdiction hierarchy. *(verified by `CombinedRate_City_SumsStatePlusCountyPlusCity`, `CombinedRate_StateOnly_ReturnsSingleRate`.)*

### TRC-LAW-7 — Idempotent seeders and importers {#TRC-LAW-7}
Startup seeders and Setup-pipeline importers check before inserting and may be re-run without duplicating data. *(verified by `SeedAsync_IsIdempotent`, `Hierarchy_SeederDoesNotSeedIfCountryExists`.)*

### TRC-LAW-8 — SQL Server is the only datastore {#TRC-LAW-8}
The system targets SQL Server (LocalDB dev / Azure SQL prod) via EF Core 10. There is no SQLite path; stale references to SQLite in `TODO.md` are superseded by this law and by [TRC-A1](docs/AMENDMENTS.md#TRC-A1).

## 6. Verified state {#TRC-§6}

**Build:** `dotnet build TaxRateCollector.slnx -c Debug` → **Build succeeded, 0 warnings, 0 errors** (verified 2026-06-07).

**Tests:** `dotnet test TaxRateCollector.UnitTests --filter "Category!=Integration"` → **736 passed / 8 failed / 744 total** (verified 2026-06-07). Integration tests (`Category=Integration`) were not run here — they require SQL Server LocalDB with migrations applied — so anything depending solely on them stays 🟡.

Proven working (✅, each cited in [USER_STORIES](docs/USER_STORIES.md)):
- Cumulative rate calculation across tiers and all excise bases/brackets/caps/compound/sourcing — `TaxCalculatorTests` (both suites), `HierarchyTests`.
- Evidence hashing + integrity (SHA-256, tamper detection, MIME mapping, base64 PDF round-trip) — `EvidenceValidationTests`.
- Change detection (new/changed/removed/structural) — `DiffEngineTests`.
- SST taxonomy structure + idempotent category/profile seeding — `SstTaxonomyStructureTests`, `TaxCategorySeederTests`.
- Scrape strategies CA/IL/TX parse + skip invalid rows — `StrategyScraperTests`.
- Billing math (per-state pricing, tax-on-subscription, precision) — `BillingCalculationTests`.
- Settings defaults + URL validity — `AppSettingsTests`.

🟡 Known-failing as of 2026-06-07 (8 tests — see [TRC-US-C2](docs/USER_STORIES.md), [TRC-US-D2](docs/USER_STORIES.md)):
- `EvidenceFileStore` evidence-type / HTML-zip / CSV-type detection: `TextCsv_ReturnsEvidenceType_Csv` (returns `txt` not `csv`), `Html_ReturnsEvidenceType_Zip`, `SimpleHtmlZip_ContainsIndexHtml`, `SimpleHtmlZip_IndexHtml_ContainsOriginalContent`, `FullPageZip_BundlesLinkedAssets`, `FileName_MatchesExpectedPattern`.
- `AlertService` acknowledge flow: `AcknowledgeAllAsync_IsNoOp_WhenNoneExist`, `AcknowledgeAllAsync_MarksAllEntriesAcknowledged`.

## 7. Active frontier {#TRC-§7}

- **RFC:** [docs/rfc/0001-sst-bulk-scraper.md](docs/rfc/0001-sst-bulk-scraper.md) — one shared scraper for the 24 SSUTA member states instead of 24 near-identical classes.
- **Epics / backlog:** see [docs/USER_STORIES.md](docs/USER_STORIES.md) — coverage expansion to all ~3,144 counties / ~10,000+ cities (Epic B), evidence capture + Wayback fallback (Epic C), shared SST scraper (Epic E), public rate API (Epic G).
- **Immediate:** fix the 8 failing evidence/alert tests ([§6](#TRC-§6)).

## 8. Quality bar {#TRC-§8}

A feature is **done** ([HOUSE-LAW-8]) only when:
1. `dotnet build TaxRateCollector.slnx` is clean (0 errors).
2. New behaviour has NUnit coverage and the suite is green (`Category!=Integration` at minimum; `Category=Integration` for DB-shaped work).
3. Any rate-producing path attaches hash-verified evidence ([TRC-LAW-1](#TRC-LAW-1)) and logs changes ([TRC-LAW-4](#TRC-LAW-4)).
4. Secrets resolve through the credential store, not literals ([TRC-LAW-5](#TRC-LAW-5)).
5. Docs updated: a `✅` story names its verifying test; unproven work stays `🟡`/`⬜`.

## 9. Glossary {#TRC-§9}

- **Jurisdiction** — a node in the Country → State → County → City hierarchy.
- **SSUTA / SST** — Streamlined Sales & Use Tax Agreement; its Appendix C is the canonical product taxonomy; 24 member states apply it uniformly.
- **Evidence / provenance** — the raw government artifact (`SourceDocument`) plus its SHA-256 hash, source URL, and fetch timestamp that proves a rate.
- **RateBasis** — how a rate is applied: Percentage, FlatPerUnit, FlatPerVolume, FlatPerWeight, FlatPerProofGallon, PercentageOfWholesale.
- **IsCurrent** — flags the single live rate row per jurisdiction+category; superseded rows are retained with `IsCurrent=false`.
- **IsIncludedInPrice** — true when a tax is remitted upstream and already embedded in the retailer's cost (must not be re-added to the customer invoice).
- **IsCompound** — true when a rate applies to (price + other taxes), i.e. tax-on-tax.
- **Home rule** — a local jurisdiction that administers/collects its own sales tax independently of the state (CO, AL, LA).
- **Sourcing (origin/destination/modified)** — which location's rate applies for an intrastate sale.
- **ZCTA** — ZIP Code Tabulation Area; the Census geography that backs ZIP → jurisdiction lookup.
- **Front door** — an executable entry point (Blazor host, Worker) over the shared Core+Infrastructure engine ([HOUSE-LAW-6]).
