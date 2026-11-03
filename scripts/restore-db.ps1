<#
  Restores the PlatinVault dev database (.data\dev\platinvault.db) from a backup.

  .\scripts\restore-db.ps1                       newest backup from .backups\auto\recent
  .\scripts\restore-db.ps1 -File <backup.db>     a specific backup (also from .backups\auto\daily or a copy elsewhere)
  .\scripts\restore-db.ps1 -Yes                  no question

  The work is done by the server itself (Companion.Api --restore, see src/Companion.Api/Data/DatabaseCommands.cs): the chosen backup is
  copied to .backups\auto\restore and checked first, the current state gets a safety backup (no pruning), only then the file is replaced.
  Backend and frontend of start.ps1 are stopped before; start them again with .\start.ps1 - the backend brings an older backup up to date
  by itself (migrations). The Windows program in shared mode (start-windows.ps1) uses the same file and must be stopped first.
  Windows PowerShell 5.1 compatible. Console output is German and ASCII-only.
#>
param(
    [string]$File,
    [ValidateSet('dev')][string]$Env = 'dev',
    [switch]$Yes
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'dev-common.ps1')
Set-Location $RootDir

if (-not $File) {
    $latest = Get-ChildItem (Join-Path (Get-BackupDir) 'recent') -Filter 'platinvault-*.db' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $latest) { throw 'Keine Sicherung in .backups\auto\recent gefunden - mit -File <pfad.db> eine angeben.' }
    $File = $latest.FullName
}
if (-not (Test-Path -LiteralPath $File)) { throw "Datei nicht gefunden: $File" }
$File = (Resolve-Path -LiteralPath $File).Path
$item = Get-Item -LiteralPath $File

Write-Host "Wiederherstellung aus: $File" -ForegroundColor Cyan
Write-Host ("  {0:N0} Bytes, Stand {1}" -f $item.Length, $item.LastWriteTime)
Write-Host "  Ziel: $(Get-DatabaseFile $Env) (wird komplett ersetzt)" -ForegroundColor Yellow

# The Windows program of start-windows.ps1 works on this same file by default; replacing it under a running program is refused anyway
# (the file is open), so say it clearly before anything happens.
$sharedDesktop = @(Get-CimInstance Win32_Process -Filter "Name='PlatinVault.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -and $_.CommandLine.ToLowerInvariant().Contains('--shared-data-dir') })
if ($sharedDesktop.Count -gt 0) {
    throw "Das Windows-Programm (PlatinVault.exe, PID $($sharedDesktop[0].ProcessId)) nutzt diese Datenbank gerade. Bitte zuerst beenden: .\start-windows.ps1 stop - danach erneut wiederherstellen."
}

if (-not $Yes) {
    $answer = Read-Host 'Fortfahren? Die aktuelle Datenbank wird vorher noch einmal gesichert (j/N)'
    if ($answer -notmatch '^(j|ja|y|yes)$') { Write-Host 'Abgebrochen.'; exit 1 }
}

Write-Step 'Stoppe Backend und Frontend...'
Stop-AppProcesses

# Own build output: the running dotnet watch (if any) would lock src\Companion.Api\bin
$artifacts = Join-Path $RootDir 'artifacts\dev-tools\dotnet'
Write-Step 'Baue den Server fuer die Wiederherstellung...'
$null = Invoke-Native 'dotnet' @('build', (Join-Path $RootDir 'src\Companion.Api\Companion.Api.csproj'), '-c', 'Debug', '--artifacts-path', $artifacts, '-nologo', '-v:q')
$dll = Join-Path $artifacts 'bin\Companion.Api\debug\Companion.Api.dll'
if (-not (Test-Path -LiteralPath $dll)) { throw "Server-Build nicht gefunden: $dll" }

Write-Step 'Spiele die Sicherung ein...'
$code = Invoke-ServerCommand $dll @('--restore', $File) $Env -AllowFail
if ($code -ne 0) { throw "Die Wiederherstellung ist nicht erfolgt (Exit Code $code) - Grund siehe oben. Die Datenbank ist unveraendert." }
Write-Host "`nFertig. Starten mit .\start.ps1 - fehlende Migrationen wendet das Backend beim Start an." -ForegroundColor Green
