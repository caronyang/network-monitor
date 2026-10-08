param([string]$Handle)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$h = [IntPtr][int64]$Handle
$win = [System.Windows.Automation.AutomationElement]::FromHandle($h)
$pat = $null
if ($win.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern, [ref]$pat)) {
  $pat.Close()
  Write-Output "closed via window pattern"
} else {
  Write-Output "no window pattern"
}
