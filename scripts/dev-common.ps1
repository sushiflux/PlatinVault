<#
  Shared helpers for start.ps1, start-windows.ps1 and the scripts in scripts\ (dot-sourced). Windows PowerShell 5.1 compatible.
  Console output is German and ASCII-only.

  Since 2026-11-02 the database is one SQLite file (.data\dev\platinvault.db); development needs no Docker any more.
#>

$script:RootDir = Split-Path -Parent $PSScriptRoot

# Fixed ports of the browser dev stack (never chosen at run time).
$script:Ports = [ordered]@{
    frontend = 5230
    backend  = 5231
}

function Write-Step([string]$Message) { Write-Host "-> $Message" -ForegroundColor Cyan }
function Write-Ok([string]$Message) { Write-Host "   $Message" -ForegroundColor Green }
function Write-Warn([string]$Message) { Write-WarnLine $Message }
function Write-WarnLine([string]$Message) { Write-Host "   $Message" -ForegroundColor Yellow }

function Invoke-Native([string]$Exe, [string[]]$Arguments, [switch]$AllowFail, [switch]$Quiet) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        if ($Quiet) { & $Exe @Arguments 2>&1 | Out-Null }
        else { & $Exe @Arguments 2>&1 | ForEach-Object { "$_" } | Write-Host }
        $code = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }
    if ($code -ne 0 -and -not $AllowFail) { throw "$Exe $($Arguments -join ' ') ist fehlgeschlagen (Exit Code $code)." }
    return $code
}

function Get-DataDir([string]$Environment) { return (Join-Path $script:RootDir ".data\$Environment") }

# The browser stack's database file and its backup folder (the Windows program's shared mode uses the same two).
function Get-DatabaseFile([string]$Environment) { return (Join-Path (Get-DataDir $Environment) 'platinvault.db') }
function Get-BackupDir { return (Join-Path $script:RootDir '.backups\auto') }

# Environment of the dev server (dotnet watch) and of its helper commands (--backup-now, --restore): same data folder, same backups.
function Get-ServerEnvironment([string]$Environment) {
    return [ordered]@{
        ASPNETCORE_ENVIRONMENT = 'Development'
        Companion__DataDir     = Get-DataDir $Environment
        Companion__Backups__Dir = Get-BackupDir
    }
}

# Runs the server's database command (--backup-now / --restore <file>) with the dev configuration. $Dll is a built Companion.Api.dll.
function Invoke-ServerCommand([string]$Dll, [string[]]$Arguments, [string]$Environment = 'dev', [switch]$AllowFail) {
    $vars = Get-ServerEnvironment $Environment
    $saved = @{}
    foreach ($key in $vars.Keys) { $saved[$key] = [Environment]::GetEnvironmentVariable($key); [Environment]::SetEnvironmentVariable($key, $vars[$key]) }
    Push-Location (Join-Path $script:RootDir 'src\Companion.Api')
    try { return (Invoke-Native 'dotnet' (@($Dll) + $Arguments) -AllowFail:$AllowFail) }
    finally {
        Pop-Location
        foreach ($key in $vars.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key]) }
    }
}

# The server dll that "dotnet watch" of start.ps1 builds (Debug). Null when it was never built.
function Find-DevServerDll {
    $dll = Join-Path $script:RootDir 'src\Companion.Api\bin\Debug\net10.0\Companion.Api.dll'
    if (Test-Path -LiteralPath $dll) { return $dll }
    return $null
}

function Get-PortProcess([int]$Port) {
    $connection = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $connection) { return $null }
    return Get-Process -Id $connection.OwningProcess -ErrorAction SilentlyContinue
}

function Stop-ProcessTree([int]$ProcessId) {
    if (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) {
        cmd /c "taskkill /PID $ProcessId /T /F >nul 2>&1"
    }
}

function Get-OwnAppProcesses {
    $frontend = (Join-Path $script:RootDir 'frontend').ToLowerInvariant()
    Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object {
            $line = if ($_.CommandLine) { $_.CommandLine.ToLowerInvariant() } else { '' }
            ($_.Name -eq 'Companion.Api.exe') -or
            ($_.Name -eq 'dotnet.exe' -and $line.Contains('watch') -and $line.Contains('companion.api')) -or
            ($_.Name -eq 'node.exe' -and $line.Contains('vite') -and $line.Contains($frontend) -and -not $line.Contains('vite.desktop.config'))
        }
}

function Stop-AppProcesses {
    foreach ($process in @(Get-OwnAppProcesses)) { Stop-ProcessTree $process.ProcessId }
    foreach ($port in @($script:Ports.frontend, $script:Ports.backend)) {
        $owner = Get-PortProcess $port
        if ($owner -and $owner.ProcessName -in @('dotnet', 'node', 'Companion.Api')) { Stop-ProcessTree $owner.Id }
    }
}
