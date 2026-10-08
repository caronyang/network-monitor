param(
  [string]$Image,
  [int]$Ox, [int]$Oy,
  [string]$Rects   # "x,y,w,h|x,y,w,h" in screen coords
)
Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Bitmap]::FromFile($Image)
foreach ($r in $Rects.Split('|')) {
  $p = $r.Split(',') | ForEach-Object { [int]$_ }
  $x0 = $p[0] - $Ox + 3; $y0 = $p[1] - $Oy + 3
  $w = $p[2] - 6; $h = $p[3] - 6
  $top = -1; $bot = -1; $ink = 0
  for ($y = $y0; $y -lt $y0 + $h; $y++) {
    $rowInk = 0
    for ($x = $x0; $x -lt $x0 + $w; $x++) {
      if ($x -lt 0 -or $y -lt 0 -or $x -ge $bmp.Width -or $y -ge $bmp.Height) { continue }
      $c = $bmp.GetPixel($x, $y)
      if (($c.R + $c.G + $c.B) / 3 -lt 170) { $rowInk++ }
    }
    if ($rowInk -gt 1) {
      if ($top -lt 0) { $top = $y - $y0 }
      $bot = $y - $y0
      $ink += $rowInk
    }
  }
  Write-Output ("rect " + $r + " -> text rows " + $top + ".." + $bot + " ink=" + $ink)
}
$bmp.Dispose()
