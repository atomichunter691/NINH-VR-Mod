# Builds every plugin under C:\VRMod\src and auto-deploys to game_copy\BepInEx\plugins\NIVR.
#   .\scripts\build.ps1            (Debug)
#   .\scripts\build.ps1 -Release
param([switch]$Release)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$cfg = if ($Release) { 'Release' } else { 'Debug' }

if (Get-Process NoImNotAHuman -ErrorAction SilentlyContinue) {
    Write-Host 'Game is running - stopping it so the plugin dlls can be replaced.'
    Stop-Process -Name NoImNotAHuman -Force
    Start-Sleep -Seconds 2
}

$failed = $false
Get-ChildItem (Join-Path $root 'src') -Recurse -Filter *.csproj | ForEach-Object {
    Write-Host "== building $($_.Name) ($cfg)"
    dotnet build $_.FullName -c $cfg -nologo -v q -clp:NoSummary
    if ($LASTEXITCODE -ne 0) { $failed = $true }
}
if ($failed) { Write-Host 'BUILD FAILED'; exit 1 }
Write-Host 'BUILD OK'
