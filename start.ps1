<#
  PlatinVault - one script for the browser development environment.

  .\start.ps1                   start (default): backend (dotnet watch) and frontend (Vite) run natively in this window.
                                Any key or Ctrl+C stops everything cleanly. No Docker needed: the database is one SQLite file.
  .\start.ps1 -Detached         start everything in the background (logs in .logs\) and return immediately.
  .\start.ps1 stop              take a backup, then stop backend and frontend (the database file stays).
  .\start.ps1 restart           stop + start
  .\start.ps1 status            state of backend, frontend, database file and backups
  .\start.ps1 logs [service]    logs: backend | frontend

  Data: .data\dev (database platinvault.db, encrypted API keys, caches). Backups: .backups\auto (the server makes them itself: at start,
  every 6 hours and when it stops cleanly; "stop" makes one more before ending the processes). Restore: .\scripts\restore-db.ps1.
  Ports are fixed: frontend 5230, backend 5231 (a busy port stops the start with a clear message, it is never replaced).
#>
param(
    [Parameter(Position = 0)][ValidateSet('start', 'stop', 'restart', 'status', 'logs')][string]$Command = 'start',
    [Parameter(Position = 1)][string]$Service,
    [ValidateSet('dev')][string]$Env = 'dev',
    [switch]$Detached
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts\dev-common.ps1')
Set-Location $RootDir

$LogDir = Join-Path $RootDir '.logs'

function Initialize-Repository {
    if (Test-Path (Join-Path $RootDir '.git')) {
        Invoke-Native 'git' @('config', 'core.hooksPath', 'scripts/git-hooks') -Quiet | Out-Null
    }
    $versionFile = Join-Path $RootDir 'version.txt'
    if (-not (Test-Path $versionFile)) {
        $now = Get-Date
        $previous = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try { $count = (& git rev-list --count HEAD --since="$($now.Year)-01-01T00:00:00" 2>$null) }
        finally { $ErrorActionPreference = $previous }
        if ($LASTEXITCODE -ne 0 -or -not $count) { $count = '0' }
        [System.IO.File]::WriteAllText($versionFile, "v$($now.Year).$($now.Month).$count")
    }
    New-Item -ItemType Directory -Force -Path (Get-DataDir $Env), (Get-BackupDir) | Out-Null
}

function Assert-PortsFree {
    foreach ($name in @('frontend', 'backend')) {
        $port = $Ports[$name]
        $owner = Get-PortProcess $port
        if ($owner) {
            throw "Port $port ($name) ist belegt von '$($owner.ProcessName)' (PID $($owner.Id)). Bitte diesen Prozess beenden - die Ports sind fest vergeben."
        }
    }
}

function Initialize-Frontend {
    if (-not (Test-Path (Join-Path $RootDir 'frontend\node_modules'))) {
        Write-Step 'Installiere Frontend-Abhaengigkeiten (npm install)...'
        Push-Location (Join-Path $RootDir 'frontend')
        try { Invoke-Native 'npm.cmd' @('install', '--no-fund', '--no-audit') | Out-Null } finally { Pop-Location }
    }
}

function Get-BackendEnvironment {
    $environment = Get-ServerEnvironment $Env
    $environment['ASPNETCORE_URLS'] = "http://localhost:$($Ports.backend)"
    $environment['DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER'] = '1'
    return $environment
}

# Starts a hidden process through WMI so it is not part of this console's job object and keeps running after the
# script (or the terminal that launched it) has exited. WMI processes do not inherit this session's environment,
# so the variables are set inside the cmd.exe command line.
function Start-DetachedProcess([string]$Command, [string]$WorkingDirectory, [System.Collections.IDictionary]$Environment, [string]$LogName) {
    $prefix = ''
    if ($Environment) {
        foreach ($key in $Environment.Keys) { $prefix += "set `"$key=$($Environment[$key])`"&& " }
    }
    $commandLine = "cmd.exe /d /c `"$prefix$Command > `"$LogDir\$LogName.log`" 2> `"$LogDir\$LogName.err.log`"`""
    $startup = New-CimInstance -ClassName Win32_ProcessStartup -ClientOnly -Property @{ ShowWindow = [uint16]0 }
    $result = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
        CommandLine = $commandLine; CurrentDirectory = $WorkingDirectory; ProcessStartupInformation = $startup
    }
    if ($result.ReturnValue -ne 0) { throw "$LogName konnte nicht gestartet werden (WMI-Code $($result.ReturnValue))." }
}

function Start-App {
    $backendEnv = Get-BackendEnvironment
    # No hot reload: dotnet watch's edit-and-continue crashed the watcher several times ("An item with the same key has
    # already been added" in Roslyn's delta emit) and left the backend down; a clean restart per change is reliable
    $backendArgs = 'watch --no-hot-reload --project src\Companion.Api\Companion.Api.csproj'
    $frontendDir = Join-Path $RootDir 'frontend'

    if ($Detached) {
        New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
        Write-Step "Starte Backend (dotnet watch) auf Port $($Ports.backend) im Hintergrund..."
        Start-DetachedProcess "dotnet $backendArgs" $RootDir $backendEnv 'backend'
        Write-Step "Starte Oberflaeche (Vite) auf Port $($Ports.frontend) im Hintergrund..."
        Start-DetachedProcess 'npm.cmd run dev' $frontendDir $null 'frontend'
        return
    }

    foreach ($key in $backendEnv.Keys) { Set-Item -Path "Env:$key" -Value $backendEnv[$key] }
    Write-Step "Starte Backend (dotnet watch) auf Port $($Ports.backend)..."
    $script:BackendProcess = Start-Process -FilePath 'dotnet.exe' -ArgumentList $backendArgs -WorkingDirectory $RootDir -NoNewWindow -PassThru
    Write-Step "Starte Oberflaeche (Vite) auf Port $($Ports.frontend)..."
    $script:FrontendProcess = Start-Process -FilePath 'npm.cmd' -ArgumentList 'run dev' -WorkingDirectory $frontendDir -NoNewWindow -PassThru
}

function Wait-ForApp {
    Write-Step 'Warte auf Backend und Oberflaeche...'
    $deadline = (Get-Date).AddSeconds(150)
    $backendUp = $false
    $frontendUp = $false
    while ((Get-Date) -lt $deadline -and -not ($backendUp -and $frontendUp)) {
        Start-Sleep -Milliseconds 750
        if (-not $backendUp) { $backendUp = [bool](Get-PortProcess $Ports.backend) }
        if (-not $frontendUp) { $frontendUp = [bool](Get-PortProcess $Ports.frontend) }
    }
    if (-not $backendUp) { Write-Warn 'Backend antwortet noch nicht - Ausgabe pruefen (.\start.ps1 logs backend).' }
    if (-not $frontendUp) { Write-Warn 'Oberflaeche antwortet noch nicht - Ausgabe pruefen (.\start.ps1 logs frontend).' }
    return ($backendUp -and $frontendUp)
}

# One backup right before the processes are ended (best effort, never blocks stopping). Ending dotnet watch is not a clean stop of the
# server, so its own "stop" backup would not run. The server's --backup-now makes an online copy while the server still runs.
function Invoke-BackupNow {
    $dll = Find-DevServerDll
    if (-not $dll -or -not (Test-Path -LiteralPath (Get-DatabaseFile $Env))) { return }
    Write-Step 'Sichere die Datenbank vor dem Stoppen...'
    try { $null = Invoke-ServerCommand $dll @('--backup-now') $Env -AllowFail }
    catch { Write-Warn "Sicherung vor dem Stoppen nicht moeglich: $($_.Exception.Message)" }
}

function Stop-Everything {
    Write-Host "`nFahre PlatinVault herunter..." -ForegroundColor Yellow
    if (@(Get-OwnAppProcesses).Count -gt 0 -or $script:BackendProcess) { Invoke-BackupNow }
    Write-Step 'Beende Backend und Oberflaeche...'
    if ($script:BackendProcess) { Stop-ProcessTree $script:BackendProcess.Id }
    if ($script:FrontendProcess) { Stop-ProcessTree $script:FrontendProcess.Id }
    Stop-AppProcesses
    Write-Host 'Alles sauber beendet (die Datenbankdatei bleibt).' -ForegroundColor Green
}

function Show-Status {
    Write-Host "PlatinVault - Umgebung '$Env'" -ForegroundColor Cyan
    $version = Join-Path $RootDir 'version.txt'
    if (Test-Path $version) { Write-Host ("  Version:     {0}" -f ((Get-Content $version) -join ' / ')) }
    foreach ($name in $Ports.Keys) {
        $owner = Get-PortProcess $Ports[$name]
        $state = if ($owner) { "laeuft ($($owner.ProcessName), PID $($owner.Id))" } else { 'gestoppt' }
        Write-Host ("  {0,-10}  Port {1}: {2}" -f $name, $Ports[$name], $state)
    }
    $db = Get-DatabaseFile $Env
    if (Test-Path -LiteralPath $db) { Write-Host ("  Datenbank:   {0} ({1:N0} KB)" -f $db, ((Get-Item -LiteralPath $db).Length / 1KB)) }
    else { Write-Host "  Datenbank:   $db (noch nicht angelegt - entsteht beim ersten Start)" }
    $recent = Join-Path (Get-BackupDir) 'recent'
    $backups = @(Get-ChildItem -LiteralPath $recent -Filter 'platinvault-*.db' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
    if ($backups.Count -gt 0) { Write-Host ("  Sicherungen: {0} in {1} (neueste: {2})" -f $backups.Count, $recent, $backups[0].LastWriteTime.ToString('yyyy-MM-dd HH:mm')) }
    else { Write-Host "  Sicherungen: noch keine ($recent)" }
    try {
        $health = Invoke-RestMethod -Uri "http://localhost:$($Ports.backend)/health" -TimeoutSec 5
        Write-Host ("  Health:      {0} (Datenbank: {1})" -f $health.status, $health.database)
    }
    catch {
        Write-Host '  Health:      Backend nicht erreichbar'
    }
}

function Show-Logs {
    switch ($Service) {
        { $_ -in @('', $null, 'backend') } { Get-Content "$LogDir\backend.log", "$LogDir\backend.err.log" -Wait -Tail 100 -ErrorAction Stop }
        'frontend' { Get-Content "$LogDir\frontend.log", "$LogDir\frontend.err.log" -Wait -Tail 100 -ErrorAction Stop }
        default { throw "Unbekanntes Protokoll '$Service'. Erlaubt: backend | frontend." }
    }
}

function Start-Everything {
    Write-Host 'Starte PlatinVault - Entwicklungsumgebung...' -ForegroundColor Cyan
    Stop-AppProcesses
    Initialize-Repository
    Assert-PortsFree
    Initialize-Frontend
    Start-App
    $ready = Wait-ForApp

    Write-Host ''
    Write-Host '========================================================' -ForegroundColor Green
    Write-Host "  Oberflaeche: http://localhost:$($Ports.frontend)" -ForegroundColor Green
    Write-Host "  API:         http://localhost:$($Ports.backend)  (Health: /health)" -ForegroundColor Green
    Write-Host "  Datenbank:   $(Get-DatabaseFile $Env)" -ForegroundColor Green
    Write-Host "  Sicherungen: $(Get-BackupDir)" -ForegroundColor Green
    if (-not $ready) { Write-Host '  Achtung: nicht alle Dienste haben rechtzeitig geantwortet.' -ForegroundColor Yellow }
    Write-Host '========================================================' -ForegroundColor Green
}

switch ($Command) {
    'status' { Show-Status; return }
    'logs' { Show-Logs; return }
    'stop' { Stop-Everything; return }
    'restart' { Stop-Everything }
}

if ($Detached) {
    Start-Everything
    Write-Host "Laeuft im Hintergrund. Logs: .\start.ps1 logs backend|frontend - Beenden: .\start.ps1 stop" -ForegroundColor Green
    return
}

try {
    Start-Everything
    Write-Host 'Beliebige Taste oder Strg+C beendet alles.' -ForegroundColor Magenta
    $null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')
}
finally {
    Stop-Everything
}
