# Generates assets\app.ico (16-256 px). Run from anywhere: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'assets\app.ico'
New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([System.Drawing.Color]::Transparent)
    $s = [single]$size
    function P([double]$x, [double]$y) { New-Object System.Drawing.PointF ([single]($x * $s)), ([single]($y * $s)) }

    # Rounded-square tile in the app accent color.
    $m = 0.04 * $s; $d = 0.42 * $s; $w = $s - 2 * $m
    $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tile.AddArc($m, $m, $d, $d, 180, 90); $tile.AddArc($m + $w - $d, $m, $d, $d, 270, 90)
    $tile.AddArc($m + $w - $d, $m + $w - $d, $d, $d, 0, 90); $tile.AddArc($m, $m + $w - $d, $d, $d, 90, 90); $tile.CloseFigure()
    $fill = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0, 95, 184))
    $g.FillPath($fill, $tile)

    # White pointer arrow.
    $arrow = [System.Drawing.PointF[]]@((P .27 .17), (P .27 .70), (P .40 .58), (P .49 .78), (P .59 .74), (P .50 .54), (P .68 .54))
    $g.FillPolygon([System.Drawing.Brushes]::White, $arrow)

    # Record dot, with a ring in the tile color so it separates from the arrow.
    $cx = 0.69 * $s; $cy = 0.69 * $s; $r = 0.19 * $s; $ring = 0.045 * $s
    $ringBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0, 95, 184))
    $g.FillEllipse($ringBrush, $cx - $r - $ring, $cy - $r - $ring, 2 * ($r + $ring), 2 * ($r + $ring))
    $dot = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(232, 60, 44))
    $g.FillEllipse($dot, $cx - $r, $cy - $r, 2 * $r, 2 * $r)
    $g.Dispose(); return $bmp
}

function Get-Dib($bmp) {
    # 32-bit BGRA DIB (bottom-up) plus 1-bit AND mask, as stored in .ico image entries.
    $w = $bmp.Width; $h = $bmp.Height
    $rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
    $data = $bmp.LockBits($rect, 'ReadOnly', [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixels = New-Object byte[] ($w * $h * 4)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length); $bmp.UnlockBits($data)
    $ms = New-Object System.IO.MemoryStream; $bw = New-Object System.IO.BinaryWriter $ms
    $bw.Write([int]40); $bw.Write([int]$w); $bw.Write([int](2 * $h)); $bw.Write([int16]1); $bw.Write([int16]32)
    $bw.Write([int]0); $bw.Write([int]($w * $h * 4)); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
    for ($y = $h - 1; $y -ge 0; $y--) { $bw.Write($pixels, $y * $w * 4, $w * 4) }
    $maskRow = [int]([math]::Ceiling($w / 32.0) * 4)
    $bw.Write((New-Object byte[] ($maskRow * $h)))
    $bw.Flush(); return , $ms.ToArray()
}

function Get-Png($bmp) {
    $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); return , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 256
$images = @()
foreach ($size in $sizes) {
    $bmp = New-IconBitmap $size
    $bytes = if ($size -ge 256) { Get-Png $bmp } else { Get-Dib $bmp }
    $images += , @($size, $bytes)
    $bmp.Dispose()
}

$fs = [System.IO.File]::Create($out); $bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([int16]0); $bw.Write([int16]1); $bw.Write([int16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $size = $img[0]; $bytes = $img[1]
    $bw.Write([byte]($(if ($size -ge 256) { 0 } else { $size }))); $bw.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
    $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]$bytes.Length); $bw.Write([int]$offset)
    $offset += $bytes.Length
}
foreach ($img in $images) { $bw.Write([byte[]]$img[1]) }
$bw.Close(); $fs.Close()
Write-Host "Wrote $out"
