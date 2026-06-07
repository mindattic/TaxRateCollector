<#
  SessionStart hook: injects docs/BIBLE.digest.md as authoritative context.

  Emits Claude Code hook JSON:
    { "hookSpecificOutput": { "hookEventName": "SessionStart", "additionalContext": "<preamble+digest>" } }

  Windows PowerShell 5.1 / Win-1252 safe: all non-ASCII is escaped to \uXXXX so the
  JSON is pure ASCII regardless of console code page. If the digest is missing or
  empty, emits {} (a no-op).
#>
$ErrorActionPreference = 'Stop'

try {
  $hookDir   = Split-Path -Parent $MyInvocation.MyCommand.Definition
  $repoRoot  = Split-Path -Parent (Split-Path -Parent $hookDir)
  $digestPath = Join-Path $repoRoot 'docs\BIBLE.digest.md'

  if (-not (Test-Path $digestPath)) { Write-Output '{}'; exit 0 }

  $digest = [System.IO.File]::ReadAllText($digestPath)
  if ([string]::IsNullOrWhiteSpace($digest)) { Write-Output '{}'; exit 0 }

  $preamble = @"
[TaxRateCollector / Codex] The following is the AUTHORITATIVE project digest, generated from
docs/BIBLE.md (the single source of truth) and docs/AMENDMENTS.md (amendment wins over the bible).
Treat it as ground truth for what this project IS, is NOT, and its Laws. When it conflicts with
older assumptions, the digest and the full docs/BIBLE.md win. Full detail lives in docs/BIBLE.md,
docs/USER_STORIES.md, docs/AMENDMENTS.md, and docs/rfc/. Org-wide rules are inherited from
MindAttic.HouseRules.md.

"@

  $context = $preamble + $digest

  # Build JSON manually and ASCII-escape every non-ASCII char to \uXXXX.
  $payload = '{"hookSpecificOutput":{"hookEventName":"SessionStart","additionalContext":"PLACEHOLDER"}}'

  $sb = New-Object System.Text.StringBuilder
  foreach ($ch in $context.ToCharArray()) {
    $code = [int]$ch
    switch ($ch) {
      '"'  { [void]$sb.Append('\"') }
      '\'  { [void]$sb.Append('\\') }
      "`b" { [void]$sb.Append('\b') }
      "`f" { [void]$sb.Append('\f') }
      "`n" { [void]$sb.Append('\n') }
      "`r" { [void]$sb.Append('\r') }
      "`t" { [void]$sb.Append('\t') }
      default {
        if ($code -lt 32 -or $code -gt 126) {
          [void]$sb.Append('\u' + $code.ToString('x4'))
        } else {
          [void]$sb.Append($ch)
        }
      }
    }
  }

  $json = $payload.Replace('PLACEHOLDER', $sb.ToString())
  Write-Output $json
  exit 0
}
catch {
  # Never block a session on a hook error.
  Write-Output '{}'
  exit 0
}
