# deploy.ps1 - landing-page FTP deploy (project name read from deploy.settings.json -> Subscriber).
#
# 1. (build)  Renders README.md -> index.htm README-CONTENT marker block via build-html.js.
# 2. (sync)   Pulls MindAttic.Components, then splices Outfit/Attic/Cyberspace marker blocks
#             into index.htm via sync-landing-page.ps1.
# 3. (stamp)  Refreshes "Last Updated" comment at top of index.htm.
# 4. (deploy) Uploads index.htm to /mindattic.com/idiotproof/ via curl.exe (FTPS).
#
# Flags: -NoBuild skips step 1; -NoSync skips step 2.
# Credentials: deploy.settings.json (gitignored). Copy from .template if missing.

param (
    [string]$SettingsFile = "$PSScriptRoot\deploy.settings.json",
    [switch]$NoBuild,
    [switch]$NoSync
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

# ---------------------------------------------------------------------------
# Load settings
# ---------------------------------------------------------------------------
if (-not (Test-Path $SettingsFile)) {
    Write-Error @"
deploy.settings.json not found at: $SettingsFile
Copy deploy.settings.json.template -> deploy.settings.json and fill in your FTP
credentials. The .gitignore already excludes the real file.
"@
    exit 1
}

$cfg = Get-Content -Raw -Path $SettingsFile | ConvertFrom-Json

$ftpHost    = $cfg.FtpHost
$ftpPort    = $cfg.FtpPort
$ftpUser    = $cfg.FtpUsername
$ftpPass    = $cfg.FtpPassword
$remotePath = $cfg.FtpRemotePath.TrimEnd('/')
$useSsl     = [bool]$cfg.FtpUseSsl
$usePassive = [bool]$cfg.FtpPassive
if (-not ($cfg.PSObject.Properties.Name -contains 'Subscriber') -or [string]::IsNullOrWhiteSpace($cfg.Subscriber)) {
    Write-Error "deploy.settings.json is missing the 'Subscriber' field (e.g. 'IdiotProof'). Add it and re-run."
    exit 1
}
$subscriber = $cfg.Subscriber

# ---------------------------------------------------------------------------
# 1. Render README.md -> index.htm via build-html.js
# ---------------------------------------------------------------------------
if (-not $NoBuild) {
    $buildScript = Join-Path $PSScriptRoot 'build-html.js'
    if (-not (Test-Path $buildScript)) { Write-Error "build-html.js missing at $buildScript"; exit 1 }
    $markedDir = Join-Path $repoRoot 'node_modules\marked'
    if (-not (Test-Path $markedDir)) {
        Write-Host "marked not found in node_modules - running 'npm install' first..."
        Push-Location $repoRoot
        try {
            & npm install --silent --no-audit --no-fund
            if ($LASTEXITCODE -ne 0) { Write-Error "npm install failed (exit $LASTEXITCODE)"; exit 1 }
        } finally { Pop-Location }
    }

    Write-Host "Rendering README.md -> index.htm ..."
    Push-Location $repoRoot
    try {
        & node $buildScript
        if ($LASTEXITCODE -ne 0) { Write-Error "build-html.js failed (exit $LASTEXITCODE)"; exit 1 }
    } finally { Pop-Location }
}

# ---------------------------------------------------------------------------
# 2. Sync MindAttic.Components -> index.htm (Outfit / Attic / Cyberspace)
# ---------------------------------------------------------------------------
$contentRoot = Join-Path (Split-Path -Parent $repoRoot) 'MindAttic.Components'
$syncScript  = Join-Path $contentRoot 'sync\sync-landing-page.ps1'

if (-not $NoSync) {
    if (-not (Test-Path "$contentRoot\.git")) {
        Write-Error "MindAttic.Components is not a git repo at $contentRoot. Clone https://github.com/mindattic/MindAttic.Components.git there first."
        exit 1
    }

    Write-Host "Pulling MindAttic.Components..."
    $ErrorActionPreference = "Continue"
    $pullOut  = & git -C $contentRoot pull --no-edit --no-rebase 2>&1
    $pullExit = $LASTEXITCODE
    $ErrorActionPreference = "Stop"
    if ($pullExit -ne 0) {
        Write-Host $pullOut -ForegroundColor Red
        Write-Error "git pull on MindAttic.Components failed (exit $pullExit)."
        exit 1
    }

    if (-not (Test-Path $syncScript)) { Write-Error "sync-landing-page.ps1 not found at $syncScript"; exit 1 }

    Write-Host "Syncing MindAttic.Components -> index.htm (subscriber: $subscriber)..."
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $syncScript -Subscriber $subscriber -TargetIndex "$repoRoot\index.htm"
    if ($LASTEXITCODE -ne 0) { Write-Error "sync-landing-page.ps1 failed (exit $LASTEXITCODE)"; exit 1 }
}

# ---------------------------------------------------------------------------
# 3. Stamp index.htm
# ---------------------------------------------------------------------------
$indexFile = Join-Path $repoRoot 'index.htm'
if (-not (Test-Path $indexFile)) { Write-Error "index.htm missing at $indexFile"; exit 1 }

$date    = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$stamp   = "<!-- Last Updated: $date -->"
$content = [System.IO.File]::ReadAllText($indexFile, [System.Text.Encoding]::UTF8)

if ($content -match "(?s)^<!--\s*Last Updated:.*?-->(\r?\n)") {
    $content = $content -replace "(?s)^<!--\s*Last Updated:.*?-->(\r?\n)", "$stamp`$1"
} else {
    $content = "$stamp`r`n$content"
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($indexFile, $content, $utf8NoBom)
Write-Host "Stamped: $date"

# ---------------------------------------------------------------------------
# 4. Deploy via curl.exe
# ---------------------------------------------------------------------------
$curlArgs = @('--ftp-create-dirs', '--insecure')
if ($usePassive) { $curlArgs += '--ftp-pasv' }
if ($useSsl)     { $curlArgs += '--ssl-reqd' }

$file      = Get-Item $indexFile
$remoteUrl = "ftp://${ftpHost}:${ftpPort}${remotePath}/index.htm"

Write-Host ""
Write-Host "Deploying to ftp://${ftpHost}:${ftpPort}${remotePath}/ ..."
Write-Host ("-" * 60)

$ErrorActionPreference = "Continue"
$output = & curl.exe @curlArgs -u "${ftpUser}:${ftpPass}" -T $file.FullName $remoteUrl 2>&1
$ErrorActionPreference = "Stop"

if ($LASTEXITCODE -eq 0) {
    Write-Host ("  [OK] index.htm  ({0:N0} bytes)" -f $file.Length)
    Write-Host ("-" * 60)
    Write-Host "Done."
    exit 0
} else {
    Write-Host "  [FAIL] index.htm  - exit $LASTEXITCODE : $output" -ForegroundColor Red
    Write-Host ("-" * 60)
    Write-Host "Done. 1 failed."
    exit 1
}
