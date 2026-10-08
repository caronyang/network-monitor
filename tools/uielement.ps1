param([string]$Handle, [int]$X, [int]$Y)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$h = [IntPtr][int64]$Handle
$win = [System.Windows.Automation.AutomationElement]::FromHandle($h)
$pt = New-Object System.Windows.Point($X, $Y)
$el = [System.Windows.Automation.AutomationElement]::FromPoint($pt)
if ($el -eq $null) { Write-Output "no element at $X,$Y"; exit }
$c = $el.Current
$r = $c.BoundingRectangle
Write-Output ("at {0},{1}: type={2} name='{3}' rect=({4},{5},{6},{7}) offscreen={8} enabled={9}" -f `
  $X, $Y, $c.ControlType.ProgrammaticName, $c.Name, [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height, $c.IsOffscreen, $c.IsEnabled)
$el2 = $el
while ($el2 -ne $null) {
  $c2 = $el2.Current
  $r2 = $c2.BoundingRectangle
  Write-Output ("  parent: {0} '{1}' rect=({2},{3},{4},{5})" -f $c2.ControlType.ProgrammaticName, $c2.Name, [int]$r2.X, [int]$r2.Y, [int]$r2.Width, [int]$r2.Height)
  $el2 = [System.Windows.Automation.TreeWalker]::RawViewWalker.GetParent($el2)
}
