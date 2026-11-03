<#
  Builds the PlatinVault Bridge (src\PlatinVault.Bridge) as a self-contained single-file WinForms exe for win-x64
  (no .NET installation needed on the gaming PC, no console window; tray icon, setup and status windows).
  The exe is not trimmed (WinForms is not trim-safe) and not compressed (that would halve the size but cost about
  75 MB more RAM for the resident tray program), so it is about 103 MB.

  Output in artifacts\bridge\:
    PlatinVault.Bridge.exe     the program
    bridge-version.txt         just the bridge version (the server reads it to offer self-updates)
    package\                   template the server zips with the exe and a generated bridge.json: LIESMICH.txt

  The bridge has its own version, independent of the app version: 1.0.<n>, where n is the number of commits that
  touched the bridge or its shared Steam reader (so it only changes when the bridge itself changes). Without any
  commit n is 0. For CI/Docker without .git pass -Version 1.0.7 or set the environment variable BRIDGE_VERSION.

  Windows PowerShell 5.1 compatible. Console output is German and ASCII-only.

  Aufruf:  .\scripts\build-bridge.ps1 [-Version 1.0.7]
#>
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'dev-common.ps1')

# Paths the version counts commits for: the bridge itself and the Steam reader it compiles from the server project.
$script:VersionPaths = @('src/PlatinVault.Bridge', 'src/Companion.Api/Services/SteamLocalStatsService.cs')

function Get-BridgeVersion([string]$Explicit) {
    $value = $Explicit
    if (-not $value) { $value = $env:BRIDGE_VERSION }
    if ($value) {
        $value = $value.Trim()
        if ($value -notmatch '^\d+\.\d+\.\d+$') {
            throw "Ungueltige Bridge-Version '$value'. Erwartet wird das Format 1.0.7."
        }
        return $value
    }

    # No .git (CI/Docker) or no commit yet makes git fail: count 0, version 1.0.0.
    $count = 0
    if (Get-Command 'git' -ErrorAction SilentlyContinue) {
        $previous = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $output = & git -C $script:RootDir rev-list --count HEAD -- $script:VersionPaths 2>$null
            if ($LASTEXITCODE -eq 0 -and "$output".Trim() -match '^\d+$') { $count = [int]"$output".Trim() }
        }
        finally { $ErrorActionPreference = $previous }
    }
    return "1.0.$count"
}

$bridgeVersion = Get-BridgeVersion $Version
$outputDir = Join-Path $script:RootDir 'artifacts\bridge'
$exePath = Join-Path $outputDir 'PlatinVault.Bridge.exe'
$packageSource = Join-Path $script:RootDir 'src\PlatinVault.Bridge\package'

Write-Step 'Pruefe dotnet ...'
if (-not (Get-Command 'dotnet' -ErrorAction SilentlyContinue)) {
    throw 'dotnet wurde nicht gefunden. Bitte das .NET 10 SDK installieren: https://dotnet.microsoft.com/download/dotnet/10.0'
}

# Start from an empty folder so files of an earlier build can never end up next to the new exe.
if (Test-Path -LiteralPath $outputDir) {
    Write-Step 'Raeume altes Ausgabeverzeichnis auf ...'
    Remove-Item -LiteralPath $outputDir -Recurse -Force
}

Write-Step "Veroeffentliche PlatinVault.Bridge $bridgeVersion (Release, win-x64, eigenstaendig, WinForms, eine Datei) ..."
Push-Location $script:RootDir
try {
    $null = Invoke-Native 'dotnet' @(
        'publish', 'src/PlatinVault.Bridge',
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        "-p:Version=$bridgeVersion",
        '-o', 'artifacts/bridge'
    )
}
finally {
    Pop-Location
}

if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Die Bridge wurde nicht erzeugt: $exePath fehlt."
}

# Just the version, no newline: the server compares it with what the running bridge reports.
[System.IO.File]::WriteAllText((Join-Path $outputDir 'bridge-version.txt'), $bridgeVersion, (New-Object System.Text.ASCIIEncoding))

if (Test-Path -LiteralPath $packageSource) {
    Write-Step 'Kopiere Paket-Vorlagen (package) ...'
    Copy-Item -LiteralPath $packageSource -Destination (Join-Path $outputDir 'package') -Recurse -Force
}

$sizeMb = [math]::Round((Get-Item -LiteralPath $exePath).Length / 1MB, 1)
Write-Ok "Fertig: Version $bridgeVersion, $sizeMb MB"
Write-Host "   $exePath"
Write-Host ''
Write-Host '   Start: PlatinVault.Bridge.exe doppelklicken - das Einrichtungsfenster fuehrt durch die Installation.'
Write-Host '   Ohne Fenster: PlatinVault.Bridge.exe --no-setup --server http://<SERVER>:<PORT> --token <TOKEN> --save'
Write-Host '   Hinweis: Es wird keine .NET-Installation auf dem Gaming-PC benoetigt.'
