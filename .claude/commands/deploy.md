Deploy the TaxRateCollector landing page (`mindattic.com/taxratecollector.htm`) via **MindAttic.Deploy** (sibling repo at `D:\Projects\MindAttic\MindAttic.Deploy`).

Renders this repo's `README.md` through the catalog template (`template/index.template.htm`, Cyberspace theme, MindAttic.UiUx components loaded via jsDelivr) and FTPS-uploads the single-file result. One repo owns the whole FTP pipeline — there is no per-project deploy state in this folder.

Run this command and report the result:

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "cd D:\Projects\MindAttic\MindAttic.Deploy; npm run deploy -- --only taxratecollector"
```

It will:

1. Render `D:\Projects\MindAttic\TaxRateCollector\README.md` through the catalog template.
2. FTPS-upload `out/taxratecollector.htm` to `/mindattic.com/taxratecollector.htm`.

After running, summarize the result and flag any failures.

Notes:
- Catalog entry: `MindAttic.Deploy/projects.json` -> `projects[]` slug `taxratecollector` (theme: Cyberspace).
- Credentials: MindAttic.Vault at `%APPDATA%\MindAttic\Deploy\ftp.json` (transitional fallback: `MindAttic.Deploy/secrets/ftp.json`, gitignored).
- A Blazor app deploy also exists in `apps[]` (`--app taxratecollector`) but is **disabled** pending Azure infra (App Service + `AZURE_WEBAPP_PUBLISH_PROFILE`). Until that's provisioned, `/deploy` ships the landing page only.
