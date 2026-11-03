<#
  Builds the project documents as PDF in the on-demand Playwright container (no local browser install):
    docs\handbuch\de\HANDBUCH.md        -> docs\handbuch\de\PlatinVault-Handbuch.pdf
    docs\handbuch\en\HANDBOOK.md        -> docs\handbuch\en\PlatinVault-Handbook.pdf
    intern\Konzept.md                   -> intern\Konzept.pdf   (only when that private, git-ignored file exists; skipped otherwise)
  Run after every change to one of these files:  .\scripts\build-konzept.ps1
#>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'dev-common.ps1')

$image = 'mcr.microsoft.com/playwright:v1.64.1-noble'
$repo = $RootDir -replace '\\', '/'
$logo = '/repo/frontend/public/favicon.svg'
$inner = 'mkdir -p /tmp/kp && cd /tmp/kp && npm init -y >/dev/null 2>&1 && npm i --no-audit --no-fund marked@16 playwright@1.64.1 @fontsource/inter@5 @fontsource/jetbrains-mono@5 >/dev/null 2>&1 && cp /repo/scripts/konzept-pdf.mjs /tmp/kp/' +
    " && node /tmp/kp/konzept-pdf.mjs /repo/docs/handbuch/de/HANDBUCH.md /repo/docs/handbuch/de/PlatinVault-Handbuch.pdf $logo" +
    " && node /tmp/kp/konzept-pdf.mjs /repo/docs/handbuch/en/HANDBOOK.md /repo/docs/handbuch/en/PlatinVault-Handbook.pdf $logo"
$withConcept = Test-Path -LiteralPath (Join-Path $RootDir 'intern\Konzept.md')
if ($withConcept) {
    $inner += " && node /tmp/kp/konzept-pdf.mjs /repo/intern/Konzept.md /repo/intern/Konzept.pdf $logo"
}

Write-Step 'Erzeuge die Handbuecher als PDF (Playwright-Container)...'
Invoke-Native 'docker' @('run', '--rm', '-v', "${repo}:/repo", $image, 'sh', '-c', $inner) | Out-Null
Write-Ok 'Die Handbuecher (DE/EN) sind aktuell.'
if ($withConcept) { Write-Ok 'intern\Konzept.pdf ist aktuell.' }
