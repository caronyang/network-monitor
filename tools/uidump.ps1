param([string]$Handle, [string]$OutFile)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$h = [IntPtr][int64]$Handle
$win = [System.Windows.Automation.AutomationElement]::FromHandle($h)
$sb = New-Object System.Text.StringBuilder
$all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
[void]$sb.AppendLine("total elements: " + $all.Count)
foreach ($e in $all) {
  $ct = $e.Current.ControlType.ProgrammaticName
  $r = $e.Current.BoundingRectangle
  [void]$sb.AppendLine(("  {0,-24} '{1}' rect=({2},{3},{4},{5})" -f $ct, $e.Current.Name, [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height))
}
[System.IO.File]::WriteAllText($OutFile, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))
Write-Output "written"
