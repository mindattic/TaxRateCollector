---
codex: 1
project: TaxRateCollector
code: TRC
layer: stories
status: living
updated: 2026-06-07
---

# TaxRateCollector — User Stories

> ✅ done (shipped & tested) · 🟡 partial · ⬜ planned · 🗑️ cut. Every ✅ cites its verifying test.
> Status reflects the 2026-06-07 run: build clean; 736/744 unit tests passing (see [BIBLE §6](BIBLE.md#TRC-§6)).

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
- **TRC-US-C2 🟡** As an admin, when I drop or capture an HTML/CSV source, the evidence store classifies its type and bundles a self-contained zip with the original content. *Implemented but currently failing: `EvidenceFileStore` returns `txt` instead of `csv` and the HTML→zip bundling assertions fail.* (failing tests: `TextCsv_ReturnsEvidenceType_Csv`, `Html_ReturnsEvidenceType_Zip`, `SimpleHtmlZip_ContainsIndexHtml`, `SimpleHtmlZip_IndexHtml_ContainsOriginalContent`, `FullPageZip_BundlesLinkedAssets`, `FileName_MatchesExpectedPattern` — see [BIBLE §6](BIBLE.md#TRC-§6).)
- **TRC-US-C3 ⬜** As an admin, if a live `.gov` URL 404s, evidence capture falls back to the Wayback Machine. *(setting `wayback_machine_fallback` exists; capture path not yet built.)*

## Epic D — Scraping & change detection

- **TRC-US-D1 ✅** As the system, when I re-scrape I write a `ChangeLogEntry` only when a rate changed/was removed/changed structure, never for unchanged rates. *(verified by `DiffEngineTests` — `RateChanged_CreatesRateChangedEntry`, `UnchangedRate_CreatesNoEntry`, `AbsentJurisdiction_InCurrentRun_CreatesRemovedEntry`, `StructuralChange_RateBasisChanges_CreatesStructuralChangeEntry`; enforces [TRC-LAW-4](BIBLE.md#TRC-LAW-4).)*
- **TRC-US-D2 🟡** As the system, I alert admins to scrape anomalies and can acknowledge all alerts. *`AlertService` exists and most paths pass, but the acknowledge-all flow is failing.* (failing tests: `AcknowledgeAllAsync_IsNoOp_WhenNoneExist`, `AcknowledgeAllAsync_MarksAllEntriesAcknowledged`.)
- **TRC-US-D3 ✅** As the system, format-specific strategies parse CA CSV / IL HTML table / TX XLSX and skip invalid or out-of-range rows. *(verified by `StrategyScraperTests` — `ScrapeAsync_ParsesValidCsvRow`, `ScrapeAsync_SkipsRowWithInvalidRate`, `ScrapeAsync_SkipsRowAboveCeiling`, `ScrapeAsync_ParsesHtmlTableRow`, `CanHandle_ReturnsTrue_ForCA`.)*
- **TRC-US-D4 ✅** As the system, the rate-string `Sanitizer` normalises `"6.25%"`, `"0.0625"`, `"$0.231/pack"` to a decimal. *(verified by `SanitizerTests`.)*
- **TRC-US-D5 ✅** As the system, AI rate extraction routes through MindAttic.Legion and degrades safely without a key. *(verified by `ClaudeExtractionParserTests`; enforces [TRC-LAW-3](BIBLE.md#TRC-LAW-3).)*
- **TRC-US-D6 ✅** As the system, the recursive scraper walks a jurisdiction hierarchy, AI-extracts rate laws with evidence, and persists them flagged `NeedsReview=true` for admin approval. *(verified by `RecursiveRateScraperTests`.)*
- **TRC-US-D7 ✅** As an admin, I can approve or reject AI-extracted rates via the review flow; approved rates become `IsCurrent=true` and rejected rates are removed. *(verified by `NeedsReviewFlowTests`.)*

## Epic E — SSUTA member-state scraping (frontier)

- **TRC-US-E1 ✅** As a maintainer, I have a Wisconsin pilot scraper for a non-SST state (statute-cited). *(verified by `BulkSalesTaxScraperTests`.)*
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

1. Fix the 8 failing tests — [TRC-US-C2](#) evidence type/zip detection, [TRC-US-D2](#) alert acknowledge. *(unblocks a clean green suite — the headline quality gate, [BIBLE §8](BIBLE.md#TRC-§8).)*
2. [TRC-US-B3] Full county/city corpus import (run + assert via `Category=Integration`).
3. [TRC-US-C3] Wayback fallback on dead `.gov` URLs.
4. [TRC-US-E2] Shared SST bulk scraper ([RFC 0001](rfc/0001-sst-bulk-scraper.md)).
5. [TRC-US-G1] Public rate API.

### Audit log

No stories have been re-scoped from an original written specification yet. When a story's intent
changes, preserve the original ask here verbatim, marked "(original spec — audit log)".
