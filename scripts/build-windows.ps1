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

  Stages (-Stage, default "all" = everything in one go, unsigned - the local workflow). The release workflow signs in between
  (.github/workflows/release.yml, job "windows"; SignPath artifact configurations in .signpath\artifact-configurations\):
    app       UI, bridge and the program folder artifacts\windows-cache\stage\PlatinVault. With -PrepareSigning our own binaries (the list
              in .signpath\artifact-configurations\windows-app.xml, nothing third-party) are also copied to
              artifacts\windows-cache\to-sign\app - the artifact that is uploaded for signing.
    setup     needs the program folder of "app": copies the signed binaries from -SignedAppDir over it (when given), packs it, appends it
              to the installer, copies the bridge from the program folder and writes the .sha256.
    checksum  after the installer itself was signed: copies the signed installer from -SignedSetup over the built one, checks that the
              payload is still found, with -RequireSignature checks the Authenticode signatures (installer, PlatinVault.exe, bridge) and
              writes the .sha256 again - always from the final file.
    all       app + setup.

  Version: version.txt of the repository (written by the pre-commit hook), nothing is maintained by hand. A beta passes -Beta: the program
  then shows "v2026.11.12 (13.557 beta-sign)" instead of "v2026.11.12 (13.557)" (the program computes that itself from version.txt and the
  channel in release-channel.txt, see src\PlatinVault.Versioning\VersionFormat.cs). The installer's file name uses the technical form
  without spaces, parentheses or the beta sign: PlatinVault-Setup-v2026.11.12.exe (release) and
  PlatinVault-Setup-v2026.11.12.13.557-beta.exe (beta) - the same string as the git tag, which the program's update check relies on.
  A dev build (commit word "Dev-Build", the user's own test package, never published) passes -Dev: channel "dev", the program shows
  "v2026.11.12 (13.557-dev)", never looks for updates, and the installer is PlatinVault-Setup-v2026.11.12.13.557-dev.exe.
  -Beta and -Dev exclude each other. Every stage of one build needs the same -Beta/-Dev.

  File properties of every binary we build: product name "PlatinVault", product version = the readable version of the build (the same
  for all files, e.g. "v2026.11.12 (13.557)"; -ProductVersion, otherwise computed with scripts\version-display.sh through Git's sh),
  file version = the numeric form of version.txt line 1 (2026.11.12.0; the bridge keeps its own 1.0.<n>). See Directory.Build.props.

  Windows PowerShell 5.1 compatible. Console output is German and ASCII-only.

  Aufruf:  .\scripts\build-windows.ps1 [-Stage all|app|setup|checksum] [-Beta | -Dev] [-BridgeVersion 1.0.7] [-SkipFrontend]
           [-ProductVersion '<lesbare Version>'] [-PrepareSigning] [-SignedAppDir <Ordner>] [-SignedSetup <Datei|Ordner>]
           [-RequireSignature]
#>
param(
    [ValidateSet('all', 'app', 'setup', 'checksum')]
    [string]$Stage = 'all',
    [switch]$Beta,
    [switch]$Dev,
    [string]$BridgeVersion,
    [string]$ProductVersion,
    [switch]$SkipFrontend,
    [switch]$PrepareSigning,
    [string]$SignedAppDir,
    [string]$SignedSetup,
    [switch]$RequireSignature
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'dev-common.ps1')
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = $script:RootDir
$out = Join-Path $root 'artifacts\windows'
$cache = Join-Path $root 'artifacts\windows-cache'
$stageDir = Join-Path $cache 'stage'
$app = Join-Path $stageDir 'PlatinVault'
$toSign = Join-Path $cache 'to-sign\app'
$dotnetArtifacts = Join-Path $cache 'dotnet'
$appSigningConfig = Join-Path $root '.signpath\artifact-configurations\windows-app.xml'

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
$final = Join-Path $out "PlatinVault-Setup-$tagVersion.exe"

# File version: version.txt line 1 as four numbers (Windows allows at most 65535 per part)
function Get-NumericFileVersion([string]$Version) {
    if ($Version -notmatch '^v?(\d+)\.(\d+)\.(\d+)$') { throw "version.txt Zeile 1 '$Version' hat nicht die Form vJahr.Monat.Commit." }
    $parts = @([int]$Matches[1], [int]$Matches[2], [int]$Matches[3], 0)
    foreach ($part in $parts) { if ($part -gt 65535) { throw "Dateiversion aus '$Version': $part ist groesser als 65535." } }
    return ($parts -join '.')
}

# Git's sh (the same one that runs the pre-commit hook); on PATH, or next to git.exe of Git for Windows
function Find-PosixShell {
    $cmd = Get-Command 'sh' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($cmd) { return $cmd.Source }
    $git = Get-Command 'git' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($git) {
        $dir = Split-Path -Parent $git.Source
        foreach ($gitRoot in @((Split-Path -Parent $dir), (Split-Path -Parent (Split-Path -Parent $dir)))) {
            foreach ($rel in @('bin\sh.exe', 'usr\bin\sh.exe')) {
                $candidate = Join-Path $gitRoot $rel
                if (Test-Path -LiteralPath $candidate) { return $candidate }
            }
        }
    }
    return $null
}

# The readable version ("v2026.11.12 (13.557)", beta "... beta-sign)", dev "...-dev)") from the one shell definition of it
function Get-ReadableVersion {
    $sh = Find-PosixShell
    if (-not $sh) { throw 'sh (Git for Windows) wurde nicht gefunden - ohne -ProductVersion wird die lesbare Version mit scripts/version-display.sh gebildet.' }
    $scriptPath = (Join-Path $PSScriptRoot 'version-display.sh') -replace '\\', '/'
    $previous = $null
    try { $previous = [Console]::OutputEncoding; [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { $previous = $null }
    try { $text = & $sh $scriptPath display $short $detail $channel }
    finally { if ($previous) { [Console]::OutputEncoding = $previous } }
    if ($LASTEXITCODE -ne 0 -or -not "$text".Trim()) { throw 'scripts/version-display.sh ist fehlgeschlagen.' }
    return "$text".Trim()
}

# Relative paths (Windows separators) of the binaries the stage-1 artifact configuration signs - the XML is the single list
function Get-AppSigningList {
    if (-not (Test-Path -LiteralPath $appSigningConfig)) { throw "Signatur-Konfiguration fehlt: $appSigningConfig" }
    [xml]$doc = Get-Content -LiteralPath $appSigningConfig -Raw -Encoding UTF8
    $zipNode = @($doc.DocumentElement.ChildNodes | Where-Object { $_.LocalName -eq 'zip-file' })
    if ($zipNode.Count -ne 1) { throw 'windows-app.xml: genau ein <zip-file> erwartet.' }
    $list = New-Object System.Collections.Generic.List[string]
    Add-SigningEntries $zipNode[0] '' $list
    if ($list.Count -eq 0) { throw 'windows-app.xml nennt keine Datei.' }
    return , $list
}
function Add-SigningEntries($Node, [string]$Prefix, $List) {
    foreach ($child in @($Node.ChildNodes | Where-Object { $_.NodeType -eq 'Element' })) {
        $path = "$($child.GetAttribute('path'))"
        if ($path -match '[\*\?\[]') { throw "windows-app.xml: '$path' - Platzhalter sind hier nicht erlaubt (jede Datei einzeln nennen)." }
        $full = if ($Prefix) { "$Prefix\$($path -replace '/', '\')" } else { $path -replace '/', '\' }
        if ($child.LocalName -eq 'directory') { Add-SigningEntries $child $full $List }
        elseif ($child.LocalName -eq 'pe-file') { $List.Add($full) }
    }
}

# The SignPath action extracts the signed ZIP into its output folder; if only a ZIP is there, extract it first
function Resolve-SignedFolder([string]$Folder, [string]$Probe) {
    if (-not (Test-Path -LiteralPath $Folder -PathType Container)) { throw "Ordner mit signierten Dateien fehlt: $Folder" }
    if ($Probe -and (Test-Path -LiteralPath (Join-Path $Folder $Probe))) { return $Folder }
    $zips = @(Get-ChildItem -LiteralPath $Folder -Filter '*.zip' -File)
    if ($zips.Count -eq 1) {
        $extracted = Join-Path $Folder '_extracted'
        if (Test-Path -LiteralPath $extracted) { Remove-Item -LiteralPath $extracted -Recurse -Force }
        [IO.Compression.ZipFile]::ExtractToDirectory($zips[0].FullName, $extracted)
        return $extracted
    }
    return $Folder
}

# The installer finds its payload by the trailer "PVPAYLD1" in its last 64 KiB (src\PlatinVault.Setup\Payload.cs) - a signature must not push it out
function Assert-PayloadTrailer([string]$Exe) {
    $magic = [Text.Encoding]::ASCII.GetBytes('PVPAYLD1')
    $stream = [IO.File]::OpenRead($Exe)
    try {
        $window = [int][Math]::Min($stream.Length, 64KB)
        $buffer = New-Object byte[] $window
        $null = $stream.Seek(-$window, [IO.SeekOrigin]::End)
        $read = 0
        while ($read -lt $window) { $n = $stream.Read($buffer, $read, $window - $read); if ($n -le 0) { break }; $read += $n }
    }
    finally { $stream.Dispose() }
    # Same rule as Payload.FromExe: the last "PVPAYLD1" whose 56-byte trailer fits into the window
    for ($i = $window - 56; $i -ge 0; $i--) {
        $hit = $true
        for ($j = 0; $j -lt $magic.Length; $j++) { if ($buffer[$i + $j] -ne $magic[$j]) { $hit = $false; break } }
        if ($hit) { return }
    }
    throw "Im Installer wurde das angehaengte Programm (PVPAYLD1) in den letzten 64 KiB nicht gefunden: $Exe"
}

function Write-Checksum([string]$Exe) {
    $hash = (Get-FileHash -LiteralPath $Exe -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$Exe.sha256", "$hash *$(Split-Path -Leaf $Exe)`n", (New-Object System.Text.ASCIIEncoding))
    return $hash
}

function Assert-ProgramFolder {
    if (-not (Test-Path -LiteralPath (Join-Path $app 'PlatinVault.exe'))) { throw "Programmordner fehlt ($app) - zuerst -Stage app bauen." }
    $builtVersion = ([IO.File]::ReadAllText((Join-Path $app 'version.txt'))).Trim() -replace "`r", ''
    $builtChannel = ([IO.File]::ReadAllText((Join-Path $app 'release-channel.txt'))).Trim()
    if ($builtVersion -ne "$short`n$detail" -or $builtChannel -ne $channel) {
        throw "Der Programmordner gehoert zu einem anderen Build (Kanal '$builtChannel') - alle Stufen mit derselben version.txt und denselben Schaltern (-Beta/-Dev) bauen."
    }
}

# Folders/files from the command line as full paths (.NET calls resolve relative paths against the process folder, not PowerShell's)
if ($SignedAppDir) { $SignedAppDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($SignedAppDir) }
if ($SignedSetup) { $SignedSetup = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($SignedSetup) }

$needsVersionInfo = $Stage -in @('all', 'app', 'setup')
if ($needsVersionInfo) {
    if (-not $ProductVersion) { $ProductVersion = Get-ReadableVersion }
    $ProductVersion = $ProductVersion.Trim()
    $fileVersion = Get-NumericFileVersion $short
    # Global properties for every project of the build (Directory.Build.props turns them into the file properties)
    $versionProps = @("-p:PvProductVersion=$ProductVersion", "-p:PvFileVersion=$fileVersion")
}
Write-Step "PlatinVault fuer Windows $tagVersion ($channel), Stufe '$Stage'"
New-Item -ItemType Directory -Force -Path $out | Out-Null

# =====================================================================================================================================
# Stage "app": UI, bridge, program folder
# =====================================================================================================================================
if ($Stage -in @('all', 'app')) {
    if (Test-Path -LiteralPath $stageDir) { Remove-Item -LiteralPath $stageDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $stageDir | Out-Null
    # An installer of an earlier build must never be mistaken for this one
    Get-ChildItem -LiteralPath $out -Filter 'PlatinVault-Setup-*' -ErrorAction SilentlyContinue | Remove-Item -Force

    # ---- 1. UI ----------------------------------------------------------------------------------------------------------------------
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

    # ---- 2. bridge ------------------------------------------------------------------------------------------------------------------
    $bridgeArgs = @{ ProductVersion = $ProductVersion }
    if ($BridgeVersion) { $bridgeArgs['Version'] = $BridgeVersion }
    & (Join-Path $PSScriptRoot 'build-bridge.ps1') @bridgeArgs
    $bridgeDir = Join-Path $root 'artifacts\bridge'

    # ---- 3. program -----------------------------------------------------------------------------------------------------------------
    Write-Step "Veroeffentliche PlatinVault.Desktop (Release, win-x64, eigenstaendig; Dateiversion $fileVersion) ..."
    $null = Invoke-Native 'dotnet' (@('publish', (Join-Path $root 'src\PlatinVault.Desktop\PlatinVault.Desktop.csproj'),
        '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--artifacts-path', $dotnetArtifacts, '-o', $app, '-p:DebugType=none') + $versionProps)
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
    # The customer changelog: the "What's new" window after an update reads the entry of the running version from it (works offline)
    Copy-Item -LiteralPath (Join-Path $root 'CHANGELOG.md') -Destination (Join-Path $app 'docs\CHANGELOG.md')
    # version.txt keeps the two plain lines (version, build id); the program turns them into the readable form with the channel
    [IO.File]::WriteAllText((Join-Path $app 'version.txt'), "$short`n$detail", $utf8)
    [IO.File]::WriteAllText((Join-Path $app 'release-channel.txt'), $channel, $utf8)

    New-Item -ItemType Directory -Force -Path (Join-Path $app 'bridge') | Out-Null
    Copy-Item -LiteralPath (Join-Path $bridgeDir 'PlatinVault.Bridge.exe') -Destination (Join-Path $app 'bridge')
    Copy-Item -LiteralPath (Join-Path $bridgeDir 'bridge-version.txt') -Destination (Join-Path $app 'bridge')
    if (Test-Path -LiteralPath (Join-Path $bridgeDir 'package')) { Copy-Item -LiteralPath (Join-Path $bridgeDir 'package') -Destination (Join-Path $app 'bridge\package') -Recurse }

    if ($PrepareSigning) {
        # Only our own binaries go to the signing service (third-party files stay as they are)
        if (Test-Path -LiteralPath $toSign) { Remove-Item -LiteralPath $toSign -Recurse -Force }
        foreach ($rel in (Get-AppSigningList)) {
            $source = Join-Path $app $rel
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Zu signierende Datei fehlt im Programmordner: $rel" }
            $target = Join-Path $toSign $rel
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
            Copy-Item -LiteralPath $source -Destination $target
        }
        Write-Ok "Zum Signieren vorbereitet: $toSign"
    }
    Write-Ok "Programmordner fertig: $app"
}

# =====================================================================================================================================
# Stage "setup": (signed binaries back into the program folder,) payload, installer, bridge download, checksum
# =====================================================================================================================================
if ($Stage -in @('all', 'setup')) {
    Assert-ProgramFolder

    if ($SignedAppDir) {
        $signedRoot = Resolve-SignedFolder $SignedAppDir 'PlatinVault.exe'
        Write-Step "Uebernehme die signierten Dateien aus $signedRoot ..."
        foreach ($rel in (Get-AppSigningList)) {
            $source = Join-Path $signedRoot $rel
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Signierte Datei fehlt: $rel" }
            Copy-Item -LiteralPath $source -Destination (Join-Path $app $rel) -Force
            Write-Ok "signiert: $rel"
        }
    }

    $zip = Join-Path $cache 'payload.zip'
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    Write-Step 'Packe den Programmordner ...'
    [IO.Compression.ZipFile]::CreateFromDirectory($app, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)

    $setupOut = Join-Path $cache 'setup'
    Write-Step 'Veroeffentliche den Installer (PlatinVault.Setup, eine Datei) ...'
    $null = Invoke-Native 'dotnet' (@('publish', (Join-Path $root 'src\PlatinVault.Setup\PlatinVault.Setup.csproj'),
        '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--artifacts-path', $dotnetArtifacts, '-o', $setupOut) + $versionProps)
    $setupExe = Join-Path $setupOut 'PlatinVault-Setup.exe'

    Get-ChildItem -LiteralPath $out -Filter 'PlatinVault-Setup-*' -ErrorAction SilentlyContinue | Remove-Item -Force
    Copy-Item -LiteralPath $setupExe -Destination $final
    # Build mode of the setup exe: only appends the ZIP plus trailer to the given file, installs nothing
    $null = Invoke-Native $setupExe @('--append-payload', $final, $zip)
    Assert-PayloadTrailer $final
    # The bridge download is the very file inside the program (signed there when the release is signed)
    Copy-Item -LiteralPath (Join-Path $app 'bridge\PlatinVault.Bridge.exe') -Destination (Join-Path $out 'PlatinVault.Bridge.exe') -Force
    $hash = Write-Checksum $final

    $sizeMb = [math]::Round((Get-Item -LiteralPath $final).Length / 1MB, 1)
    $appMb = [math]::Round(((Get-ChildItem -LiteralPath $app -Recurse -File | Measure-Object -Sum Length).Sum) / 1MB, 1)
    Write-Ok "Fertig: $final ($sizeMb MB; installiert $appMb MB)"
    Write-Host "   Produktversion: $ProductVersion, Dateiversion: $fileVersion"
    Write-Host "   SHA-256: $hash"
}

# =====================================================================================================================================
# Stage "checksum": the signed installer replaces the built one; signatures checked; .sha256 from the final file
# =====================================================================================================================================
if ($Stage -eq 'checksum') {
    if (-not (Test-Path -LiteralPath $final)) { throw "Installer fehlt ($final) - zuerst -Stage setup bauen." }
    if ($SignedSetup) {
        $signedFile = $SignedSetup
        if (Test-Path -LiteralPath $SignedSetup -PathType Container) {
            $folder = Resolve-SignedFolder $SignedSetup (Split-Path -Leaf $final)
            $signedFile = Join-Path $folder (Split-Path -Leaf $final)
        }
        if (-not (Test-Path -LiteralPath $signedFile -PathType Leaf)) { throw "Signierter Installer fehlt: $signedFile" }
        Copy-Item -LiteralPath $signedFile -Destination $final -Force
        Write-Ok "Signierter Installer uebernommen: $(Split-Path -Leaf $final)"
    }
    Assert-PayloadTrailer $final

    if ($RequireSignature) {
        Write-Step 'Pruefe die Signaturen (Authenticode) ...'
        foreach ($file in @($final, (Join-Path $app 'PlatinVault.exe'), (Join-Path $out 'PlatinVault.Bridge.exe'))) {
            if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Datei fehlt: $file" }
            $signature = Get-AuthenticodeSignature -LiteralPath $file
            $subject = if ($signature.SignerCertificate) { $signature.SignerCertificate.Subject } else { '-' }
            if ($signature.Status -ne 'Valid') { throw "Signatur von $(Split-Path -Leaf $file) ist nicht gueltig: $($signature.Status) ($($signature.StatusMessage))" }
            Write-Ok "$(Split-Path -Leaf $file): Valid - $subject"
        }
    }
    $hash = Write-Checksum $final
    Write-Ok "SHA-256 ($(Split-Path -Leaf $final)): $hash"
}
