<#
  Installs the git hooks (pre-commit, post-commit, post-merge) from scripts\git-hooks into this repository's own hooks
  folder. This is the one deliberate way to take over changed hooks: look at them first (git diff scripts/git-hooks), then run
  .\scripts\install-hooks.ps1. Also removes the old core.hooksPath setting if it points at scripts/git-hooks.
#>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'dev-common.ps1')

if (-not (Test-Path (Join-Path $RootDir '.git'))) { throw 'Kein Git-Repository: .git fehlt.' }
Remove-LegacyHooksPath | Out-Null
$hooksDir = Get-HooksDir
if (-not $hooksDir) { throw 'Der Hook-Ordner des Repositorys konnte nicht ermittelt werden.' }
Write-Step "Installiere Git-Hooks nach $hooksDir"
Install-GitHooks $hooksDir $script:HookNames
Write-Ok 'Fertig.'
