param([string]$Handle, [string]$Name)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$h = [IntPtr][int64]$Handle
$win = [System.Windows.Automation.AutomationElement]::FromHandle($h)
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)
$el = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
if ($el -eq $null) {
    # list available checkboxes to help diagnose name mismatches
    Write-Output "element '$Name' not found; checkboxes in window:"
    $all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($e in $all) {
        if ($e.Current.ControlType.ProgrammaticName -eq "ControlType.CheckBox") { Write-Output ("  " + $e.Current.Name) }
    }
    exit 1
}
$before = "unknown"
$tg = $null
if ($el.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$tg)) {
    $before = $tg.Current.ToggleState
}
$inv = $null
if ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv)) {
    $inv.Invoke()
    Write-Output "invoked '$Name'"
} elseif ($tg -ne $null) {
    $tg.Toggle()
    Write-Output "toggled '$Name'"
} else {
    Write-Output "no pattern on '$Name'"
    exit 1
}
Start-Sleep -Milliseconds 400
if ($tg -ne $null) {
    $after = $null
    if ($el.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$after)) { Write-Output ("state: " + $before + " -> " + $after.Current.ToggleState) }
}
