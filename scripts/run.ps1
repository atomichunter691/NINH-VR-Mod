# One command: build + deploy + launch the game copy + tail the log.
#   .\scripts\run.ps1                 build, launch windowed 1280x720, tail C:\VRMod\logs\nivr.log (Ctrl+C stops tailing only)
#   .\scripts\run.ps1 -NoBuild        skip the build
#   .\scripts\run.ps1 -NoTail         launch and return (for scripted use)
#   .\scripts\run.ps1 -Fullscreen     let the game pick its own resolution
#   .\scripts\run.ps1 -WaitReady 120  block until BepInEx reports "Chainloader startup complete" (seconds)
param(
    [switch]$NoBuild,
    [switch]$NoTail,
    [switch]$Fullscreen,
    [int]$Width = 1280,
    [int]$Height = 720,
    [int]$WaitReady = 0
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$game = Join-Path $root 'game_copy'
$exe = Join-Path $game 'NoImNotAHuman.exe'
$log = Join-Path $root 'logs\nivr.log'
$bepLog = Join-Path $game 'BepInEx\LogOutput.log'

if (-not $NoBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1')
    if ($LASTEXITCODE -ne 0) { exit 1 }
}

if (-not (Get-Process steam -ErrorAction SilentlyContinue)) {
    Write-Warning 'Steam is not running - the game needs Steam running (and the game owned) to start.'
}
Get-Process NoImNotAHuman -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
if (Test-Path $log) { Move-Item $log "$log.prev" -Force }   # keep the previous run; the tail below waits for the fresh file

$gameArgs = @()
if (-not $Fullscreen) { $gameArgs = @('-screen-fullscreen', '0', '-screen-width', "$Width", '-screen-height', "$Height") }
Write-Host "Launching $exe $gameArgs"
if ($gameArgs.Count) { Start-Process -FilePath $exe -WorkingDirectory $game -ArgumentList $gameArgs }
else { Start-Process -FilePath $exe -WorkingDirectory $game }

if ($WaitReady -gt 0) {
    $deadline = (Get-Date).AddSeconds($WaitReady)
    $ready = $false
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 1
        if ((Test-Path $bepLog) -and (Select-String -Path $bepLog -Pattern 'Chainloader startup complete' -Quiet)) { $ready = $true; break }
        if (-not (Get-Process NoImNotAHuman -ErrorAction SilentlyContinue)) { Write-Host 'Game process exited early.'; break }
    }
    Write-Host "ready=$ready"
}

if (-not $NoTail) {
    Write-Host "Tailing $log (Ctrl+C to stop tailing; the game keeps running)"
    while (-not (Test-Path $log)) {
        Start-Sleep -Milliseconds 300
        if (-not (Get-Process NoImNotAHuman -ErrorAction SilentlyContinue)) { Write-Host 'Game exited before the log appeared.'; exit 1 }
    }
    Get-Content $log -Wait
}
