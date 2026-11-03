<#
  Builds branding\platinvault.ico from frontend\public\favicon.svg in the on-demand Playwright container.
  Run again whenever the app icon changes:  .\scripts\build-icon.ps1
#>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'dev-common.ps1')

$image = 'mcr.microsoft.com/playwright:v1.64.1-noble'
$repo = $RootDir -replace '\\', '/'
$inner = 'mkdir -p /tmp/ic && cd /tmp/ic && npm init -y >/dev/null 2>&1 && npm i --no-audit --no-fund playwright@1.64.1 >/dev/null 2>&1 && cp /repo/scripts/build-icon.mjs /tmp/ic/ && node /tmp/ic/build-icon.mjs /repo/frontend/public/favicon.svg /repo/branding/platinvault.ico'

Write-Step 'Erzeuge branding\platinvault.ico (Playwright-Container)...'
Invoke-Native 'docker' @('run', '--rm', '-v', "${repo}:/repo", $image, 'sh', '-c', $inner) | Out-Null
Write-Ok 'branding\platinvault.ico ist aktuell.'
