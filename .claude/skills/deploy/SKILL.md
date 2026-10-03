---
name: deploy
description: Deploy TaxRateCollector via MindAttic.Deploy (sibling repo). Fires the GitHub Actions workflow that targets the taxratecollector Azure App Service. Currently DISABLED in MindAttic.Deploy until Azure infrastructure + AZURE_WEBAPP_PUBLISH_PROFILE secret are provisioned.
---

When invoked, run:

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "cd D:\Projects\MindAttic\MindAttic.Deploy; npm run deploy -- --app taxratecollector"
```

Report the result. Today this prints the "disabled" note and exits 0 -- the project's `.github/workflows/azure-deploy.yml` exists but the Azure side is not yet provisioned. To enable: provision the `taxratecollector` App Service in Azure, add the `AZURE_WEBAPP_PUBLISH_PROFILE` secret to `mindattic/TaxRateCollector`, then flip `apps[].disabled` from `true` to `false` in `MindAttic.Deploy/projects.json`.

Notes:
- The README-driven landing page (`mindattic.com/taxratecollector.htm`) that the legacy `scripts/cli/deploy.{bat,ps1}` + `build-html.js` used to ship -- and later MindAttic.Deploy's catalog mode (`--only taxratecollector`) -- is retired (MindAttic.Deploy DEP-A6, 2026-10-03). This repo's README on GitHub (https://github.com/mindattic/TaxRateCollector) is the project page. This `/deploy` command is for the APP only.
