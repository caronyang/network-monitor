param(
  [string]$Exe = "D:\99_Temp\opencode\networkmonitor\src\NetworkMonitor\bin\Release\net10.0-windows10.0.19041\NetworkMonitor.exe",
  [string[]]$Modes = @("", "grid", "summary", "tray", "1"),
  [int]$Warmup = 15,
  [int]$Seconds = 40,
  [int]$Repeat = 2
)
foreach ($m in $Modes) {
  if ($m -eq "") { Remove-Item env:NM_NOPAINT -ErrorAction SilentlyContinue; $label = "normal" }
  else { $env:NM_NOPAINT = $m; $label = "NM_NOPAINT=$m" }
  $runs = @()
  for ($i = 0; $i -lt $Repeat; $i++) {
    $p = $null
    for ($try = 0; $try -lt 5 -and $null -eq $p; $try++) {
      try { $p = Start-Process -FilePath $Exe -PassThru -ErrorAction Stop }
      catch {
        if ($try -eq 0) { Write-Output ("  dbg exe='{0}' exists={1}" -f $Exe, (Test-Path $Exe)) }
        Start-Sleep -Milliseconds 800
      }
    }
    if ($null -eq $p) { Write-Output "  dbg start failed"; continue }
    Start-Sleep -Seconds $Warmup
    $p.Refresh(); $b = $p.TotalProcessorTime
    Start-Sleep -Seconds $Seconds
    $p.Refresh(); $a = $p.TotalProcessorTime
    $runs += ($a - $b).TotalMilliseconds / ($Seconds * 10.0)
    Stop-Process -Id $p.Id -Force
    Start-Sleep -Milliseconds 900
  }
  $min = ($runs | Measure-Object -Minimum).Minimum
  $max = ($runs | Measure-Object -Maximum).Maximum
  Write-Output ("{0,-22} min={1,6:N2}%  max={2,6:N2}%   [{3}]" -f $label, $min, $max,
    (($runs | ForEach-Object { "{0:N2}" -f $_ }) -join ", "))
}
Remove-Item env:NM_NOPAINT -ErrorAction SilentlyContinue
