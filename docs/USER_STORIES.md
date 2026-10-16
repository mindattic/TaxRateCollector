---
codex: 1
project: TaxRateCollector
code: TRC
layer: stories
status: living
updated: 2026-10-03
---
<!-- GENERATED from MindAttic.Workflow/data/workflow.db by `workflow db render`. Do not edit: this file is rewritten from the database; change stories and tickets with the workflow CLI. -->

# TaxRateCollector — User Stories

> ✅ done (shipped & tested) · 🟡 partial · ⬜ planned. Every ✅ cites its verifying test.
> Status reflects the 2026-10-03 run: build clean; 748/748 unit tests passing (see [BIBLE §6](BIBLE.md#TRC-§6)).

## Epic A — Jurisdiction hierarchy & rate calculation

- **TRC-US-A1 ✅** As a tax team, I can resolve the combined rate for a city by summing State + County + City, so a point-of-sale rate is correct. *Given a CA→LA County→Beverly Hills chain, When I calculate, Then the total equals the sum of tiers.* *(verified by `CombinedRate_City_SumsStatePlusCountyPlusCity`, `CombinedRate_StateOnly_ReturnsSingleRate`, `CombinedRate_ZeroRateCountyAndCity_EqualsStateRate`.)*
- **TRC-US-A2 ✅** As a tax team, I get exactly one current rate per jurisdiction+category, with prior rates retired not deleted. *Given an edited rate, When I save, Then the old row is `IsCurrent=false` and a new `IsCurrent=true` row exists.* *(verified by `TaxRate_OnlyOneCurrentRatePerJurisdiction`; enforces [TRC-LAW-2](BIBLE.md#TRC-LAW-2).)*
- **TRC-US-A3 ✅** As a billing system, I get correct excise math for non-percentage bases (per-unit, per-volume, per-proof-gallon, per-weight, percentage-of-wholesale), brackets, per-unit caps, compound tax, and min-taxable floors. *(verified by `TaxCalculatorTests` — `FlatPerUnit_Rate_CalculatesCorrectly`, `FlatPerVolume_UsesProvidedVolume`, `FlatPerProofGallon_UsesVolumeAsProofGallons`, `PercentageOfWholesale_UsesWholesalePrice`, `FlatCapPerUnit_LimitsExcessivePercentageTax`, `MinTaxableAmount_ExcludesPortionBelowBracket`.)*
- **TRC-US-A4 ✅** As an integrator, an unknown jurisdiction or a jurisdiction with no current rates returns null rather than throwing. *(verified by `UnknownJurisdiction_ReturnsNull`, `NoCurrentRates_ReturnsNull`.)*
- **TRC-US-A5 ✅** As a data steward, county nodes are parented to the correct state and re-seeding does not duplicate the country. *(verified by `Hierarchy_CountyParentIsState`, `Hierarchy_SeederDoesNotSeedIfCountryExists`; enforces [TRC-LAW-7](BIBLE.md#TRC-LAW-7).)*

## Epic B — Population & geographic import

- **TRC-US-B1 ✅** As an admin, the SST product taxonomy and 51 state profiles seed idempotently on startup. *(verified by `SeedAsync_IsIdempotent`, `SeedAsync_CountMatchesDefinitions`, `SeedAsync_HasExactlyTwoRoots`, `TaxCategorySeederTests`.)*
- **TRC-US-B2 ✅** As an admin, ZIP→jurisdiction crosswalk parsing selects the primary county/place by largest land-area part. *(verified by `ZipCrosswalkParserTests`, `ZctaCrosswalkTests`, `CensusGazetteerParserTests`.)*
- **TRC-US-B3 🟡** As an admin, I can populate all ~3,144 counties and ~10,000+ cities via the Setup pipeline. *The `CensusJurisdictionImportService` exists and unit-level parsing is covered, but full-corpus population is a live-download integration step not asserted in the unit run.* (depends on `Category=Integration` + network.)
- **TRC-US-B4 🟡** As an admin, ZIP imports enrich city names via the USPS CityStateLookup API and persist `UspsValidated`. *Field + migration shipped; batch USPS validation of `Jurisdiction` rows (not just via ZIP import) is still ⬜.*

## Epic C — Evidence & provenance

- **TRC-US-C1 ✅** As an auditor, every rate's evidence is SHA-256 hashed and tamper-evident, with consistent MIME mapping and base64-PDF round-tripping. *(verified by `EvidenceValidationTests` — `Hash_SameContent_ProducesSameHash`, `SourceDocument_HashMatchesContent_PassesVerification`, `SourceDocument_TamperedContent_FailsVerification`, `SourceType_MimeMapping_IsConsistent`, `SourceDocument_Base64Pdf_RoundTrips`; enforces [TRC-LAW-1](BIBLE.md#TRC-LAW-1).)*
- **TRC-US-C2 ✅** As an admin, when I drop or capture an HTML/CSV source, the evidence store classifies its type and bundles a self-contained zip with the original content. *(verified by `EvidenceFileStoreTests` — `TextCsv_ReturnsEvidenceType_Csv`, `Html_ReturnsEvidenceType_Zip`, `SimpleHtmlZip_ContainsIndexHtml`, `SimpleHtmlZip_IndexHtml_ContainsOriginalContent`, `FullPageZip_BundlesLinkedAssets`, `FileName_MatchesExpectedPattern`.)*
- **TRC-US-C3 ⬜** As an admin, if a live `.gov` URL 404s, evidence capture falls back to the Wayback Machine. *(setting `wayback_machine_fallback` exists; capture path not yet built.)*

## Epic D — Scraping & change detection

- **TRC-US-D1 ✅** As the system, when I re-scrape I write a `ChangeLogEntry` only when a rate changed/was removed/changed structure, never for unchanged rates. *(verified by `DiffEngineTests` — `RateChanged_CreatesRateChangedEntry`, `UnchangedRate_CreatesNoEntry`, `AbsentJurisdiction_InCurrentRun_CreatesRemovedEntry`, `StructuralChange_RateBasisChanges_CreatesStructuralChangeEntry`; enforces [TRC-LAW-4](BIBLE.md#TRC-LAW-4).)*
- **TRC-US-D2 ✅** As the system, I alert admins to scrape anomalies and can acknowledge all alerts. *(verified by `AlertServiceTests` — `AcknowledgeAllAsync_IsNoOp_WhenNoneExist`, `AcknowledgeAllAsync_MarksAllEntriesAcknowledged`.)*
- **TRC-US-D3 ✅** As the system, format-specific strategies parse CA CSV / IL HTML table / TX XLSX and skip invalid or out-of-range rows. *(verified by `StrategyScraperTests` — `ScrapeAsync_ParsesValidCsvRow`, `ScrapeAsync_SkipsRowWithInvalidRate`, `ScrapeAsync_SkipsRowAboveCeiling`, `ScrapeAsync_ParsesHtmlTableRow`, `CanHandle_ReturnsTrue_ForCA`.)*
- **TRC-US-D4 ✅** As the system, the rate-string `Sanitizer` normalises `"6.25%"`, `"0.0625"`, `"$0.231/pack"` to a decimal. *(verified by `SanitizerTests`.)*
- **TRC-US-D5 ✅** As the system, AI rate extraction routes through MindAttic.Legion and degrades safely without a key. *(verified by `ClaudeExtractionParserTests`; enforces [TRC-LAW-3](BIBLE.md#TRC-LAW-3).)*
- **TRC-US-D6 ✅** As the system, the recursive scraper walks a jurisdiction hierarchy, AI-extracts rate laws with evidence, and persists them flagged `NeedsReview=true` for admin approval. *(verified by `RecursiveRateScraperTests`.)*
- **TRC-US-D7 ✅** As an admin, I can approve or reject AI-extracted rates via the review flow; approved rates become `IsCurrent=true` and rejected rates are removed. *(verified by `NeedsReviewFlowTests`.)*

## Epic E — SSUTA member-state scraping (frontier)

- **TRC-US-E1 ✅** As a maintainer, I have a statute-cited Wisconsin general-sales-tax pilot scraper (`WisconsinSalesTaxScraper`), the reference the shared SST scraper will be diffed against. *(verified by `BulkSalesTaxScraperTests`.)*
- **TRC-US-E2 ⬜** As a maintainer, one shared `SstSalesTaxScraper` covers all 24 SSUTA member states by fanning out over the uniform SST schema, instead of 24 near-identical classes. *(designed in [RFC 0001](rfc/0001-sst-bulk-scraper.md).)*

## Epic F — Subscriptions, roles & billing

- **TRC-US-F1 ✅** As a subscriber, I pay per subscribed state at the configured unit price, computed with decimal precision and never negative. *(verified by `BillingCalculationTests` — `PricePerMonth_MatchesStateCountTimesPrice`, `Total_FullMembership_50States_DefaultPrice`, `TaxAmount_DecimalPrecision_NotTruncated`, `Total_NeverNegative_ForValidInputs`.)*
- **TRC-US-F2 ✅** As a subscriber, tax on my subscription uses my billing state and zero-tax states charge no tax. *(verified by `BillingRecord_TaxOnSubscription_UsesSubscriberBillingState`, `BillingRecord_ZeroTaxState_NoTaxCharged`.)*
- **TRC-US-F3 ✅** As the system, PayPal checkout/webhook billing behaves per `PayPalServiceTests`, and subscription tax lookup resolves correctly. *(verified by `PayPalServiceTests`, `SubscriptionTaxLookupTests`, `SubscriptionFlowTests`.)*
- **TRC-US-F4 🟡** As an admin, I preview the UI as Visitor / Subscriber / Admin via `ViewAsService`. *Service shipped; behaviour is UI-level and not unit-asserted.*

## Epic G — Integration surface

- **TRC-US-G1 ⬜** As an enterprise consumer, I query `GET /api/rates?state=IL&county=Cook&city=Chicago` and get a combined rate + evidence. *(no HTTP API yet — UI + file exports only; see [BIBLE §3](BIBLE.md#TRC-§3).)*
- **TRC-US-G2 🟡** As a billing system, I export the master table to CSV / XLSX / SQL / HTML. *Exports implemented and partially covered (`ExportFormattingTests`, `ExportEvidenceSubqueryTests`); excise-rate inclusion in exports is still ⬜.*

## Priority backlog

1. [TRC-US-B3] Full county/city corpus import (run + assert via `Category=Integration`).
2. [TRC-US-C3] Wayback fallback on dead `.gov` URLs.
3. [TRC-US-E2] Shared SST bulk scraper ([RFC 0001](rfc/0001-sst-bulk-scraper.md)).
4. [TRC-US-G1] Public rate API.

## Tickets
> Permanent log of every change and fix — this repo's ticket system. Rows are append-only: never
> delete or reword one; only move its Status (open → closed / wont-do) and fill in Ref. Status is
> a word, not a glyph, so tickets don't count toward the story index. Keep this section last.

| ID | Date | Type | Status | Summary | Ref |
|---|---|---|---|---|---|
| TRC-T1 | 2026-10-12 | docs | closed | Start the permanent ticket log in USER_STORIES.md | — |
| TRC-T2 | 2026-10-14 | chore | closed | NUnit tests for ScraperHttpHelper (Infrastructure/Scrapers/ScraperHttpHelper.cs, internal, InternalsVisibleTo already set): via FakeHttpMessageHandler cover the fallback ladder order direct then browser-UA then browser-heavy then Wayback then archive.today then Jina Reader, that a 200 body missing requiredContent (case-insensitive) is a soft failure that moves to the next tier, that Wayback/archive.today are skipped when waybackFallback is false, the UrlUsed returned for each tier (original URL vs archive URL vs r.jina.ai proxy URL), Wayback JSON with available=false or malformed JSON, the three distinct InvalidOperationException messages (unreachable everywhere / required content missing / all failed) from GetRequiredStringAsync, and that GetOptionalStringAsync returns null instead of throwing; every bulk state scraper depends on this and only the Wayback path is exercised indirectly today | TaxRateCollector.UnitTests.ScraperTests.ScraperHttpHelperTests |
| TRC-T3 | 2026-10-14 | chore | open | NUnit tests for CensusJurisdictionImportService (Infrastructure/Services/CensusJurisdictionImportService.cs) with in-memory/SQLite DB and FakeHttpMessageHandler: counties created under the right state and existing county FIPS skipped; cities parented via the ZCTA place-to-county map, falling back to the first county in the same state when unmapped, and skipped when the state has no county (territories); ReparentCitiesAsync moves cities whose ParentId disagrees with the crosswalk and no-ops when the map is empty; RelinkZipsAsync fills null CountyJurisdictionId by FIPS and null CityJurisdictionId by state plus punctuation-insensitive city name without overwriting existing links; result counts are correct. Note the cache dir is a static AppData path, so tests must pre-seed or isolate those cache files (may need a small seam) to avoid touching the user's real cache | — |
| TRC-T4 | 2026-10-14 | chore | open | NUnit tests for ZipImportService (Infrastructure/Services/ZipImportService.cs): ZIPs absent from the county crosswalk are counted as skipped, state code derived from the first two county-FIPS digits including territories (PR, GU, VI), already-imported ZIPs are not re-added (idempotent re-run), state/county/city jurisdiction IDs linked by FIPS and normalized city name, USPS CityStateLookup city overrides the Census place name and sets Source to USPS+Census while Error elements and failed batches fall back to Census, city jurisdictions confirmed by USPS get UspsValidated and UspsValidatedAt set, and the ZipImportResult counts; no test touches this importer today and wrong ZIP links give wrong combined tax rates. Static AppData cache paths need isolating as with the Census importer | — |
| TRC-T5 | 2026-10-14 | chore | open | NUnit tests for SstTaxonomyImportService.WriteCategoriesAsync and ImportAsync error handling (Infrastructure/Services/SstTaxonomyImportService.cs): roots from SstTaxonomyData inserted first then children resolved across passes with correct ParentId, re-running updates descriptions of existing categories instead of duplicating rows (case-insensitive name match), PDF definitions resolved into Description via SstDefinitionParser, unresolvable parents logged and excluded; ImportAsync returns a failed SstImportResult (not an exception) when SstAgreementUrl is blank or the PDF has no Appendix C. Only the parser and static data are tested now, not the DB upsert that builds the category tree | — |
| TRC-T6 | 2026-10-14 | chore | open | NUnit tests for the scrape background services ScrapeWorkerService and ScrapeSchedulerService (Infrastructure/Services): on start, runs left in Running are recovered to Paused; a StartFull job calls IScrapeOrchestrator.RunFullScrapeAsync and a resume job with RunId calls ResumeAsync; a pause (cancelling the coordinator token) is swallowed and the coordinator is reset to not running in finally, including after an orchestrator exception; a resume job without RunId is a no-op. Use fake IScrapeOrchestrator, the real ScrapeJobCoordinator and in-memory DB; these drive every UI-triggered scrape and pause/resume | — |
| TRC-T7 | 2026-10-14 | chore | open | NUnit tests for the Worker project ScrapeJobWorker and MonthlySchedulerService (TaxRateCollector.Worker; add a ProjectReference from UnitTests and likely an internal/clock seam): ScrapeJobWorker claims only the oldest Pending run, does nothing when the conditional Pending-to-Running update affects 0 rows (already claimed), scrapes only active State jurisdictions, continues after a per-state exception, and marks the run Failed only when there are errors and zero laws created else Completed with ChangesDetected and ErrorCount set; MonthlySchedulerService queues a Pending run only on day 1 and only if no non-Manual run started this month (Manual seeder runs must not suppress it). Use SQLite in-memory since ExecuteUpdateAsync is not supported by the EF InMemory provider | — |
| TRC-T8 | 2026-10-14 | chore | open | NUnit tests for DemoSubscriberSeeder (Infrastructure/Seeding/DemoSubscriberSeeder.cs): first run creates the five demo users, Subscriber rows and their SubscribedStates; a second run is idempotent (no duplicate subscribers or states); a pre-existing subscriber missing some states only gets the missing ones added (case-insensitive state codes); a user whose CreateAsync fails is skipped without aborting the others. Needs a UserManager backed by an in-memory Identity store; runs at every Blazor startup so a regression duplicates data or blocks boot | — |
| TRC-T9 | 2026-10-14 | chore | open | NUnit tests for Blazor services ViewAsService and EfCoreSink (TaxRateCollector.Blazor/Services/ViewAsService.cs, Logging/EfCoreSink.cs; add a ProjectReference to the Blazor project): ViewAsService.EffectiveIsAdmin is true only for an actual admin in Actual role and false while impersonating Subscriber or Guest, setting Role raises RoleChanged, DemoSubscribedStateCodes matches case-insensitively; EfCoreSink.Emit drops events when the provider factory returns null or no IDbContextFactory is registered, writes Level, rendered Message, Exception, SourceContext with quotes trimmed and Properties JSON (null when there are no properties), and never throws when SaveChanges fails | — |
| TRC-T10 | 2026-10-14 | chore | open | Decide and do: move the Blazor EfCoreSink (SQL LogEntries table behind the Logs page) and the console-only Worker to MindAttic.Log, or record them as deliberate in MindAttic.Log's MIGRATION.md | — |
| TRC-T11 | 2026-10-15 | chore | closed | Remove duplicate command copies (/quickload, /quicksave) now centralized at the user level (~/.claude/commands) | M.FLOW-T14 |
