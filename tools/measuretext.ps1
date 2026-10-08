Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$font = New-Object System.Drawing.Font("Segoe UI", 9)
$zhRefresh = -join ([char]0x91CD, [char]0x65B0, [char]0x6574, [char]0x7406)
$zhExport = -join ([char]0x532F, [char]0x51FA, " ", "CSV")
$flags = [System.Windows.Forms.TextFormatFlags]::WordBreak -bor [System.Windows.Forms.TextFormatFlags]::TextBoxControl `
  -bor [System.Windows.Forms.TextFormatFlags]::HorizontalCenter -bor [System.Windows.Forms.TextFormatFlags]::VerticalCenter `
  -bor [System.Windows.Forms.TextFormatFlags]::HidePrefix
$big = New-Object System.Drawing.Size(2000, 100)
Write-Output "--- single-line (proposed 2000x100) ---"
foreach ($s in @("Refresh", "Export CSV", $zhRefresh, $zhExport, "Settings", "Statistics")) {
  $m = [System.Windows.Forms.TextRenderer]::MeasureText($s, $font, $big, $flags)
  Write-Output ("  '" + $s + "' -> " + $m.Width + "x" + $m.Height)
}
Write-Output "--- wrap check at candidate available widths ---"
foreach ($w in @(68, 70, 72, 76)) {
  $ps = New-Object System.Drawing.Size($w, 24)
  $r = @()
  foreach ($s in @("Refresh", "Export CSV", $zhRefresh, $zhExport)) {
    $m = [System.Windows.Forms.TextRenderer]::MeasureText($s, $font, $ps, $flags)
    $r += ("'" + $s + "'=" + $m.Width + "x" + $m.Height)
  }
  Write-Output ("  avail " + $w + ": " + ($r -join "  "))
}
