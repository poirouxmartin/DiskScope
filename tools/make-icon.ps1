# Draws the DiskScope application icon (a drive and a magnifier) and writes a multi-size .ico.
# Usage: powershell -File tools/make-icon.ps1 [-Accent '#3B6EA5'] [-Out src/DiskScope.App/Assets/DiskScope.ico]
param(
    [string]$Accent = '#3B6EA5',
    [string]$Ink = '#1E242C',
    [string]$Out = (Join-Path $PSScriptRoot '..\src\DiskScope.App\Assets\DiskScope.ico')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 32.0
    $accentColor = [System.Drawing.ColorTranslator]::FromHtml($Accent)
    $inkColor = [System.Drawing.ColorTranslator]::FromHtml($Ink)

    # Drive body and its activity slot.
    $body = New-RoundedRect (1.5 * $s) (5 * $s) (23 * $s) (19 * $s) (4.5 * $s)
    $g.FillPath((New-Object System.Drawing.SolidBrush $accentColor), $body)
    $slot = New-RoundedRect (5.5 * $s) (17.5 * $s) (9 * $s) (2.6 * $s) (1.3 * $s)
    $g.FillPath([System.Drawing.Brushes]::White, $slot)

    # Magnifier: white lens with an ink ring, and a handle.
    $penW = [Math]::Max(1.6, 2.6 * $s)
    $ring = New-Object System.Drawing.Pen $inkColor, $penW
    $g.FillEllipse([System.Drawing.Brushes]::White, 15 * $s, 13 * $s, 12 * $s, 12 * $s)
    $g.DrawEllipse($ring, 15 * $s, 13 * $s, 12 * $s, 12 * $s)
    $handle = New-Object System.Drawing.Pen $inkColor, ([Math]::Max(2.0, 3.4 * $s))
    $handle.StartCap = 'Round'; $handle.EndCap = 'Round'
    $g.DrawLine($handle, 25.2 * $s, 23.2 * $s, 29.5 * $s, 27.5 * $s)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($sz in $sizes) { , (New-IconPng $sz) }

$dir = Split-Path -Parent $Out
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
$fs = [System.IO.File]::Create($Out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $len = $images[$i].Length
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$len); $w.Write([UInt32]$offset)
    $offset += $len
}
foreach ($img in $images) { $w.Write($img) }
$w.Dispose()
Write-Output "Wrote $Out ($($sizes.Count) sizes, accent $Accent)"
