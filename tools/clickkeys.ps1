param([string]$StatsHandle, [string]$ComboX, [string]$ComboY, [string]$Keys)
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class M {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
$h = [IntPtr][int64]$StatsHandle
[void][M]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 300
$r = New-Object M+RECT
[void][M]::GetWindowRect($h, [ref]$r)
# client origin = window left + border; WinForms stats: border ~8px, title ~30px
$cx = $r.Left + [int]$ComboX
$cy = $r.Top + [int]$ComboY
[void][M]::SetCursorPos($cx, $cy)
Start-Sleep -Milliseconds 150
# left down + up
[M]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)  # LEFTDOWN
[M]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)  # LEFTUP
Start-Sleep -Milliseconds 300
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.SendKeys]::SendWait($Keys)
Start-Sleep -Milliseconds 400
Write-Output "clicked ($cx,$cy) sent keys '$Keys'"
