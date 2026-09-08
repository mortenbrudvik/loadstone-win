# Regenerates the .ico assets. Run with the 64-bit PowerShell:
#   & 'C:\Program Files\PowerShell\7\pwsh.exe' -File tools/icons/generate.ps1
# tray-dark.ico  : white glyph, for the dark taskbar
# tray-light.ico : near-black glyph, for the light taskbar
# loadstone.ico  : the app icon, a white glyph on a blue rounded square
param([string]$OutDir = (Join-Path $PSScriptRoot '..\..\src\Loadstone.UI\Assets'))

Add-Type -AssemblyName System.Drawing

function New-PngFrame([int]$size, [scriptblock]$draw) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    & $draw $g $size
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

# An ICO is a header, one directory entry per image, then the images. PNG-compressed entries are
# supported since Vista, which is what lets a 256 px frame fit.
function Write-Ico([string]$path, [int[]]$sizes, [scriptblock]$draw) {
    $frames = foreach ($s in $sizes) { ,@($s, (New-PngFrame $s $draw)) }
    $stream = [System.IO.File]::Create($path)
    $writer = New-Object System.IO.BinaryWriter $stream
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $s = $frame[0]; $bytes = $frame[1]
        $dim = if ($s -ge 256) { 0 } else { $s }
        $writer.Write([byte]$dim); $writer.Write([byte]$dim); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$bytes.Length); $writer.Write([uint32]$offset)
        $offset += $bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame[1]) }
    $writer.Dispose(); $stream.Dispose()
    "wrote $path"
}

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc($x, $y, $r, $r, 180, 90)
    $p.AddArc($x + $w - $r, $y, $r, $r, 270, 90)
    $p.AddArc($x + $w - $r, $y + $h - $r, $r, $r, 0, 90)
    $p.AddArc($x, $y + $h - $r, $r, $r, 90, 90)
    $p.CloseFigure()
    return $p
}

# The glyph: a rounded rectangle split into a left and a right pane.
function Draw-Glyph($g, [int]$size, [System.Drawing.Color]$color) {
    $stroke = [math]::Max(1.0, $size / 9.0)
    $pen = New-Object System.Drawing.Pen $color, $stroke
    $inset = $stroke
    $x = $inset; $y = $size * 0.2 + $inset; $w = $size - 2 * $inset; $h = $size * 0.6 - 2 * $inset
    $path = New-RoundedPath $x $y $w $h ([math]::Max(2.0, $size * 0.18))
    $g.DrawPath($pen, $path)
    $mid = $x + $w / 2
    $g.DrawLine($pen, $mid, $y, $mid, $y + $h)
    $pen.Dispose(); $path.Dispose()
}

New-Item -ItemType Directory -Force $OutDir | Out-Null

Write-Ico (Join-Path $OutDir 'tray-dark.ico') @(16, 20, 24, 32, 48) { param($g, $s) Draw-Glyph $g $s ([System.Drawing.Color]::White) }
Write-Ico (Join-Path $OutDir 'tray-light.ico') @(16, 20, 24, 32, 48) { param($g, $s) Draw-Glyph $g $s ([System.Drawing.Color]::FromArgb(255, 31, 31, 31)) }
Write-Ico (Join-Path $OutDir 'loadstone.ico') @(16, 24, 32, 48, 64, 128, 256) {
    param($g, $s)
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0, 120, 212))
    $bg = New-RoundedPath 0 0 $s $s ($s * 0.22)
    $g.FillPath($brush, $bg)
    $inner = [int]($s * 0.72)
    $g.TranslateTransform(($s - $inner) / 2, ($s - $inner) / 2)
    Draw-Glyph $g $inner ([System.Drawing.Color]::White)
    $g.ResetTransform()
    $brush.Dispose(); $bg.Dispose()
}
