# Generates src\NetworkMonitor\assets\app.ico (multi-size, PNG-compressed entries)
param([string]$OutFile = (Join-Path $PSScriptRoot '..\src\NetworkMonitor\assets\app.ico'))

Add-Type -AssemblyName System.Drawing

function Render-IconBitmap {
    param([int]$s)
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $k = $s / 256.0

    $rect = New-Object System.Drawing.RectangleF((10 * $k), (10 * $k), (236 * $k), (236 * $k))
    $r = 58 * $k
    $d = $r * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $c1 = [System.Drawing.Color]::FromArgb(255, 24, 84, 166)
    $c2 = [System.Drawing.Color]::FromArgb(255, 13, 170, 158)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, 48.0)
    $g.FillPath($brush, $path)

    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, (17 * $k))
    $pen.StartCap = 'Round'
    $pen.EndCap = 'Round'
    $white = [System.Drawing.Brushes]::White

    # down arrow (left)
    $cx = 88 * $k
    $g.DrawLine($pen, $cx, 62 * $k, $cx, 152 * $k)
    $g.FillPolygon($white, @(
        (New-Object System.Drawing.PointF(($cx - 35 * $k), (140 * $k))),
        (New-Object System.Drawing.PointF(($cx + 35 * $k), (140 * $k))),
        (New-Object System.Drawing.PointF($cx, (188 * $k)))
    ))

    # up arrow (right)
    $cx = 168 * $k
    $g.DrawLine($pen, $cx, 194 * $k, $cx, 104 * $k)
    $g.FillPolygon($white, @(
        (New-Object System.Drawing.PointF(($cx - 35 * $k), (116 * $k))),
        (New-Object System.Drawing.PointF(($cx + 35 * $k), (116 * $k))),
        (New-Object System.Drawing.PointF($cx, (68 * $k)))
    ))

    $pen.Dispose()
    $brush.Dispose()
    $path.Dispose()
    $g.Dispose()
    return $bmp
}

$dir = Split-Path -Parent $OutFile
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$blobs = @()
foreach ($s in $sizes) {
    $bmp = Render-IconBitmap -s $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $blobs += , @($s, $ms.ToArray())
    $ms.Dispose()
}

$fs = [System.IO.File]::Create($OutFile)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$blobs.Count)
$offset = 6 + (16 * $blobs.Count)
foreach ($e in $blobs) {
    $s = [int]$e[0]
    $data = [byte[]]$e[1]
    $entrySize = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$entrySize)
    $bw.Write([byte]$entrySize)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$data.Length)
    $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($e in $blobs) { $bw.Write([byte[]]$e[1]) }
$bw.Flush()
$bw.Close()
$fs.Dispose()

Write-Output "icon written: $OutFile ($((Get-Item $OutFile).Length) bytes)"
