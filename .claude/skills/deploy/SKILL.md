---
name: deploy
description: Render README.md, sync MindAttic.Components, and FTP-upload index.htm to mindattic.com/taxratecollector/. Runs scripts/cli/deploy.bat.
---

When invoked:

1. Run `scripts\cli\deploy.bat` from the project root (`D:\Projects\MindAttic\TaxRateCollector`).
2. The script will:
   - `node scripts/cli/build-html.js` -- renders `README.md` into the `<!-- BEGIN README-CONTENT -->` marker block of `index.htm` (using marked + highlight.js). Auto-runs `npm install` if `node_modules` is absent.
   - `git pull` MindAttic.Components (sibling repo) for the latest font / Cyberspace bundle.
   - `sync-landing-page.ps1 -Subscriber TaxRateCollector` -- splices OutfitFont / AtticFont / Cyberspace marker blocks into `index.htm`.
   - Stamp `<!-- Last Updated: ... -->` at the top of `index.htm`.
   - FTPS upload `index.htm` to `/mindattic.com/taxratecollector/` (defined in `scripts/cli/deploy.settings.json`).
3. Report the FTP outcome (OK/FAIL) and the deployed URL.

Flags:
- `-NoBuild` -- skip the README render step.
- `-NoSync` -- skip the components pull/splice.

Notes:
- Credentials come from `scripts/cli/deploy.settings.json` (gitignored). If missing, copy `deploy.settings.json.template` and fill in.
- `node_modules/` is gitignored; `npm install` runs on first deploy.