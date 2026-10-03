# External screenshot of the game window (works even if the plugin is not loaded or the game hangs).
# For in-engine captures prefer:  .\scripts\cmd.ps1 shot <name>
#   .\scripts\shot.ps1 [name]   -> C:\VRMod\logs\shots\<name>.png
param([string]$Name = ("win_{0:yyyyMMdd_HHmmss}" -f (Get-Date)))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$outDir = Join-Path $root 'logs\shots'
New-Item -ItemType Directory -Force $outDir | Out-Null

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class NivrWin {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
'@
[void][NivrWin]::SetProcessDPIAware()

$p = Get-Process NoImNotAHuman -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) { Write-Error 'Game window not found.' }
$r = New-Object NivrWin+RECT
[void][NivrWin]::GetClientRect($p.MainWindowHandle, [ref]$r)
$w = $r.R - $r.L; $h = $r.B - $r.T
if ($w -le 0 -or $h -le 0) { Write-Error 'Game window has no client area (minimized?).' }
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
# 1 = PW_CLIENTONLY, 2 = PW_RENDERFULLCONTENT (needed for DirectX swapchains)
$ok = [NivrWin]::PrintWindow($p.MainWindowHandle, $hdc, 3)
$g.ReleaseHdc($hdc); $g.Dispose()
$path = Join-Path $outDir ($Name + '.png')
$bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "PrintWindow=$ok ${w}x${h} -> $path"
