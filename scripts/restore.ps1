# Restore paths. The Steam install is never modified, so "restore" only concerns the working copy.
#   .\scripts\restore.ps1 -DisableMod     turn BepInEx off in the copy (doorstop enabled=false); game runs vanilla
#   .\scripts\restore.ps1 -EnableMod      turn it back on
#   .\scripts\restore.ps1 -RebuildCopy    wipe game files in game_copy and re-copy from Steam, then re-apply BepInEx + plugins
#   .\scripts\restore.ps1 -VerifyOriginal list any BepInEx/doorstop files in the Steam install (should print 'clean')
param([switch]$DisableMod, [switch]$EnableMod, [switch]$RebuildCopy, [switch]$VerifyOriginal)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$copy = Join-Path $root 'game_copy'
$steam = "C:\Program Files (x86)\Steam\steamapps\common\No, I'm not a Human"
$ini = Join-Path $copy 'doorstop_config.ini'

function Set-Doorstop([bool]$on) {
    $v = if ($on) { 'true' } else { 'false' }
    $done = $false
    $lines = Get-Content $ini | ForEach-Object {
        if (-not $done -and $_ -match '^\s*enabled\s*=') { $done = $true; "enabled = $v" } else { $_ }
    }
    Set-Content $ini $lines
    Write-Host "doorstop enabled = $v"
}

if ($DisableMod) { Set-Doorstop $false }
if ($EnableMod) { Set-Doorstop $true }

if ($VerifyOriginal) {
    $bad = 'BepInEx', 'dotnet', 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'steam_appid.txt' |
        Where-Object { Test-Path -LiteralPath (Join-Path $steam $_) }
    if ($bad) { Write-Warning "Steam install contains mod files: $($bad -join ', ')" } else { Write-Host 'clean' }
}

if ($RebuildCopy) {
    Get-Process NoImNotAHuman -ErrorAction SilentlyContinue | Stop-Process -Force
    # /MIR on the game folders only; BepInEx, dotnet, doorstop files and steam_appid.txt are preserved via /XD /XF.
    robocopy $steam $copy /MIR /NFL /NDL /NP /R:1 /W:1 /MT:8 /XD "$copy\BepInEx" "$copy\dotnet" /XF winhttp.dll doorstop_config.ini .doorstop_version steam_appid.txt changelog.txt | Select-Object -Last 12
    if ($LASTEXITCODE -ge 8) { Write-Error "robocopy failed ($LASTEXITCODE)" }
    if (-not (Test-Path "$copy\BepInEx\core")) {
        $zip = Get-ChildItem (Join-Path $root 'tools') -Filter 'BepInEx-Unity.IL2CPP-win-x64-*.zip' | Select-Object -First 1
        Expand-Archive $zip.FullName -DestinationPath $copy -Force
    }
    Set-Content -Path "$copy\steam_appid.txt" -Value '3180070' -Encoding ascii -NoNewline
    # A game update changes GameAssembly.dll; BepInEx regenerates interop on next launch automatically.
    Write-Host 'Copy rebuilt. Run scripts\run.ps1 to rebuild/deploy plugins.'
    exit 0
}
