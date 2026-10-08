param(
  [string]$Image,
  [int]$Ox, [int]$Oy,
  [string]$Rects
)
Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Bitmap]::FromFile($Image)
foreach ($r in $Rects.Split('|')) {
  $p = $r.Split(',') | ForEach-Object { [int]$_ }
  $x0 = $p[0] - $Ox; $y0 = $p[1] - $Oy
  $w = $p[2]; $h = $p[3]
  Write-Output ("=== rect " + $r)
  $minX = 999; $maxX = -1
  for ($y = 0; $y -lt $h; $y++) {
    $rowInk = 0
    for ($x = 1; $x -lt $w - 1; $x++) {
      $c = $bmp.GetPixel($x0 + $x, $y0 + $y)
      if (($c.R + $c.G + $c.B) / 3 -lt 170) { $rowInk++; if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x } }
    }
    if ($rowInk -gt 0) { Write-Output ("  y=" + $y + " ink=" + $rowInk) }
  }
  Write-Output ("  x extent: " + $minX + ".." + $maxX)
}
$bmp.Dispose()
