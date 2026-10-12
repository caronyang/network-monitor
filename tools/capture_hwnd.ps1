param([int]$Hwnd, [string]$OutFile)
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class CH { [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; } }
'@ -Language CSharp
$r = New-Object CH+RECT
if (-not [CH]::GetWindowRect([IntPtr]$Hwnd, [ref]$r)) { Write-Output 'GetWindowRect failed'; exit 1 }
$w = $r.R - $r.L; $h = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
$g.Dispose()
$bmp.Save($OutFile, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "saved $OutFile ($w x $h) at ($($r.L),$($r.T))"
