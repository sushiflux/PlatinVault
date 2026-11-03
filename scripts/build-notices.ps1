<#
  Generates THIRD-PARTY-NOTICES.md (repo root): the notices for the bundled open-source components that ship in every artifact
  (Docker image, bridge ZIP, "Lizenzen" viewer in the app). Sources: frontend\package-lock.json + frontend\node_modules (browser app),
  a temporary "dotnet publish" of the server and the bridge (NuGet packages that really ship), the official .NET runtime packs in
  the NuGet cache, and scripts\notices-data\sources.json (fonts, sounds). Fails loudly, without writing, when a component has no
  determinable licence. Details in scripts\build-notices.ts.

  Run it whenever dependencies change and before every beta/release:  .\scripts\build-notices.ps1
  Only check whether the committed file is still current:           .\scripts\build-notices.ps1 -Check

  Needs Node.js 24+ (runs the TypeScript script directly), the .NET 10 SDK and "npm ci" in frontend\.
  Windows PowerShell 5.1 compatible. Console output is German and ASCII-only.
#>
param(
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'dev-common.ps1')

if (-not (Get-Command 'node' -ErrorAction SilentlyContinue)) {
    throw 'node wurde nicht gefunden. Bitte Node.js 24 oder neuer installieren: https://nodejs.org/en/download'
}
if (-not (Get-Command 'dotnet' -ErrorAction SilentlyContinue)) {
    throw 'dotnet wurde nicht gefunden. Bitte das .NET 10 SDK installieren: https://dotnet.microsoft.com/download/dotnet/10.0'
}
$nodeMajor = [int]((& node -p "process.versions.node.split('.')[0]") | Select-Object -First 1)
if ($nodeMajor -lt 24) {
    throw "Node.js $nodeMajor ist zu alt - das Skript braucht Node.js 24 oder neuer (fuehrt TypeScript direkt aus)."
}
if (-not (Test-Path -LiteralPath (Join-Path $script:RootDir 'frontend\node_modules'))) {
    throw 'frontend\node_modules fehlt - bitte zuerst "npm ci" im Ordner frontend ausfuehren.'
}

$arguments = @((Join-Path $PSScriptRoot 'build-notices.ts'))
if ($Check) { $arguments += '--check' }

Write-Step $(if ($Check) { 'Pruefe THIRD-PARTY-NOTICES.md ...' } else { 'Erzeuge THIRD-PARTY-NOTICES.md ...' })
$null = Invoke-Native 'node' $arguments
if (-not $Check) { Write-Ok 'THIRD-PARTY-NOTICES.md ist aktuell. Fuer die Auslieferung wird sie ins Docker-Image und in das Bridge-ZIP kopiert.' }
