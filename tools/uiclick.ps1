param([int]$ProcId = 0, [string]$Handle = "", [string]$Name, [string]$OutFile = "")
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

if ($Handle -ne "") {
  $h = [IntPtr][int64]$Handle
  $win = [System.Windows.Automation.AutomationElement]::FromHandle($h)
} else {
  $root = [System.Windows.Automation.AutomationElement]::RootElement
  $procCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $ProcId)
  $win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $procCond)
  if ($null -eq $win) {
    Write-Output "window not found by pid; top-level windows:"
    $wins = $root.FindAll([System.Windows.Automation.TreeScope]::Children,
      (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $ProcId)))
    foreach ($w in $wins) { Write-Output ("  '{0}' handle={1}" -f $w.Current.Name, $w.Current.NativeWindowHandle) }
    exit 1
  }
}
Write-Output ("window: '{0}'" -f $win.Current.Name)

$nameCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)
$el = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCond)
if ($null -eq $el) {
  Write-Output ("element '{0}' not found; buttons in window:" -f $Name)
  $all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
  foreach ($b in $all) { Write-Output ("  button: '{0}'" -f $b.Current.Name) }
  exit 1
}
$pattern = $null
if ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
  $pattern.Invoke()
  Write-Output ("invoked '{0}'" -f $Name)
} else {
  Write-Output ("no invoke pattern for '{0}' (controlType={1})" -f $Name, $el.Current.ControlType.ProgrammaticName)
}
if ($OutFile -ne "") {
  Start-Sleep -Milliseconds 1500
  & "$PSScriptRoot\shot.ps1" -ProcId $ProcId -OutFile $OutFile
}
