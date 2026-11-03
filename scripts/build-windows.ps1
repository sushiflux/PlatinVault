<#
  Builds the Windows program of PlatinVault and its designed installer:

    artifacts\windows\PlatinVault-Setup-<version>.exe          the installer (one file; the program is appended to it)
    artifacts\windows\PlatinVault-Setup-<version>.exe.sha256   its SHA-256 (the program's update check verifies it)
    artifacts\windows\PlatinVault.Bridge.exe                   the bridge as its own download (same release)

  Steps: React UI (frontend\dist), the bridge
  (scripts\build-bridge.ps1), "dotnet publish" of src\PlatinVault.Desktop (self-contained, win-x64), then everything into one folder
  (app + wwwroot + docs + bridge + version.txt), zipped and appended to the published PlatinVault.Setup single-file exe. The database is
  SQLite inside the program (native e_sqlite3.dll of SQLitePCLRaw, imports only kernel32.dll); nothing else is bundled.
  .NET builds go to artifacts\windows-cache\dotnet (--artifacts-path), so a running "dotnet watch" of the dev stack is never touched.

  Version: version.txt of the repository (written by the pre-commit hook), nothing is maintained by hand. A beta passes -Beta: the program
  then shows "v2026.11.12 (13.557 beta-sign)" instead of "v2026.11.12 (13.557)" (the program computes that itself from version.txt and the
  channel in release-channel.txt, see src\PlatinVault.Versioning\VersionFormat.cs). The installer's file name uses the technical form
  without spaces, parentheses or the beta sign: PlatinVault-Setup-v2026.11.12.exe (release) and
  PlatinVault-Setup-v2026.11.12.13.557-beta.exe (beta) - the same string as the git tag, which the program's update check relies on.
  A dev build (commit word "Dev-Build", the user's own test package, never published) passes -Dev: channel "dev", the program shows
  "v2026.11.12 (13.557-dev)", never looks for updates, and the installer is PlatinVault-Setup-v2026.11.12.13.557-dev.exe.
  -Beta and -Dev exclude each other.

  Windows PowerShell 5.1 compatible. Console output is German and ASCII-only.

  Aufruf:  .\scripts\build-windows.ps1 [-Beta | -Dev] [-BridgeVersion 1.0.7] [-SkipFrontend]
#>
param(
    [switch]$Beta,
    [switch]$Dev,
    [string]$BridgeVersion,
    [switch]$SkipFrontend
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'dev-common.ps1')
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = $script:RootDir
$out = Join-Path $root 'artifacts\windows'
$cache = Join-Path $root 'artifacts\windows-cache'
$stage = Join-Path $cache 'stage'
$dotnetArtifacts = Join-Path $cache 'dotnet'

# ---- version (version.txt line 1 = vYear.Month.Commit, line 2 = five-segment build id) ------------------------------------------------
$versionLines = @(Get-Content -LiteralPath (Join-Path $root 'version.txt') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($versionLines.Count -eq 0) { throw 'version.txt ist leer.' }
$short = $versionLines[0]
$detail = if ($versionLines.Count -gt 1) { $versionLines[1] } else { $short }
if ($Beta -and $Dev) { throw '-Beta und -Dev schliessen sich aus.' }
$channel = if ($Beta) { 'beta' } elseif ($Dev) { 'dev' } else { 'release' }
# Technical form (git tag, installer file name): release = line 1, beta/dev = the five-segment build id + "-beta" / "-dev". Same rule as
# VersionFormat.Tag and scripts/version-display.sh. A beta or dev build needs the counters of line 2, otherwise its tag could not be told apart.
if (($Beta -or $Dev) -and $detail -notmatch ('^' + [regex]::Escape($short) + '\.\d+\.\d+$')) { throw 'version.txt hat keine Build-ID mit Zaehlern (Zeile 2) - ein Beta-/Dev-Tag braucht sie.' }
$tagVersion = if ($Beta) { "$detail-beta" } elseif ($Dev) { "$detail-dev" } else { $short }
Write-Step "PlatinVault fuer Windows $tagVersion ($channel)"

if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage, $out | Out-Null

# ---- 1. UI --------------------------------------------------------------------------------------------------------------------------
$dist = Join-Path $root 'frontend\dist'
if (-not $SkipFrontend) {
    Push-Location (Join-Path $root 'frontend')
    try {
        # node_modules is reused (a running Vite dev server keeps working); only a fresh checkout installs
        if (-not (Test-Path -LiteralPath 'node_modules')) { $null = Invoke-Native 'npm' @('ci', '--no-fund', '--no-audit') }
        Write-Step 'Baue die Oberflaeche (npm run build) ...'
        $null = Invoke-Native 'npm' @('run', 'build')
    }
    finally { Pop-Location }
}
if (-not (Test-Path -LiteralPath (Join-Path $dist 'index.html'))) { throw "frontend\dist\index.html fehlt (Oberflaeche nicht gebaut)." }

# ---- 2. bridge ----------------------------------------------------------------------------------------------------------------------
$bridgeArgs = @{}
if ($BridgeVersion) { $bridgeArgs['Version'] = $BridgeVersion }
& (Join-Path $PSScriptRoot 'build-bridge.ps1') @bridgeArgs
$bridgeDir = Join-Path $root 'artifacts\bridge'

# ---- 3. program -----------------------------------------------------------------------------------------------------------------------
$app = Join-Path $stage 'PlatinVault'
Write-Step 'Veroeffentliche PlatinVault.Desktop (Release, win-x64, eigenstaendig) ...'
$null = Invoke-Native 'dotnet' @('publish', (Join-Path $root 'src\PlatinVault.Desktop\PlatinVault.Desktop.csproj'),
    '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--artifacts-path', $dotnetArtifacts, '-o', $app, '-p:DebugType=none')
if (-not (Test-Path -LiteralPath (Join-Path $app 'PlatinVault.exe'))) { throw 'PlatinVault.exe wurde nicht erzeugt.' }
# The server's development settings are not part of the program (the program configures the server itself)
foreach ($name in @('appsettings.Development.json')) { $p = Join-Path $app $name; if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Force } }

Write-Step 'Stelle den Programmordner zusammen ...'
Copy-Item -LiteralPath $dist -Destination (Join-Path $app 'wwwroot') -Recurse
foreach ($lang in @('de', 'en')) {
    $target = Join-Path $app "docs\handbuch\$lang"
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    $pdfs = @(Get-ChildItem -LiteralPath (Join-Path $root "docs\handbuch\$lang") -Filter '*.pdf')
    if ($pdfs.Count -eq 0) { throw "Handbuch-PDF ($lang) fehlt - das Handbuch gehoert immer zum Produkt." }
    $pdfs | Copy-Item -Destination $target
}
# Licence: the official GNU AGPL v3 text, copied unchanged (the installer shows it on its licence step, the server serves it)
$utf8 = New-Object System.Text.UTF8Encoding($false)
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $app 'docs\LICENSE')
Copy-Item -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination (Join-Path $app 'docs\THIRD-PARTY-NOTICES.md')
# version.txt keeps the two plain lines (version, build id); the program turns them into the readable form with the channel
[IO.File]::WriteAllText((Join-Path $app 'version.txt'), "$short`n$detail", $utf8)
[IO.File]::WriteAllText((Join-Path $app 'release-channel.txt'), $channel, $utf8)

New-Item -ItemType Directory -Force -Path (Join-Path $app 'bridge') | Out-Null
Copy-Item -LiteralPath (Join-Path $bridgeDir 'PlatinVault.Bridge.exe') -Destination (Join-Path $app 'bridge')
Copy-Item -LiteralPath (Join-Path $bridgeDir 'bridge-version.txt') -Destination (Join-Path $app 'bridge')
if (Test-Path -LiteralPath (Join-Path $bridgeDir 'package')) { Copy-Item -LiteralPath (Join-Path $bridgeDir 'package') -Destination (Join-Path $app 'bridge\package') -Recurse }

# ---- 4. payload + installer ------------------------------------------------------------------------------------------------------------
$zip = Join-Path $cache 'payload.zip'
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Write-Step 'Packe den Programmordner ...'
[IO.Compression.ZipFile]::CreateFromDirectory($app, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)

$setupOut = Join-Path $cache 'setup'
Write-Step 'Veroeffentliche den Installer (PlatinVault.Setup, eine Datei) ...'
$null = Invoke-Native 'dotnet' @('publish', (Join-Path $root 'src\PlatinVault.Setup\PlatinVault.Setup.csproj'),
    '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--artifacts-path', $dotnetArtifacts, '-o', $setupOut)
$setupExe = Join-Path $setupOut 'PlatinVault-Setup.exe'

Get-ChildItem -LiteralPath $out -Filter 'PlatinVault-Setup-*' -ErrorAction SilentlyContinue | Remove-Item -Force
$final = Join-Path $out "PlatinVault-Setup-$tagVersion.exe"
Copy-Item -LiteralPath $setupExe -Destination $final
$null = Invoke-Native $setupExe @('--append-payload', $final, $zip)
$hash = (Get-FileHash -LiteralPath $final -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$final.sha256", "$hash *$(Split-Path -Leaf $final)`n", (New-Object System.Text.ASCIIEncoding))
Copy-Item -LiteralPath (Join-Path $bridgeDir 'PlatinVault.Bridge.exe') -Destination (Join-Path $out 'PlatinVault.Bridge.exe') -Force

$sizeMb = [math]::Round((Get-Item -LiteralPath $final).Length / 1MB, 1)
$appMb = [math]::Round(((Get-ChildItem -LiteralPath $app -Recurse -File | Measure-Object -Sum Length).Sum) / 1MB, 1)
Write-Ok "Fertig: $final ($sizeMb MB; installiert $appMb MB)"
Write-Host "   SHA-256: $hash"
