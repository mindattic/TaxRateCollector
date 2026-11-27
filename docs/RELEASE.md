---
layer: release
status: living
---
# TaxRateCollector — Release charter

> The one finish line this repo is working toward. Every open ticket in this repo sits under one of the epics
> below, and every epic is part of the milestone. Work that does not fit is out of scope until this milestone is
> reached (owner, 2026-11-27, MAT-T319).

## 1. What this is
A Blazor Server app (plus a background Worker) that keeps an evidence-backed table of US sales and excise tax
rates for every jurisdiction, each rate bound to the hashed, timestamped .gov document it came from; for the owner
as an internal rate database with CSV / XLSX / SQL / HTML exports.

## 2. The milestone: TaxRateCollector 1.0 local rate database {#milestone}
- **Milestone:** MS-29, "TaxRateCollector 1.0 local rate database"
- **Published means:** an internal tool deployed by `run.ps1` to `R:\MindAttic\Apps\TaxRateCollector\Debug` and
  `\Release`, running against a dedicated `TaxRateCollector` database on the localhost SQL Server instance. No
  cloud hosting (Azure was decommissioned 2026-11-26; the Hetzner move is dropped).
- **The natural stopping point:** the app runs entirely on local SQL with no Azure leftovers in code, config,
  tests or workflows; the full Census jurisdiction and ZIP corpus is imported and counted; the scrapers that
  already exist have been bug-hunted; the importers and background services that feed the data carry NUnit
  coverage; exports include excise rates; and the README, BIBLE and in-app Glossary describe what the code does.
  That is a complete, trustworthy version of what is already built. New data sources and a public API are the
  next milestone, not this one.

## 3. Done when (acceptance checks)
Each line is also a `tickets milestone check`.
1. build — `dotnet build TaxRateCollector.slnx` is clean on main.
2. exists — `R:\MindAttic\Apps\TaxRateCollector\Release` holds the deployed Blazor build.
3. exists — no `database.windows.net` connection string and no `azure-deploy.yml` remain in the repo.
4. signoff — the owner opens the deployed app against localhost SQL, looks up a ZIP in the calculator, and exports
   a state's rates (sales and excise) with their evidence.

## 4. Epics, in priority order
| Priority | Epic (ticket) | What it delivers | Features |
|---|---|---|---|
| 900 | TRC-T101 Local SQL and deploy | Dedicated localhost SQL database, no Azure leftovers, run-menu deploy on R: | TRC-T105 Localhost SQL database (990), TRC-T106 Run-menu deploy on R: (980) |
| 800 | TRC-T102 Data and scraper correctness | Full Census and ZIP corpus imported and counted, alcohol scrapers bug-hunted, excise in exports | TRC-T107 Full jurisdiction corpus (890), TRC-T108 Scraper bughunt (880), TRC-T109 Excise in exports (870) |
| 700 | TRC-T103 Core service test coverage | NUnit coverage for importers, scrape background services, demo seeder and Blazor services | TRC-T110 Importer tests (790), TRC-T111 Background service tests (780), TRC-T112 Seeder and Blazor service tests (770) |
| 600 | TRC-T104 Docs match the code | README, Glossary and Setup text match the code; TODO.md retired | TRC-T113 Truthful docs (690) |

## 5. Out of scope (not before this milestone)
- Public REST / GraphQL endpoint and rate-change webhooks (TRC-US-G1) — no consumer exists yet.
- Any cloud hosting: Azure (decommissioned), Hetzner (the DB move script MAT-T244 is wont-do), containers, CI/CD
  deploys, Key Vault, Blob storage.
- Shared SST bulk scraper (RFC 0001) and new per-state general sales tax scrapers — coverage expansion is the next
  milestone.
- Wayback Machine fallback, PDF OCR cross-checks and Playwright page screenshots for evidence.
- USPS batch validation of every jurisdiction row as a background job.
- Subscription management page, PDF export, bulk CSV rate import, keyboard tree navigation, automatic bacpac
  backups on startup.
- Moving logging to MindAttic.Log.
- Generic sweeps: "run the whole suite and fix everything", open-ended bughunts, "clean up code" passes.

## 6. Current verified state
- Builds clean; 748 of 748 unit tests passed on 2026-10-03 (BIBLE §6).
- Evidence hashing, DiffEngine change log, cumulative-chain calculator (LAW-6 with combined-rate rows), CA / IL /
  TX strategies, 12 state alcohol scrapers plus Wisconsin sales tax, AI discovery via Legion with a review queue,
  PayPal subscriptions — all built (see README).
- Recent bughunt fixes landed TRC-T71 to TRC-T96 (scrape orchestrator, sanitizer, review, exports, logs, setup,
  glossary).
- Scheduler re-scrapes on `default_update_frequency_days` (TRC-T63, T98); HTML evidence capture with assets
  (TRC-T62) and PDF auto-fetch (TRC-T61).
- `run.ps1` uses the shared run menu (MAT-T306), but no build has yet been deployed under
  `R:\MindAttic\Apps\TaxRateCollector`.
- Development points at LocalDB (TRC-T69); design-time factory, test settings and a GitHub workflow still name
  Azure SQL.

## 7. Decision record
- 2026-11-27 — charter drafted from README/BIBLE/TODO/RFC 0001/tickets; Legion vote: Approve, 9 of 9 voters (plurality quorum reached, consensus 1.0); no dissent.
- Triage: 21 open (excluding this job, TRC-T100) → done 0, duplicate 0, wont-do 8 (TRC-T10, T15, T16, T65, T66, T67, T68 Hetzner, T22 container superseded by TRC-T105), kept 13, new tasks 5 (TRC-T114 to T118).
