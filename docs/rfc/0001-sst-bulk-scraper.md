---
codex: 1
project: TaxRateCollector
code: TRC
layer: rfc
status: planned
updated: 2026-06-07
---

# RFC 0001 — One shared SST bulk scraper for the 24 SSUTA member states

## Problem
The 24 SSUTA full-member states (AR, GA, IN, IA, KS, KY, MI, MN, NE, NV, NJ, NC, ND, OH, OK, RI, SD, TN, UT, VT, WA, WV, WI, WY) publish rate data against the **uniform Streamlined schema** (the SST rate/boundary feed and Taxability Matrix). Writing one `IScrapeStrategy`/`IStateBulkScraper` per state would create 24 near-identical classes that all parse the same format — duplication that violates the spirit of [TRC-LAW-7](../BIBLE.md#TRC-LAW-7) (one mechanism) and is expensive to maintain when the SST format changes.

## Options compared
1. **24 per-state classes.** Maximum flexibility, maximum duplication. Rejected: the member states are uniform by treaty.
2. **One `SstSalesTaxScraper` that fans out by state code over the uniform SST feed.** Single parser, single maintenance point; per-state behaviour is data (the state's SST endpoint + code), not a class.
3. **Hybrid (chosen).** One shared `SstSalesTaxScraper` for the 24 members; keep bespoke per-state classes only for **non-member** states (CA, FL, IL, NY, TX, and partial states like WI) whose DOR page formats and statutes genuinely differ.

## Decision
Adopt **Option 3**. Build a single `SstSalesTaxScraper` (registered once) that resolves the target state from `Jurisdiction.StateCode`, fetches that state's SST rate/boundary feed, and emits `RawScrapeResult` rows through the same `Sanitizer` + `DiffEngine` path every other scraper uses. Non-member states keep their dedicated strategies.

## What NOT to do
- Do **not** hard-code per-state rate values — the scraper reads the live SST feed ([TRC-LAW-1](../BIBLE.md#TRC-LAW-1): evidence-backed).
- Do **not** duplicate the SST parser across states.
- Do **not** apply SST Taxability Matrix classifications to non-member states; their taxability comes from their own statutes.
- Do **not** bypass the `DiffEngine` / `ChangeLogEntry` path ([TRC-LAW-4](../BIBLE.md#TRC-LAW-4)).

## Phased plan (with risk)
1. **Schema spike** — confirm the SST feed shape and per-state endpoint discovery. *Risk: SST endpoints vary subtly per state; mitigate with a per-state endpoint map (data, not code).*
2. **Single-state vertical slice** — implement `SstSalesTaxScraper` for one member state (e.g. WI, which has an existing pilot to diff against) end-to-end with a fixture-backed unit test. *Risk: confidence scoring for SST rows; reuse the existing `RawScrapeResult.Confidence` convention.*
3. **Fan-out** — enable the remaining 23 member states; add a fixture test per format variant (not per state).
4. **Cutover** — retire any duplicate per-member-state classes; mark them 🗑️ in the bible and note the git tag.

## Graduates into
- Bible: a new key-service row under [§4.3](../BIBLE.md#TRC-§4) for `SstSalesTaxScraper`.
- Stories: [TRC-US-E2](../USER_STORIES.md) flips ⬜ → ✅ once the fixture tests are green.
