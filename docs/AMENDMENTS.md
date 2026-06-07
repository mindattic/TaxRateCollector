---
codex: 1
project: TaxRateCollector
code: TRC
layer: amendments
status: living
updated: 2026-06-07
---

# TaxRateCollector — Amendments (append-only; amendment wins over the bible)

> Append-only change log. Never rewrite an amendment — supersede it with a new one.
> When an amendment conflicts with the bible prose, the amendment wins until folded back into L0.

## TRC-A1 — Datastore is SQL Server, not SQLite (supersedes TODO.md prose) {#TRC-A1}
**What changed.** Early planning notes in [`TODO.md`](../TODO.md) describe SQLite (dev) with `taxrates.db` file backups. The shipped system runs on **SQL Server** — LocalDB in dev, SQL Server / Azure SQL in prod — via EF Core 10 (`AppDbContextFactory`, `UseSqlServer`, the `2026*` migration set, and `DatabaseBackupTests` parsing a `(localdb)\MSSQLLocalDB` connection string).
**Why.** The hierarchy, filtered indexes, and `sqlpackage`-based `.bacpac` backup all assume SQL Server.
**Migration.** None required. Treat any SQLite reference in `TODO.md` as historical; canon is [TRC-LAW-8](BIBLE.md#TRC-LAW-8).

## TRC-A2 — Migration history table in README is illustrative, not literal (supersedes README §"Database Migrations") {#TRC-A2}
**What changed.** The README "Migration history" table lists friendly names (`AddHierarchyAndSourceDocument`, `AddTaxCategoryToTaxRate`, `ClearTaxCategories`, `AddStateTaxProfiles`) that do **not** match the on-disk EF migrations. The real migrations are the timestamped set in [`TaxRateCollector.Infrastructure/Migrations/`](../TaxRateCollector.Infrastructure/Migrations/) (`20260418044705_InitialCreate` … `20260529033323_FixBillingTaxRatePrecision`).
**Why.** Doctor verifies cited file paths exist; canon must point at the real migration folder, which is authoritative.
**Migration.** None. Use `dotnet ef migrations list` for the live history.
