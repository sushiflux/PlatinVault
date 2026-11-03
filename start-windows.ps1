<#
  PlatinVault - development environment of the NATIVE WINDOWS PROGRAM (PlatinVault.exe: WinForms + WebView2, the server inside the
  program). The browser development stack stays in .\start.ps1 - both can run at the same time.

  SAME DATA AS THE BROWSER STACK (default): the program works on the browser stack's SQLite file (.data\dev\platinvault.db - both servers
  open it at the same time, SQLite in WAL mode on one local disk) and on its key ring + encrypted API keys (.data\dev\dp-keys,
  .data\dev\secrets). Games, guides, ticks, settings and Steam/AI keys are therefore identical in both and never set up twice. Caches stay
  in the program's own folder (.data\desktop-dev\server). Backups: both servers back up into .backups\auto (a lock file and the change
  check keep that free of duplicates). No Docker needed.
  .\start-windows.ps1 -OwnDb          isolated mode: the program's own database file in .data\desktop-dev\database (own, empty data, no keys)
                                       and its own backups in .backups\desktop-dev - the path the installed program uses.

  .\start-windows.ps1                  start (default): builds src\PlatinVault.Desktop (Debug) and runs it in DEVELOPMENT MODE, plus a
                                       second Vite server for hot reload. Any key or Ctrl+C in this window stops everything cleanly.
  .\start-windows.ps1 -Detached        start in the background (Vite log in .logs\) and return immediately.
  .\start-windows.ps1 -Built           no Vite: the program serves the built UI (artifacts\desktop-dev\wwwroot) like the installed one.
  .\start-windows.ps1 -Offscreen       nothing on the real desktop: no visible window, no tray icon, no hotkey (automated checks).
  .\start-windows.ps1 -Hotkey          also register the global hotkey (off in development so it never takes the installed program's).
  .\start-windows.ps1 -SkipBuild       reuse the last build of the program.
  .\start-windows.ps1 stop             the program exits (its server makes a last backup); then Vite is stopped.
  .\start-windows.ps1 restart          stop + start
  .\start-windows.ps1 status           program, ports, data folder, database file, version, backups
  .\start-windows.ps1 logs [name]      logs: desktop (default) | server | frontend | build

  Development mode (PlatinVault.exe --dev <repository>): own data folder .data\desktop-dev (WebView profile, logs, caches; -OwnDb: also
  its own database file + backups in .backups\desktop-dev), no registry/autostart/Start menu/uninstall entries, no update check, own
  single-instance scope. Nothing of an installed PlatinVault is touched; of the browser stack only its database file and (shared mode) its
  keys are used - its processes (:5230/:5231) are never started or stopped here.

  Ports are fixed: program server 5234, its Vite 5236.
  A busy port stops the start with a clear message - it is never replaced.

  Windows PowerShell 5.1 compatible. Console output is German and ASCII-only.
#>
param(
    [Parameter(Position = 0)][ValidateSet('start', 'stop', 'restart', 'status', 'logs')][string]$Command = 'start',
    [Parameter(Position = 1)][string]$Service,
    [switch]$Detached,
    [switch]$Built,
    [switch]$Offscreen,
    [switch]$Hotkey,
    [switch]$SkipBuild,
    [switch]$OwnDb
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts\dev-common.ps1')
Set-Location $RootDir

# Fixed ports of this project (never chosen at run time; a busy port stops the start with a clear message).
$DesktopPorts = [ordered]@{
    backend  = 5234   # the server inside PlatinVault.exe
    frontend = 5236   # Vite for hot reload
}

$DataDir = Join-Path $RootDir '.data\desktop-dev'
$OwnBackupDir = Join-Path $RootDir '.backups\desktop-dev'
$AppLogDir = Join-Path $DataDir 'logs'
$LogDir = Join-Path $RootDir '.logs'
$ArtifactsDir = Join-Path $RootDir 'artifacts\desktop-dev'
$DotnetArtifacts = Join-Path $ArtifactsDir 'dotnet'
$WebRootDir = Join-Path $ArtifactsDir 'wwwroot'
$ViteConfig = 'vite.desktop.config.ts'
$BackendUrl = "http://127.0.0.1:$($DesktopPorts.backend)"
$UiUrl = "http://127.0.0.1:$($DesktopPorts.frontend)/"

# ---- processes ----------------------------------------------------------------------------------------------------------------------------

function Get-CommandLineMatches([string]$Name, [string[]]$Fragments) {
    Get-CimInstance Win32_Process -Filter "Name='$Name'" -ErrorAction SilentlyContinue | Where-Object {
        $line = if ($_.CommandLine) { $_.CommandLine.ToLowerInvariant() } else { '' }
        $all = $true
        foreach ($fragment in $Fragments) { if (-not $line.Contains($fragment.ToLowerInvariant())) { $all = $false } }
        $all
    }
}

# PlatinVault.exe started with --dev <this repository>. The installed program (no --dev) and the bridge never match.
function Get-DesktopAppProcess { return @(Get-CommandLineMatches 'PlatinVault.exe' @('--dev', $RootDir)) }

function Get-ViteProcesses {
    $found = @()
    foreach ($name in @('node.exe', 'cmd.exe')) { $found += @(Get-CommandLineMatches $name @($ViteConfig)) }
    return $found
}

function Get-OwnerText([int]$Port) {
    $owner = Get-PortProcess $Port
    if ($owner) { return "'$($owner.ProcessName)' (PID $($owner.Id))" }
    return $null
}

# Shared mode is the default; -OwnDb selects the program's own database file. A RUNNING program is asked what it was started with
# (its command line), so status/stop talk about the right mode even when the switch is not repeated.
function Test-SharedMode {
    $apps = @(Get-DesktopAppProcess)
    if ($apps.Count -gt 0 -and $apps[0].CommandLine) { return $apps[0].CommandLine.ToLowerInvariant().Contains('--shared-data-dir') }
    return (-not $OwnDb)
}

function Get-DesktopDatabaseFile([bool]$Shared) {
    if ($Shared) { return (Get-DatabaseFile 'dev') }
    return (Join-Path $DataDir 'database\platinvault.db')
}

function Get-DesktopBackupDir([bool]$Shared) {
    if ($Shared) { return (Get-BackupDir) }
    return $OwnBackupDir
}

function Stop-DesktopVite {
    foreach ($process in @(Get-ViteProcesses)) { Stop-ProcessTree $process.ProcessId }
}

# Starts a hidden process through WMI so it is not part of this console's job object and keeps running after the script has exited.
# WMI processes do not inherit this session's environment, so the variables are set inside the cmd.exe command line.
function Start-HiddenProcess([string]$CommandText, [string]$WorkingDirectory, [System.Collections.IDictionary]$Environment, [string]$LogName) {
    $prefix = ''
    if ($Environment) {
        foreach ($key in $Environment.Keys) { $prefix += "set `"$key=$($Environment[$key])`"&& " }
    }
    New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
    $commandLine = "cmd.exe /d /c `"$prefix$CommandText > `"$LogDir\$LogName.log`" 2> `"$LogDir\$LogName.err.log`"`""
    $startup = New-CimInstance -ClassName Win32_ProcessStartup -ClientOnly -Property @{ ShowWindow = [uint16]0 }
    $result = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
        CommandLine = $commandLine; CurrentDirectory = $WorkingDirectory; ProcessStartupInformation = $startup
    }
    if ($result.ReturnValue -ne 0) { throw "$LogName konnte nicht gestartet werden (WMI-Code $($result.ReturnValue))." }
}

# ---- build ---------------------------------------------------------------------------------------------------------------------------------

function Find-DesktopExe {
    $bin = Join-Path $DotnetArtifacts 'bin\PlatinVault.Desktop'
    if (-not (Test-Path -LiteralPath $bin)) { return $null }
    $exe = Get-ChildItem -LiteralPath $bin -Recurse -Filter 'PlatinVault.exe' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($exe) { return $exe.FullName }
    return $null
}

# Own --artifacts-path: the browser stack's "dotnet watch" locks src\Companion.Api\bin and obj, which a normal build would fight over.
function Build-Desktop {
    Write-Step 'Baue PlatinVault.Desktop (Debug) ...'
    New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
    $log = Join-Path $LogDir 'desktop-build.log'
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & dotnet build (Join-Path $RootDir 'src\PlatinVault.Desktop\PlatinVault.Desktop.csproj') -c Debug --artifacts-path $DotnetArtifacts -nologo -v:q 2>&1 | ForEach-Object { "$_" }
        $code = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }
    [IO.File]::WriteAllText($log, ($output -join "`r`n"))
    if ($code -ne 0) {
        $output | Where-Object { $_ -match 'error' } | Select-Object -First 20 | ForEach-Object { Write-Host "   $_" -ForegroundColor Red }
        throw "Der Build von PlatinVault.Desktop ist fehlgeschlagen (Exit Code $code, vollstaendige Ausgabe: .logs\desktop-build.log)."
    }
    Write-Ok 'Build fertig.'
}

function Initialize-Frontend {
    $frontend = Join-Path $RootDir 'frontend'
    if (-not (Test-Path -LiteralPath (Join-Path $frontend 'node_modules'))) {
        Write-Step 'Installiere Frontend-Abhaengigkeiten (npm install) ...'
        Push-Location $frontend
        try { $null = Invoke-Native 'npm.cmd' @('install', '--no-fund', '--no-audit') } finally { Pop-Location }
    }
}

function Build-Frontend {
    Write-Step 'Baue die Oberflaeche fuer das Programm (vite build) ...'
    # Only "vite build" (no "tsc -b" first): the type check belongs to npm run typecheck, a half-finished edit elsewhere in
    # the UI must not keep the program from starting. Own output folder, frontend\dist (used by the installer build) stays untouched.
    Push-Location (Join-Path $RootDir 'frontend')
    try { $null = Invoke-Native 'node' @('node_modules\vite\bin\vite.js', 'build', '--outDir', $WebRootDir, '--emptyOutDir') } finally { Pop-Location }
    if (-not (Test-Path -LiteralPath (Join-Path $WebRootDir 'index.html'))) { throw "Die gebaute Oberflaeche fehlt ($WebRootDir\index.html)." }
}

# ---- start ---------------------------------------------------------------------------------------------------------------------------

function Assert-PortsFree {
    $checks = @(,@('Server', $DesktopPorts.backend))
    if (-not $Built) { $checks += , @('Vite', $DesktopPorts.frontend) }
    foreach ($check in $checks) {
        $text = Get-OwnerText $check[1]
        if ($text) {
            throw "Port $($check[1]) ($($check[0]) des Windows-Programms) ist belegt von $text. Bitte diesen Prozess beenden - die Ports sind fest vergeben und werden nie ersetzt."
        }
    }
}

function Get-AppArguments {
    $arguments = @('--dev', $RootDir, '--server-port', "$($DesktopPorts.backend)")
    if (-not $OwnDb) {
        # Shared mode: the browser stack's data folder supplies the database file, the key ring and the encrypted API keys; backups go to
        # the browser stack's folder (.backups\auto)
        $arguments += @('--shared-data-dir', (Get-DataDir 'dev'), '--backups-dir', (Get-BackupDir))
    }
    if ($Built) { $arguments += @('--web-root', $WebRootDir) } else { $arguments += @('--ui-url', $UiUrl) }
    if ($Offscreen) { $arguments += '--offscreen' }
    if ($Hotkey) { $arguments += '--dev-hotkey' }
    return $arguments
}

function ConvertTo-ArgumentString([string[]]$Arguments) {
    return (($Arguments | ForEach-Object { if ($_ -match '[\s"]') { '"' + $_.Replace('"', '\"') + '"' } else { $_ } }) -join ' ')
}

function Start-Vite {
    Write-Step "Starte Vite fuer das Windows-Programm auf Port $($DesktopPorts.frontend) ..."
    $environment = [ordered]@{
        PV_DESKTOP_VITE_PORT   = "$($DesktopPorts.frontend)"
        PV_DESKTOP_BACKEND_URL = $BackendUrl
    }
    Start-HiddenProcess "npm.cmd run dev -- --config $ViteConfig" (Join-Path $RootDir 'frontend') $environment 'desktop-frontend'
}

function Wait-ForApp {
    Write-Step 'Warte auf Server und Oberflaeche (beim ersten Start legt PlatinVault die Datenbank an) ...'
    $deadline = (Get-Date).AddSeconds(300)
    $serverUp = $false
    $viteUp = $Built
    while ((Get-Date) -lt $deadline -and -not ($serverUp -and $viteUp)) {
        Start-Sleep -Milliseconds 750
        if (@(Get-DesktopAppProcess).Count -eq 0) {
            Write-Warn 'PlatinVault.exe hat sich beendet. Letzte Zeilen des Protokolls:'
            Show-LogTail (Join-Path $AppLogDir 'desktop.log') 15
            return $false
        }
        if (-not $serverUp) {
            try { $null = Invoke-RestMethod -Uri "$BackendUrl/health" -TimeoutSec 3; $serverUp = $true } catch { $serverUp = $false }
        }
        if (-not $viteUp) { $viteUp = [bool](Get-PortProcess $DesktopPorts.frontend) }
    }
    if (-not $serverUp) { Write-Warn 'Der Server antwortet noch nicht - Protokoll pruefen (.\start-windows.ps1 logs).' }
    if (-not $viteUp) { Write-Warn 'Vite antwortet noch nicht - Protokoll pruefen (.\start-windows.ps1 logs frontend).' }
    return ($serverUp -and $viteUp)
}

function Start-Everything {
    Write-Host 'Starte PlatinVault - Windows-Programm (Entwicklung) ...' -ForegroundColor Cyan

    if (@(Get-DesktopAppProcess).Count -gt 0) {
        Write-Warn 'Das Windows-Programm laeuft bereits (.\start-windows.ps1 status, zum Neustart: .\start-windows.ps1 restart).'
        $script:AlreadyRunning = $true
        return
    }
    # Own leftovers of an earlier run that ended badly (a crash, a killed window): cleaned up, never anything else
    Stop-DesktopVite
    Assert-PortsFree

    if (-not $SkipBuild -or -not (Find-DesktopExe)) { Build-Desktop }
    $exe = Find-DesktopExe
    if (-not $exe) { throw 'PlatinVault.exe wurde nicht gefunden (Build?).' }
    Initialize-Frontend
    if ($Built) { Build-Frontend } else { Start-Vite }

    $shared = -not $OwnDb
    New-Item -ItemType Directory -Force -Path $DataDir, (Get-DesktopBackupDir $shared) | Out-Null
    if ($shared) { New-Item -ItemType Directory -Force -Path (Get-DataDir 'dev') | Out-Null }
    Write-Step 'Starte PlatinVault.exe (Entwicklungsmodus) ...'
    $script:AppProcess = Start-Process -FilePath $exe -ArgumentList (ConvertTo-ArgumentString (Get-AppArguments)) -WorkingDirectory (Split-Path -Parent $exe) -PassThru
    $ready = Wait-ForApp

    Write-Host ''
    Write-Host '========================================================' -ForegroundColor Green
    Write-Host "  Programm-Server: $BackendUrl  (Health: /health)" -ForegroundColor Green
    if ($Built) { Write-Host "  Oberflaeche:     gebaut, vom Programm-Server ($BackendUrl)" -ForegroundColor Green }
    else { Write-Host "  Oberflaeche:     Vite mit Hot Reload ($UiUrl)" -ForegroundColor Green }
    Write-Host "  Datenordner:     $DataDir (Caches, Protokolle, Fenster)" -ForegroundColor Green
    if ($shared) { Write-Host "  Datenbank:       GEMEINSAM mit dem Browser-Stack: $(Get-DesktopDatabaseFile $true), API-Keys aus .data\dev" -ForegroundColor Green }
    else { Write-Host "  Datenbank:       EIGENE Datei $(Get-DesktopDatabaseFile $false)" -ForegroundColor Green }
    Write-Host "  Sicherungen:     $(Get-DesktopBackupDir $shared)" -ForegroundColor Green
    if (-not $ready) { Write-Host '  Achtung: nicht alles hat rechtzeitig geantwortet.' -ForegroundColor Yellow }
    Write-Host '========================================================' -ForegroundColor Green
}

# ---- stop ----------------------------------------------------------------------------------------------------------------------------

function Stop-Everything {
    Write-Host "`nFahre PlatinVault (Windows-Programm, Entwicklung) herunter ..." -ForegroundColor Yellow
    $apps = @(Get-DesktopAppProcess)
    if ($apps.Count -gt 0) {
        Write-Step 'Das Programm sichert die Datenbank und beendet sich ...'
        $exe = $apps[0].ExecutablePath
        if (-not $exe) { $exe = Find-DesktopExe }
        if ($exe -and (Test-Path -LiteralPath $exe)) {
            $temp = [IO.Path]::GetTempFileName()
            try {
                $quit = Start-Process -FilePath $exe -ArgumentList (ConvertTo-ArgumentString @('--dev', $RootDir, '--quit')) -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput $temp
                $text = (Get-Content -LiteralPath $temp -Raw -ErrorAction SilentlyContinue)
                if ($text) { Write-Ok $text.Trim() }
                if ($quit.ExitCode -ne 0) { Write-Warn "Das Programm hat sich nicht rechtzeitig beendet (Exit Code $($quit.ExitCode))." }
            }
            finally { Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue }
        }
        foreach ($app in @(Get-DesktopAppProcess)) {
            Write-Warn "PlatinVault.exe (PID $($app.ProcessId)) laeuft noch - wird beendet (SQLite stellt beim naechsten Start den letzten vollstaendigen Stand her)."
            Stop-ProcessTree $app.ProcessId
        }
    }
    Write-Step 'Beende Vite ...'
    Stop-DesktopVite
    Write-Host 'Alles sauber beendet.' -ForegroundColor Green
}

# ---- status / logs ---------------------------------------------------------------------------------------------------------------------

function Show-LogTail([string]$Path, [int]$Lines) {
    if (Test-Path -LiteralPath $Path) { Get-Content -LiteralPath $Path -Tail $Lines -Encoding UTF8 | ForEach-Object { Write-Host "   $_" } }
    else { Write-Host '   (keine Protokolldatei)' }
}

function Show-Status {
    Write-Host 'PlatinVault - Windows-Programm (Entwicklung)' -ForegroundColor Cyan
    $versionFile = Join-Path $RootDir 'version.txt'
    if (Test-Path -LiteralPath $versionFile) {
        $lines = @(Get-Content -LiteralPath $versionFile | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        $shown = if ($lines.Count -gt 1) { $lines[1] } elseif ($lines.Count -eq 1) { $lines[0] } else { '?' }
        Write-Host ("  Version:      {0}-dev" -f $shown)
    }
    $apps = @(Get-DesktopAppProcess)
    if ($apps.Count -gt 0) {
        $app = Get-Process -Id $apps[0].ProcessId -ErrorAction SilentlyContinue
        $title = if ($app -and $app.MainWindowTitle) { ", Fenster '$($app.MainWindowTitle)'" } else { '' }
        Write-Host ("  Programm:     laeuft (PlatinVault.exe, PID {0}{1})" -f $apps[0].ProcessId, $title)
    }
    else { Write-Host '  Programm:     gestoppt' }
    $names = [ordered]@{ backend = 'Server'; frontend = 'Vite' }
    foreach ($key in $names.Keys) {
        $port = $DesktopPorts[$key]
        $owner = Get-OwnerText $port
        $state = if ($owner) { "laeuft ($owner)" } else { 'gestoppt' }
        Write-Host ("  {0,-12}  Port {1}: {2}" -f $names[$key], $port, $state)
    }
    try {
        $health = Invoke-RestMethod -Uri "$BackendUrl/health" -TimeoutSec 5
        Write-Host ("  Health:       {0} (Datenbank: {1})" -f $health.status, $health.database)
    }
    catch { Write-Host '  Health:       Server nicht erreichbar' }

    $shared = Test-SharedMode
    Write-Host "  Datenordner:  $DataDir"
    $db = Get-DesktopDatabaseFile $shared
    $mode = if ($shared) { 'GEMEINSAM mit dem Browser-Stack' } else { 'EIGENE Datei (-OwnDb)' }
    if (Test-Path -LiteralPath $db) { Write-Host ("  Datenbank:    {0}: {1} ({2:N0} KB)" -f $mode, $db, ((Get-Item -LiteralPath $db).Length / 1KB)) }
    else { Write-Host ("  Datenbank:    {0}: {1} (noch nicht angelegt)" -f $mode, $db) }
    if ($shared) { Write-Host ("  API-Keys:     Schluesselbund + Keys aus {0}" -f (Get-DataDir 'dev')) }
    $recent = Join-Path (Get-DesktopBackupDir $shared) 'recent'
    $backups = @(Get-ChildItem -LiteralPath $recent -Filter 'platinvault-*.db' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
    if ($backups.Count -gt 0) { Write-Host ("  Sicherungen:  {0} in {1} (neueste: {2})" -f $backups.Count, $recent, $backups[0].LastWriteTime.ToString('yyyy-MM-dd HH:mm')) }
    else { Write-Host "  Sicherungen:  keine ($recent)" }
    $exe = Find-DesktopExe
    Write-Host ("  Build:        {0}" -f $(if ($exe) { $exe } else { 'noch nicht gebaut' }))
    Write-Host ("  Protokoll:    {0}" -f (Join-Path $AppLogDir 'desktop.log'))
}

function Show-Logs {
    $files = switch ($Service) {
        { $_ -in @('', $null, 'desktop') } { @((Join-Path $AppLogDir 'desktop.log')) }
        'server' { @((Join-Path $AppLogDir 'server.log')) }
        'frontend' { @((Join-Path $LogDir 'desktop-frontend.log'), (Join-Path $LogDir 'desktop-frontend.err.log')) }
        'build' { @((Join-Path $LogDir 'desktop-build.log')) }
        default { throw "Unbekanntes Protokoll '$Service'. Erlaubt: desktop | server | frontend | build." }
    }
    $existing = @($files | Where-Object { Test-Path -LiteralPath $_ })
    if ($existing.Count -eq 0) {
        Write-Warn "Noch keine Protokolldatei ($($files -join ', ')) - das Programm wurde in diesem Ordner noch nicht gestartet."
        return
    }
    Get-Content -LiteralPath $existing -Wait -Tail 100 -Encoding UTF8
}

# ---- main --------------------------------------------------------------------------------------------------------------------------

switch ($Command) {
    'status' { Show-Status; return }
    'logs' { Show-Logs; return }
    'stop' { Stop-Everything; return }
    'restart' { Stop-Everything }
}

$script:AlreadyRunning = $false
$script:AppProcess = $null

if ($Detached) {
    Start-Everything
    if (-not $script:AlreadyRunning) {
        Write-Host 'Laeuft im Hintergrund. Status: .\start-windows.ps1 status - Protokoll: .\start-windows.ps1 logs - Beenden: .\start-windows.ps1 stop' -ForegroundColor Green
    }
    return
}

try {
    Start-Everything
    if ($script:AlreadyRunning) { return }
    Write-Host 'Beliebige Taste oder Strg+C beendet alles (auch Beenden im Tray-Menue des Programms beendet das Skript).' -ForegroundColor Magenta
    $interactive = $true
    while (@(Get-DesktopAppProcess).Count -gt 0) {
        if ($interactive) {
            try { if ([Console]::KeyAvailable) { $null = [Console]::ReadKey($true); break } }
            catch [System.InvalidOperationException] { $interactive = $false }
        }
        Start-Sleep -Milliseconds 500
    }
}
finally {
    if (-not $script:AlreadyRunning) { Stop-Everything }
}
