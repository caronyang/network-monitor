param(
  [string]$Image,
  [int]$Ox, [int]$Oy,
  [string]$Rect,
  [int]$Step = 2
)
Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Bitmap]::FromFile($Image)
$p = $Rect.Split(',') | ForEach-Object { [int]$_ }
$x0 = $p[0] - $Ox; $y0 = $p[1] - $Oy
$w = $p[2]; $h = $p[3]
for ($y = 0; $y -lt $h; $y++) {
  $line = ""
  for ($x = 0; $x -lt $w; $x += $Step) {
    $ink = $false
    for ($dx = 0; $dx -lt $Step -and ($x + $dx) -lt $w; $dx++) {
      $c = $bmp.GetPixel($x0 + $x + $dx, $y0 + $y)
      if (($c.R + $c.G + $c.B) / 3 -lt 170) { $ink = $true; break }
    }
    $line += $(if ($ink) { "#" } else { "." })
  }
  Write-Output $line
}
$bmp.Dispose()
