# Produces a strict allowlist archive. Never includes local game references, assets, DevTools or BepInEx itself.
param()
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1') -Release
if ($LASTEXITCODE -ne 0) { throw 'Release build failed' }
[xml]$project = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'NIVR.Core.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+([-.][A-Za-z0-9.-]+)?$') { throw 'Invalid release version' }
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$releaseDir = Join-Path $workspace 'release'
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
$zipPath = Join-Path $releaseDir "NIVR-$version.zip"
$files = [ordered]@{
    'BepInEx/plugins/NIVR/NIVR.Core.dll' = (Join-Path $PSScriptRoot 'bin\Release\NIVR.Core.dll')
    'BepInEx/plugins/NIVR/openxr_loader.dll' = (Join-Path $PSScriptRoot 'lib\openxr_loader.dll')
    'BepInEx/plugins/NIVR/LICENSE.openxr.txt' = (Join-Path $PSScriptRoot 'lib\LICENSE.openxr.txt')
    'README.md' = (Join-Path $PSScriptRoot 'README.md')
}
foreach ($file in $files.Values) { if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing package input: $file" } }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stream = [IO.File]::Open($zipPath, [IO.FileMode]::Create)
$archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($entry in $files.GetEnumerator()) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $entry.Value, $entry.Key, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose(); $stream.Dispose() }
$check = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    if ($check.Entries.Count -ne $files.Count) { throw 'Unexpected archive entry count' }
    foreach ($entry in $check.Entries) { if (-not $files.Contains($entry.FullName)) { throw "Unexpected package file: $($entry.FullName)" } }
} finally { $check.Dispose() }
Write-Host "Release ready: $zipPath"
