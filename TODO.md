# TaxRateCollector — Backlog

A living task list covering all features discussed across sessions.
Mark items `[x]` when complete. Tackle in priority order within each section.

---

## ✅ Completed

- [x] EF Core entity hierarchy: Country → State → County → City (self-referential ParentId)
- [x] SourceDocument entity — raw API response / PDF / website capture stored per TaxRate row
- [x] Migration: AddHierarchyAndSourceDocument
- [x] ICanonEntity + JurisdictionData domain models (JSON-first, Canon pattern)
- [x] TaxRateConstants (JurisdictionTier, TaxSourceType, TaxCategory, TaxRateType string constants)
- [x] Full US seeder: 1 Country + 51 State/DC + ~200 Counties + ~450 Cities (~700 jurisdictions, 2024 rates)
- [x] SettingsService — reads/writes %APPDATA%\MindAttic\TaxRateCollector\settings.json
- [x] Jurisdictions page — lazy-loading tree with inline rate editing and evidence panel
- [x] Master Table page — sortable/filterable full dataset; export to CSV, XLSX, SQL
- [x] Settings page — light/dark theme toggle, USPS API key, scraper options
- [x] Dark mode (CSS custom properties, data-theme attribute, persisted to settings.json)
- [x] Navigation: Jurisdictions | Master Table | Settings
- [x] NUnit test suite: evidence validation (SHA-256, hash integrity, MIME mapping, URL validation)
- [x] NUnit test suite: hierarchy (combined rate, seeder idempotency, rate retirement, FIPS codes)
- [x] Migration Designer.cs for AddExciseTaxRates
- [x] AppDbContextModelSnapshot.cs updated with ExciseTaxRate + ExciseSourceDocument

---

## 🔴 High Priority

### Data Completeness
- [ ] Expand seeder to cover all ~3,144 US counties (currently ~200)
- [ ] Expand seeder to cover major cities per county (target: 5,000+ cities)
- [ ] USPS Address Validator integration — validate every state, county, city against USPS API
  - Repo reference: https://github.com/mindattic/USPSAddressValidator
  - Use modern JSON endpoint (not legacy XML)
  - Run as a background validation job; mark jurisdictions as USPS-verified
- [ ] Add `UspsValidated` bool + `UspsValidatedAt` timestamp to Jurisdiction entity + migration

### Evidence & Provenance
- [ ] Web scraper: fetch .gov source URL, capture page as PDF print / full-HTML snapshot
  - Store as base64 in SourceDocument.RawContent (MIME: application/pdf or text/html)
  - Record SHA-256 hash for integrity verification
- [ ] Wayback Machine fallback: if live .gov URL 404s, query archive.org for last cached version
  - Setting already in SettingsService (WaybackMachineFallback)
- [ ] PDF OCR evidence extraction: parse PDF evidence blobs to extract rate value for cross-check
- [ ] Rate change detection: compare new scraped rate to current; auto-create ChangeLogEntry

### Sin / Excise Taxes
- [x] Add `ProductCategory` enum: Alcohol, Tobacco, Sugar, Cannabis, Firearms, etc.
- [x] Add per-jurisdiction `ExciseTaxRate` table keyed by (JurisdictionId, ProductCategory) + migration
- [ ] UI: show excise tax rates in the Jurisdictions tree (collapsible sub-section under each node)
- [ ] Include excise rates in Master Table export

---

## 🟡 Medium Priority

### Authentication & Roles
- [ ] Add ASP.NET Core Identity with two roles: `Administrator`, `Subscriber`
- [ ] .env file (or secrets.json) for admin credentials so dev login is always available
- [ ] Protect Jurisdictions write actions (rate edit, evidence save) behind `Administrator` role
- [ ] Master Table (read-only view) accessible to `Subscriber` role

### Paid Subscriptions
- [ ] PayPal subscription integration — monthly plan at $0.01 (dev test tier)
  - Use PayPal REST API / PayPal JS SDK
  - On successful payment webhook: grant `Subscriber` role to user
  - Reference StreetSamurai repo for existing PayPal/membership pattern
- [ ] Subscription management page: show plan status, cancel, receipt history

### Hierarchy UI (Enhanced)
- [ ] File upload for evidence: drag-and-drop PDF or text file in evidence panel
  - Store file bytes as base64 in SourceDocument.RawContent
  - Show thumbnail / file name in evidence panel
- [ ] Evidence history: show all historical SourceDocuments per rate, not just the latest
- [ ] Bulk rate import: CSV upload to set rates for many jurisdictions at once
- [ ] Keyboard navigation: arrow keys to expand/collapse tree nodes

---

## 🟢 Lower Priority / Future

### Scraping Automation
- [ ] Per-jurisdiction source URL registry: known .gov URLs where each tier publishes updates
- [ ] Scheduled scraper: background job checks each jurisdiction on its UpdateFrequencyDays cadence
- [ ] Scraper result diffing: highlight changed rates in yellow in the tree
- [ ] Screenshot capture: Playwright or similar to capture .gov page as PNG for evidence archive

### Database
- [ ] SQLite backup on startup — timestamped file copy of `taxrates.db` → `backups/taxrates_<timestamp>.db`, keep last N (e.g. 10), delete older ones automatically

### Azure Deployment
- [ ] Containerize with Dockerfile (ASP.NET Core + SQLite or migrate to Azure SQL)
- [ ] Azure App Service deployment
- [ ] CI/CD pipeline (GitHub Actions) — reference StreetSamurai repo for pipeline template
- [ ] Move settings.json to Azure Key Vault / App Configuration for production secrets
- [ ] Azure Blob Storage for large evidence documents (PDFs, screenshots)

### Testing
- [ ] NUnit test project for evidence validation workflows:
  - Parse rate from API JSON response
  - Parse rate from PDF OCR
  - Hash verification of stored SourceDocument
  - Combined rate calculation (State + County + City)
  - Rate change detection logic
  - USPS address validation response parsing
- [ ] Integration tests: seed a fresh SQLite DB, run the full hierarchy seeder, assert row counts

### Export & Integration
- [ ] PDF export of Master Table (e.g., using QuestPDF or Playwright print-to-PDF)
- [ ] REST API endpoint: GET /api/rates?state=IL&county=Cook&city=Chicago → returns combined rate + evidence
- [ ] Webhook: notify integrators when a rate changes
- [ ] GraphQL endpoint for flexible querying by enterprise consumers

### Documentation
- [x] README.md: developer setup, architecture overview, local run, migration, seeder, deployment
- [ ] How-to: adding a new scraper strategy for a new state
- [ ] How-to: evidence workflow (capture → hash → store → verify)
- [ ] API reference for the REST/GraphQL endpoint

---

## Notes

- Target corpus: ~14,000 US jurisdictions (Country + 51 states + ~3,144 counties + ~10,000 cities)
- Corporate consumers need FIPS-keyed, evidence-backed, auditable rate history
- Architecture: Blazor Server + EF Core + SQLite (dev) / Azure SQL (prod) + ClosedXML exports
- Each tax rate row must have attached evidence to be considered "validated" for export
