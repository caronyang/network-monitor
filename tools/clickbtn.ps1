param([int]$ProcId = 0, [int]$Index = 0)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$procCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $ProcId)
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $procCond)
if ($null -eq $win) { Write-Output "window not found"; exit 1 }
Write-Output ("window: '{0}'" -f $win.Current.Name)

$all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
  (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
for ($i = 0; $i -lt $all.Count; $i++) { Write-Output ("  [{0}] '{1}'" -f $i, $all.Item($i).Current.Name) }
if ($Index -ge $all.Count) { Write-Output "index out of range"; exit 1 }
$el = $all.Item($Index)
$pattern = $null
if ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
  $pattern.Invoke()
  Write-Output ("invoked [{0}] '{1}'" -f $Index, $el.Current.Name)
} else {
  Write-Output ("no invoke pattern on [{0}]" -f $Index)
}
