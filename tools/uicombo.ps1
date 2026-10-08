param([string]$ProcId, [string]$StatsHandle, [string]$ComboWidth, [string]$ItemName)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$code = @"
using System; using System.Text; using System.Runtime.InteropServices; using System.Collections.Generic;
public class EW2 {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  public static List<long> List(int pid, bool visibleOnly) {
    var res = new List<long>();
    EnumWindows((h, l) => {
      int p; GetWindowThreadProcessId(h, out p);
      if (p == pid) { var sb = new StringBuilder(64); GetClassName(h, sb, 64);
        if (sb.ToString() == "ComboLBox" && (!visibleOnly || IsWindowVisible(h))) res.Add(h.ToInt64()); }
      return true;
    }, IntPtr.Zero);
    return res;
  }
}
"@
Add-Type -TypeDefinition $code

# 1. find the combo
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

# 2. collapse then expand
$exp = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
try { $exp.Collapse() } catch { }
Start-Sleep -Milliseconds 300
$exp.Expand()
Start-Sleep -Milliseconds 500

# 3. look into every ComboLBox of this process (visible first, then all)
$candidates = @([EW2]::List([int]$ProcId, $true)) + @([EW2]::List([int]$ProcId, $false))
$chosen = $null
foreach ($lbAddr in $candidates) {
  $lb = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$lbAddr)
  try {
    $items = $lb.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
  } catch { continue }
  foreach ($e in $items) {
    if ($e.Current.Name -eq $ItemName) { $chosen = $e; break }
  }
  if ($chosen) { break }
}
if ($chosen -eq $null) { Write-Output "item '$ItemName' not found in any ComboLBox"; try{$exp.Collapse()}catch{}; exit 1 }

$sel = $null
if ($chosen.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$sel)) {
  $sel.Select(); Write-Output "selected '$ItemName'"
} else {
  $inv = $null
  if ($chosen.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv)) {
    $inv.Invoke(); Write-Output "invoked '$ItemName'"
  } else { Write-Output "no pattern on item"; try{$exp.Collapse()}catch{}; exit 1 }
}
Start-Sleep -Milliseconds 400
try { $exp.Collapse() } catch { }
