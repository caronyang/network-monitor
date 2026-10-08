param(
  [string]$Image,
  [int]$Ox, [int]$Oy,          # window rect origin (screen coords)
  [int]$GridX = 608, [int]$GridY = 453  # grid pane rect (screen coords)
)
Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Bitmap]::FromFile($Image)
$gx = $GridX - $Ox
$gy = $GridY - $Oy

# --- A) status dot left margin (row 1: grid header 30px + row 46px) ---
$leftmost = -1
for ($y = $gy + 32; $y -lt $gy + 74 -and $leftmost -lt 0; $y++) {
  for ($x = $gx; $x -lt $gx + 60; $x++) {
    if ($x -lt 0 -or $y -lt 0 -or $x -ge $bmp.Width -or $y -ge $bmp.Height) { continue }
    $c = $bmp.GetPixel($x, $y)
    if ($c.G -gt 130 -and ($c.G - $c.R) -gt 60 -and ($c.G - $c.B) -gt 40) { $leftmost = $x - $gx; break }
  }
}
Write-Output ("A) green dot left margin: " + $leftmost + " px (expect ~6)")

# --- B) Today/Session two-line ink bands per row ---
# totals columns: from ~x0+0.62*width to width-14 (see Columns())
$x1 = $gx + 470
$x2 = $gx + 690
foreach ($rs in @(30, 76)) {
  $bands = @()
  $inBand = $false; $start = 0
  for ($y = $gy + $rs; $y -lt $gy + $rs + 46; $y++) {
    $ink = 0
    for ($x = $x1; $x -lt $x2; $x++) {
      if ($x -lt 0 -or $y -lt 0 -or $x -ge $bmp.Width -or $y -ge $bmp.Height) { continue }
      $c = $bmp.GetPixel($x, $y)
      $blue = (($c.B - $c.R) -gt 60 -and $c.B -gt 140)
      $orange = (($c.R - $c.B) -gt 90 -and $c.G -gt 60)
      if ($blue -or $orange) { $ink++ }
    }
    $rel = $y - ($gy + $rs)
    if ($ink -gt 2 -and -not $inBand) { $inBand = $true; $start = $rel }
    elseif ($ink -le 2 -and $inBand) { $inBand = $false; $bands += ("{0}..{1}" -f $start, ($rel - 1)) }
  }
  if ($inBand) { $bands += ("{0}..end" -f $start) }
  Write-Output ("B) row" + ($rs - 30) / 46 + " totals ink bands (rel row): " + ($bands -join ", "))
}
$bmp.Dispose()
