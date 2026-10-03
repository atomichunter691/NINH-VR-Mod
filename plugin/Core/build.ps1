# Builds NIVR.Core and deploys it (plus openxr_loader.dll) to game_copy\BepInEx\plugins\NIVR.
#   .\plugin\Core\build.ps1            (Debug)
#   .\plugin\Core\build.ps1 -Release
# scripts\build.ps1 only builds src\; run this one for the core, then scripts\run.ps1 -NoBuild to launch.
param([switch]$Release)
$ErrorActionPreference = 'Stop'
$cfg = if ($Release) { 'Release' } else { 'Debug' }
if (Get-Process NoImNotAHuman -ErrorAction SilentlyContinue) {
    Write-Host 'Game is running - stopping it so the plugin dll can be replaced.'
    Stop-Process -Name NoImNotAHuman -Force -ErrorAction SilentlyContinue # may already be exiting
    Start-Sleep -Seconds 2
}
dotnet build (Join-Path $PSScriptRoot 'NIVR.Core.csproj') -c $cfg -nologo -v q -clp:NoSummary "-p:RestoreConfigFile=$PSScriptRoot\NuGet.Config"
if ($LASTEXITCODE -ne 0) { Write-Host 'BUILD FAILED'; exit 1 }
Write-Host 'BUILD OK'
