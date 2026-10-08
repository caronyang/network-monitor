param([string]$OutPrefix = "shot")
$tools = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path (Split-Path -Parent $tools) "src\NetworkMonitor\bin\Release\net10.0-windows10.0.19041\NetworkMonitor.exe"
$p = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 6

function Get-WinHandle([int]$procId, [string]$title) {
  $out = & "$tools\enumwin.ps1" -ProcId $procId
  foreach ($line in $out) {
    if ($line -match '^(\d+)\|vis=True\|.*\|title=(.*)$') {
      if ($Matches[2] -eq $title) { return [int64]$Matches[1] }
    }
  }
  return 0
}

$mainH = Get-WinHandle $p.Id "Network Monitor"
Write-Output "main handle: $mainH"
& "$tools\uiclick.ps1" -Handle "$mainH" -Name "Statistics"
Start-Sleep -Seconds 2
$statsH = Get-WinHandle $p.Id "Statistics"
Write-Output "stats handle: $statsH"
if ($statsH -gt 0) {
  Start-Sleep -Seconds 6
  & "$tools\shoth.ps1" -Handle "$statsH" -OutFile "$tools\..\${OutPrefix}_stats.png"
}
