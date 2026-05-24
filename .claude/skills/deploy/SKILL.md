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
- The legacy `scripts/cli/deploy.{bat,ps1}` + `build-html.js` in this repo only deployed the FTP **landing page** (`mindattic.com/taxratecollector.htm`) -- not the Blazor app. The landing page is now deployed centrally from MindAttic.Deploy (`npm run deploy -- --only taxratecollector`); this `/deploy` command is for the APP.
