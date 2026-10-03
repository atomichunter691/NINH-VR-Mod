# Sends dev commands to the running game through NIVR.DevTools' file inbox (C:\VRMod\logs\cmd).
#   .\scripts\cmd.ps1 shot menu            -> C:\VRMod\logs\shots\menu.png
#   .\scripts\cmd.ps1 dump menu            -> C:\VRMod\logs\dumps\menu.txt
#   .\scripts\cmd.ps1 'loadscene 2' 'timescale 1'
# See src\NIVR.DevTools\DevToolsBehaviour.cs + GameCommands.cs for the command list.
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Commands)
$root = Split-Path $PSScriptRoot -Parent
$dir = Join-Path $root 'logs\cmd'
New-Item -ItemType Directory -Force $dir | Out-Null
# "shot menu" may arrive as two args; treat a lone verb followed by non-verb words as one command.
$line = ($Commands -join ' ')
if ($Commands.Count -gt 1 -and ($Commands | Where-Object { $_ -match ' ' })) { $lines = $Commands } else { $lines = @($line) }
$tmp = Join-Path $dir ("{0:yyyyMMdd_HHmmss_fff}.tmp" -f (Get-Date))
Set-Content -Path $tmp -Value $lines -Encoding ascii
Rename-Item $tmp ([IO.Path]::ChangeExtension($tmp, '.cmd'))
