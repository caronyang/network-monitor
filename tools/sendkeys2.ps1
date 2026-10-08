param([string]$StatsHandle, [string]$ComboWidth, [string]$VKeys)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System; using System.Runtime.InteropServices;
public class KM {
  [DllImport("user32.dll", CharSet=CharSet.Auto)] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr wParam, IntPtr lParam);
  [DllImport("user32.dll")] public static extern IntPtr PostMessage(IntPtr h, uint msg, IntPtr wParam, IntPtr lParam);
}
"@
$h = [IntPtr][int64]$StatsHandle
$win = [System.Windows.Automation.AutomationElement]::FromHandle($h)
$all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$combo = $null
foreach ($e in $all) {
  if ($e.Current.ControlType.ProgrammaticName -eq "ControlType.ComboBox") {
    $r = $e.Current.BoundingRectangle
    if ([int]$r.Width -eq [int]$ComboWidth) { $combo = $e; break }
  }
}
if ($combo -eq $null) { Write-Output "combo w=$ComboWidth not found"; exit 1 }
$nwh = $combo.Current.NativeWindowHandle
if ($nwh -eq 0) { Write-Output "combo has no native handle"; exit 1 }
$ch = [IntPtr]$nwh
$keys = $VKeys -split ","
foreach ($k in $keys) {
  $vk = [IntPtr][int]$k
  # WM_KEYDOWN=0x100, WM_KEYUP=0x101; lParam: repeat=1
  [void][KM]::PostMessage($ch, 0x100, $vk, [IntPtr]1)
  [void][KM]::PostMessage($ch, 0x101, $vk, [IntPtr]1)
  Start-Sleep -Milliseconds 120
}
Write-Output "sent keys [$VKeys] to combo hwnd=$nwh"
