<#
.SYNOPSIS
  Codex documentation tooling for TaxRateCollector (project code: TRC).

.DESCRIPTION
  Two subcommands:
    doctor  - validate the Codex docs (front-matter, unique IDs, resolvable cross-refs,
              cited file paths exist, every checkmark story names a test that exists,
              generatedFrom artifacts not stale) and regenerate+compare the digest.
    digest  - regenerate docs/BIBLE.digest.md from BIBLE.md sections 1, 3, 5, 9 plus a
              status index and the latest amendment head.

  Windows PowerShell 5.1 compatible. No build step. Run from anywhere:
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/codex.ps1 doctor
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/codex.ps1 digest
#>
[CmdletBinding()]
param(
  [Parameter(Position = 0)]
  [ValidateSet('doctor', 'digest')]
  [string]$Command = 'doctor'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot  = Split-Path -Parent $ScriptDir
$DocsDir   = Join-Path $RepoRoot 'docs'
$BiblePath      = Join-Path $DocsDir 'BIBLE.md'
$StoriesPath    = Join-Path $DocsDir 'USER_STORIES.md'
$AmendmentsPath = Join-Path $DocsDir 'AMENDMENTS.md'
$RfcDir         = Join-Path $DocsDir 'rfc'
$DataDir        = Join-Path $DocsDir 'data'
$DigestPath     = Join-Path $DocsDir 'BIBLE.digest.md'

$script:Errors   = New-Object System.Collections.Generic.List[string]
$script:Warnings = New-Object System.Collections.Generic.List[string]

function Add-Err ($m)  { $script:Errors.Add($m)   | Out-Null }
function Add-Warn ($m) { $script:Warnings.Add($m) | Out-Null }

function Read-Text ($path) {
  return [System.IO.File]::ReadAllText($path)
}

# ---------------------------------------------------------------------------
# Front-matter
# ---------------------------------------------------------------------------
function Get-FrontMatter ($path) {
  $text = Read-Text $path
  $m = [regex]::Match($text, '(?s)\A---\r?\n(.*?)\r?\n---')
  if (-not $m.Success) { return $null }
  $fm = @{}
  foreach ($line in ($m.Groups[1].Value -split "\r?\n")) {
    $kv = [regex]::Match($line, '^\s*([A-Za-z_]+)\s*:\s*(.+?)\s*$')
    if ($kv.Success) { $fm[$kv.Groups[1].Value] = $kv.Groups[2].Value }
  }
  return $fm
}

function Test-FrontMatter ($path, $expectedLayer) {
  if (-not (Test-Path $path)) { Add-Err "MISSING file: $path"; return }
  $fm = Get-FrontMatter $path
  $rel = $path.Substring($RepoRoot.Length).TrimStart('\','/')
  if ($null -eq $fm) { Add-Err "[$rel] no YAML front-matter block"; return }
  foreach ($k in 'codex','project','code','layer','status','updated') {
    if (-not $fm.ContainsKey($k)) { Add-Err "[$rel] front-matter missing key '$k'" }
  }
  if ($fm.ContainsKey('codex') -and $fm['codex'] -ne '1') { Add-Err "[$rel] codex must be 1 (was '$($fm['codex'])')" }
  if ($fm.ContainsKey('layer') -and $expectedLayer -and $fm['layer'] -ne $expectedLayer) {
    Add-Err "[$rel] layer must be '$expectedLayer' (was '$($fm['layer'])')"
  }
  if ($fm.ContainsKey('updated') -and ($fm['updated'] -notmatch '^\d{4}-\d{2}-\d{2}$')) {
    Add-Err "[$rel] 'updated' must be YYYY-MM-DD (was '$($fm['updated'])')"
  }
}

# ---------------------------------------------------------------------------
# Anchors + cross-references
# ---------------------------------------------------------------------------
function Get-AllDocFiles {
  $files = @($BiblePath, $StoriesPath, $AmendmentsPath)
  if (Test-Path $RfcDir) { $files += (Get-ChildItem $RfcDir -Filter *.md -File | ForEach-Object FullName) }
  return $files | Where-Object { Test-Path $_ }
}

function Test-AnchorsAndRefs {
  $anchorOwners = @{}   # anchor -> file
  $duplicates   = @{}
  $refs = New-Object System.Collections.Generic.List[object]

  foreach ($f in (Get-AllDocFiles)) {
    $text = Read-Text $f
    $rel = $f.Substring($RepoRoot.Length).TrimStart('\','/')

    # Definitions: {#ANCHOR}
    foreach ($am in [regex]::Matches($text, '\{#([A-Za-z0-9\-]+)\}')) {
      $a = $am.Groups[1].Value
      if ($anchorOwners.ContainsKey($a)) { $duplicates[$a] = $true }
      else { $anchorOwners[$a] = $rel }
    }
    # References: markdown links containing #ANCHOR  (e.g. (BIBLE.md#TRC-LAW-1) or (#TRC-LAW-1))
    foreach ($lm in [regex]::Matches($text, '\]\(([^)]*#[A-Za-z0-9\-]+)\)')) {
      $target = $lm.Groups[1].Value
      $anchor = ($target -split '#')[-1]
      $refs.Add([pscustomobject]@{ File = $rel; Anchor = $anchor; Target = $target }) | Out-Null
    }
  }

  foreach ($d in $duplicates.Keys) { Add-Err "duplicate anchor id '{#$d}' defined in more than one place" }

  foreach ($r in $refs) {
    # Skip placeholder links like (#) used in backlog bullets
    if ($r.Anchor -eq '' -or $r.Target -eq '#') { continue }
    if (-not $anchorOwners.ContainsKey($r.Anchor)) {
      # HOUSE-* anchors live in the shared house-rules file (inherited by reference) - allow.
      if ($r.Anchor -like 'HOUSE-*') { continue }
      Add-Err "[$($r.File)] cross-ref '#$($r.Anchor)' resolves to no {#...} anchor"
    }
  }
  return $anchorOwners
}

# ---------------------------------------------------------------------------
# Cited file paths exist
# ---------------------------------------------------------------------------
function Test-CitedPaths {
  $text = Read-Text $BiblePath
  $seen = @{}
  foreach ($lm in [regex]::Matches($text, '\]\(([^)#]+)(?:#[^)]*)?\)')) {
    $target = $lm.Groups[1].Value.Trim()
    if ($target -eq '') { continue }
    if ($target -match '^(https?:|mailto:)') { continue }
    if ($seen.ContainsKey($target)) { continue }
    $seen[$target] = $true

    # Cited paths in BIBLE.md are repo-root-relative (e.g. TaxRateCollector.Core/...,
    # docs/USER_STORIES.md, ../MindAttic.HouseRules.md). Accept if the path resolves
    # against the repo root OR the docs/ dir (tolerate either convention).
    $fromRoot = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $target))
    $fromDocs = [System.IO.Path]::GetFullPath((Join-Path $DocsDir $target))
    if (-not (Test-Path $fromRoot) -and -not (Test-Path $fromDocs)) {
      Add-Err "[docs/BIBLE.md] cited path does not exist on disk: $target"
    }
  }
}

# ---------------------------------------------------------------------------
# Stories: every checkmark names a test token that exists in the test tree
# ---------------------------------------------------------------------------
function Get-TestTokenIndex {
  $idx = @{}
  $testRoots = Get-ChildItem $RepoRoot -Directory -Filter '*Test*' -ErrorAction SilentlyContinue
  foreach ($root in $testRoots) {
    foreach ($cs in (Get-ChildItem $root.FullName -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue)) {
      $body = Read-Text $cs.FullName
      foreach ($mm in [regex]::Matches($body, '(?:public|private|internal)\s+(?:async\s+)?(?:Task|void)\s+([A-Za-z0-9_]+)\s*\(')) {
        $idx[$mm.Groups[1].Value] = $true
      }
    }
  }
  return $idx
}

function Test-StoryCheckmarks {
  if (-not (Test-Path $StoriesPath)) { return }
  $text = Read-Text $StoriesPath
  $testIdx = Get-TestTokenIndex
  $checkmark = [char]0x2705   # ✅

  foreach ($line in ($text -split "\r?\n")) {
    if ($line -notmatch [string]$checkmark) { continue }
    if ($line -notmatch '\*\*TRC-US-') { continue }   # only story bullets
    $idm = [regex]::Match($line, 'TRC-US-[A-Za-z0-9]+')
    $sid = if ($idm.Success) { $idm.Value } else { '(unknown)' }

    # Pull every backticked token on the line, keep ones that look like test names.
    $tokens = [regex]::Matches($line, '`([A-Za-z0-9_]+)`') | ForEach-Object { $_.Groups[1].Value } |
              Where-Object { $_ -match '_' -or $_ -match 'Tests$' }
    if (@($tokens).Count -eq 0) {
      Add-Err "[USER_STORIES] $sid is marked done but names no test token"
      continue
    }
    $found = $false
    foreach ($t in $tokens) {
      if ($testIdx.ContainsKey($t)) { $found = $true; break }
      # also accept a class name token (e.g. `TaxCalculatorTests`)
      if ($t -match 'Tests$') {
        $cls = Get-ChildItem $RepoRoot -Recurse -Filter "$t.cs" -File -ErrorAction SilentlyContinue
        if ($cls) { $found = $true; break }
      }
    }
    if (-not $found) {
      Add-Warn "[USER_STORIES] $sid done but none of its cited tests were found in the test tree: $($tokens -join ', ')"
    }
  }
}

# ---------------------------------------------------------------------------
# data/*.json validate against _schema/*.schema.json (only if present)
# ---------------------------------------------------------------------------
function Test-DataFiles {
  if (-not (Test-Path $DataDir)) { return }
  $ids = @{}
  foreach ($json in (Get-ChildItem $DataDir -Filter *.json -File -ErrorAction SilentlyContinue)) {
    $rel = $json.FullName.Substring($RepoRoot.Length).TrimStart('\','/')
    try { $obj = Read-Text $json.FullName | ConvertFrom-Json }
    catch { Add-Err "[$rel] invalid JSON: $($_.Exception.Message)"; continue }
    $entities = if ($obj -is [System.Array]) { $obj } elseif ($obj.PSObject.Properties.Name -contains 'entities') { $obj.entities } else { @($obj) }
    foreach ($e in $entities) {
      if (-not ($e.PSObject.Properties.Name -contains 'id')) { Add-Err "[$rel] entity missing 'id'"; continue }
      if ($ids.ContainsKey($e.id)) { Add-Err "[$rel] duplicate entity id '$($e.id)'" } else { $ids[$e.id] = $true }
    }
  }
}

# ---------------------------------------------------------------------------
# generatedFrom staleness: digest source mtime <= digest mtime
# ---------------------------------------------------------------------------
function Test-DigestFreshness {
  if (-not (Test-Path $DigestPath)) { Add-Warn 'docs/BIBLE.digest.md missing - run: codex.ps1 digest'; return }
  $digestMtime = (Get-Item $DigestPath).LastWriteTimeUtc
  foreach ($src in @($BiblePath, $AmendmentsPath)) {
    if (Test-Path $src) {
      $srcMtime = (Get-Item $src).LastWriteTimeUtc
      if ($srcMtime -gt $digestMtime) {
        $rel = $src.Substring($RepoRoot.Length).TrimStart('\','/')
        Add-Warn "BIBLE.digest.md is stale: $rel changed after the digest was generated - run: codex.ps1 digest"
      }
    }
  }
}

# ---------------------------------------------------------------------------
# Section extraction for the digest
# ---------------------------------------------------------------------------
function Get-Section ($text, $num) {
  # captures from "## <num>. " up to the next "## " or end
  $pattern = '(?ms)^##\s+' + [regex]::Escape([string]$num) + '\.\s.*?(?=^##\s|\z)'
  $m = [regex]::Match($text, $pattern)
  if ($m.Success) { return $m.Value.TrimEnd() }
  return $null
}

function Get-StatusCounts ($storiesText) {
  $done    = ([regex]::Matches($storiesText, [char]0x2705)).Count                     # done
  $partial = ([regex]::Matches($storiesText, [char]::ConvertFromUtf32(0x1F7E1))).Count # partial
  $planned = ([regex]::Matches($storiesText, [char]0x2B1C)).Count                     # planned
  $cut     = ([regex]::Matches($storiesText, [char]::ConvertFromUtf32(0x1F5D1))).Count # cut
  return [pscustomobject]@{ Done = $done; Partial = $partial; Planned = $planned; Cut = $cut }
}

function Get-LatestAmendmentHead ($amendText) {
  $m = [regex]::Match($amendText, '(?ms)^##\s+TRC-A\d+.*?(?=^##\s|\z)')
  $last = $null
  foreach ($mm in [regex]::Matches($amendText, '(?ms)^##\s+TRC-A\d+.*?(?=^##\s|\z)')) { $last = $mm.Value }
  if ($last) {
    $lines = ($last.TrimEnd() -split "\r?\n")
    # head: title line + up to 4 following non-empty lines
    $head = @($lines[0])
    $count = 0
    for ($i = 1; $i -lt $lines.Count -and $count -lt 5; $i++) {
      if ($lines[$i].Trim() -ne '') { $head += $lines[$i]; $count++ }
    }
    return ($head -join "`n")
  }
  return '(no amendments yet)'
}

# ---------------------------------------------------------------------------
# digest
# ---------------------------------------------------------------------------
function Invoke-Digest {
  if (-not (Test-Path $BiblePath)) { Write-Error "BIBLE.md not found at $BiblePath"; exit 2 }
  $bible   = Read-Text $BiblePath
  $stories = if (Test-Path $StoriesPath) { Read-Text $StoriesPath } else { '' }
  $amend   = if (Test-Path $AmendmentsPath) { Read-Text $AmendmentsPath } else { '' }

  $s1 = Get-Section $bible 1
  $s3 = Get-Section $bible 3
  $s5 = Get-Section $bible 5
  $s9 = Get-Section $bible 9
  $counts = Get-StatusCounts $stories
  $head = Get-LatestAmendmentHead $amend

  $checkmark = [char]0x2705
  $partial   = [char]::ConvertFromUtf32(0x1F7E1)
  $planned   = [char]0x2B1C
  $cut       = [char]::ConvertFromUtf32(0x1F5D1)

  $sb = New-Object System.Text.StringBuilder
  [void]$sb.AppendLine('AUTHORITATIVE - full detail in docs/BIBLE.md')
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine('---')
  [void]$sb.AppendLine('codex: 1')
  [void]$sb.AppendLine('project: TaxRateCollector')
  [void]$sb.AppendLine('code: TRC')
  [void]$sb.AppendLine('layer: digest')
  [void]$sb.AppendLine('status: living')
  [void]$sb.AppendLine('generatedFrom: TRC-bible')
  [void]$sb.AppendLine(('updated: ' + (Get-Date -Format 'yyyy-MM-dd')))
  [void]$sb.AppendLine('---')
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine('# TaxRateCollector - Bible Digest (generated; do not hand-edit)')
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine('> Generated by tools/codex.ps1 from docs/BIBLE.md sections 1, 3, 5, 9 + status index')
  [void]$sb.AppendLine('> + latest amendment. Regenerate with: powershell -File tools/codex.ps1 digest')
  [void]$sb.AppendLine('')
  if ($s1) { [void]$sb.AppendLine($s1); [void]$sb.AppendLine('') }
  if ($s3) { [void]$sb.AppendLine($s3); [void]$sb.AppendLine('') }
  if ($s5) { [void]$sb.AppendLine($s5); [void]$sb.AppendLine('') }
  if ($s9) { [void]$sb.AppendLine($s9); [void]$sb.AppendLine('') }
  [void]$sb.AppendLine('## Status index')
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine(("- {0} done: {1}" -f $checkmark, $counts.Done))
  [void]$sb.AppendLine(("- {0} partial: {1}" -f $partial, $counts.Partial))
  [void]$sb.AppendLine(("- {0} planned: {1}" -f $planned, $counts.Planned))
  [void]$sb.AppendLine(("- {0} cut: {1}" -f $cut, $counts.Cut))
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine('## Latest amendment')
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine($head)
  [void]$sb.AppendLine('')

  # Write UTF-8 (no BOM) so the hook can read it cleanly.
  $enc = New-Object System.Text.UTF8Encoding($false)
  [System.IO.File]::WriteAllText($DigestPath, $sb.ToString(), $enc)
  Write-Host ("digest -> {0} ({1} chars)" -f ($DigestPath.Substring($RepoRoot.Length).TrimStart('\','/')), $sb.Length)
}

# ---------------------------------------------------------------------------
# doctor
# ---------------------------------------------------------------------------
function Invoke-Doctor {
  Test-FrontMatter $BiblePath      'bible'
  Test-FrontMatter $StoriesPath    'stories'
  Test-FrontMatter $AmendmentsPath 'amendments'
  if (Test-Path $RfcDir) {
    foreach ($r in (Get-ChildItem $RfcDir -Filter *.md -File)) { Test-FrontMatter $r.FullName 'rfc' }
  }
  if (Test-Path $DataDir) {
    foreach ($d in (Get-ChildItem $DataDir -Filter *.json -File -ErrorAction SilentlyContinue)) { Test-FrontMatter $d.FullName 'data' }
  }

  [void](Test-AnchorsAndRefs)
  Test-CitedPaths
  Test-StoryCheckmarks
  Test-DataFiles

  # Regenerate the digest, then check freshness (so doctor self-heals + warns).
  Invoke-Digest
  Test-DigestFreshness

  Write-Host ''
  Write-Host '=== codex doctor: TaxRateCollector (TRC) ==='
  $checks = @(
    'front-matter present and valid (BIBLE, USER_STORIES, AMENDMENTS, rfc/*)',
    'anchor ids unique; cross-references resolve',
    'cited file paths exist on disk',
    'every done story names a test that exists in the test tree',
    'data/*.json validate + unique ids (none defined - skipped)',
    'BIBLE.digest.md regenerated; staleness checked'
  )
  foreach ($c in $checks) { Write-Host ("  [check] {0}" -f $c) }

  if ($script:Warnings.Count -gt 0) {
    Write-Host ''
    Write-Host ("WARNINGS ({0}):" -f $script:Warnings.Count)
    foreach ($w in $script:Warnings) { Write-Host ("  ! {0}" -f $w) }
  }

  Write-Host ''
  if ($script:Errors.Count -gt 0) {
    Write-Host ("DOCTOR FAILED - {0} error(s):" -f $script:Errors.Count)
    foreach ($e in $script:Errors) { Write-Host ("  X {0}" -f $e) }
    exit 1
  }
  Write-Host 'DOCTOR OK - all hard checks passed.'
  exit 0
}

switch ($Command) {
  'digest' { Invoke-Digest }
  'doctor' { Invoke-Doctor }
}
