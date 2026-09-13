# Regenerates the .ico assets. Run with the 64-bit PowerShell:
#   & 'C:\Program Files\PowerShell\7\pwsh.exe' -File tools/icons/generate.ps1
# The icon is a lodestone: a polished black pebble, broad at the top and narrowing toward one
# corner, lit from the top left with a soft highlight and a faint reflection along its lower rim.
# loadstone.ico  : the app icon, the shaded pebble on a transparent ground
# tray-dark.ico  : the same pebble for the dark taskbar, with a stronger edge so it separates from the bar
# tray-light.ico : the same pebble for the light taskbar, with a lighter touch of that edge
# Splash\splash-{dark,light}.scale-{100,150,200}.png : the startup splash, the pebble above the wordmark
param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\..\src\Loadstone.UI\Assets'),
    [string]$MsixAssetDir = (Join-Path $PSScriptRoot '..\..\packaging\msix\Assets')
)

Add-Type -AssemblyName System.Drawing

function New-PngFrame([int]$size, [scriptblock]$draw) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
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

# The MSIX asset set. Unlike the .ico files these are single-size PNGs on a transparent ground; the
# tile plate supplies the background, so they use the app-icon treatment ($edge 0) rather than either
# tray variant's stronger rim.
function Write-Png([string]$path, [int]$size, [scriptblock]$draw) {
    [System.IO.File]::WriteAllBytes($path, (New-PngFrame $size $draw))
    "wrote $path"
}

# Everything is drawn in a 64-unit square and scaled to the frame, so strokes scale with the icon.
function Add-Bezier($path, [float[]]$pts) {
    for ($i = 0; $i + 7 -lt $pts.Count; $i += 6) {
        $path.AddBezier($pts[$i], $pts[$i + 1], $pts[$i + 2], $pts[$i + 3], $pts[$i + 4], $pts[$i + 5], $pts[$i + 6], $pts[$i + 7])
    }
    return $path
}

function New-PebblePath {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-Bezier $p @(
        24, 12, 38, 8, 54, 18, 58, 30,
        58, 30, 61, 40, 52, 54, 40, 57,
        40, 57, 30, 59, 18, 60, 14, 52,
        14, 52, 10, 44, 10, 26, 16, 18,
        16, 18, 18, 15, 21, 13, 24, 12) | Out-Null
    $p.CloseFigure()
    return $p
}

# World transform: unit square to the frame, with the pebble enlarged about its centre so it fills
# the frame the way Windows app icons do (about 84 percent) or a tray glyph does (about 90 percent).
function Set-World($g, [int]$size, [float]$fill) {
    $m = New-Object System.Drawing.Drawing2D.Matrix
    $m.Scale($size / 64.0, $size / 64.0)
    $m.Translate(35.0, 34.5)
    $m.Scale($fill, $fill)
    $m.Translate(-35.0, -34.5)
    $g.Transform = $m
    $m.Dispose()
}

# $edge is 0 for the app icon. Higher values thicken the highlights and add a grey outline so the
# stone separates from a dark taskbar at 16 px.
function Draw-Pebble($g, [int]$size, [float]$fill, [float]$edge) {
    Set-World $g $size $fill
    $path = New-PebblePath

    # Body: near-black at the rim rising to a grey sheen where the light lands.
    $body = New-Object System.Drawing.Drawing2D.PathGradientBrush $path
    $body.CenterPoint = New-Object System.Drawing.PointF 27, 24
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend 4
    $blend.Colors = @(
        [System.Drawing.Color]::FromArgb(255, 13, 15, 18),
        [System.Drawing.Color]::FromArgb(255, 22, 25, 28),
        [System.Drawing.Color]::FromArgb(255, 44, 48, 54),
        [System.Drawing.Color]::FromArgb(255, 92, 97, 104))
    $blend.Positions = [single[]]@(0.0, 0.18, 0.58, 1.0)
    $body.InterpolationColors = $blend
    $g.FillPath($body, $path)
    $body.Dispose()

    # Specular pool on the shoulder: a soft rotated ellipse fading to nothing.
    $spot = New-Object System.Drawing.Drawing2D.GraphicsPath
    $spot.AddEllipse(14.0, 16.0, 26.0, 16.0)
    $rot = New-Object System.Drawing.Drawing2D.Matrix
    $rot.RotateAt(-18.0, (New-Object System.Drawing.PointF 27, 24))
    $spot.Transform($rot); $rot.Dispose()
    $shine = New-Object System.Drawing.Drawing2D.PathGradientBrush $spot
    $shineBlend = New-Object System.Drawing.Drawing2D.ColorBlend 3
    $shineAlpha = [int][math]::Min(220, 140 + 40 * $edge)
    $shineBlend.Colors = @(
        [System.Drawing.Color]::FromArgb(0, 255, 255, 255),
        [System.Drawing.Color]::FromArgb(36, 255, 255, 255),
        [System.Drawing.Color]::FromArgb($shineAlpha, 255, 255, 255))
    $shineBlend.Positions = [single[]]@(0.0, 0.45, 1.0)
    $shine.InterpolationColors = $shineBlend
    $g.SetClip($path)
    $g.FillPath($shine, $spot)
    $g.ResetClip()
    $shine.Dispose(); $spot.Dispose()

    # A crisp highlight along the top edge and a faint reflection along the lower rim.
    $topWidth = 1.4 + 1.6 * $edge; $rimWidth = 1.2 + 1.8 * $edge
    $topAlpha = [int][math]::Min(230, 82 + 60 * $edge); $rimAlpha = [int][math]::Min(230, 128 + 50 * $edge)
    $top = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb($topAlpha, 255, 255, 255)), $topWidth
    $top.StartCap = [System.Drawing.Drawing2D.LineCap]::Round; $top.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $topPath = Add-Bezier (New-Object System.Drawing.Drawing2D.GraphicsPath) @(22, 13, 32, 9, 46, 12, 54, 22)
    $g.DrawPath($top, $topPath)
    $rim = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb($rimAlpha, 154, 162, 170)), $rimWidth
    $rim.StartCap = [System.Drawing.Drawing2D.LineCap]::Round; $rim.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $rimPath = Add-Bezier (New-Object System.Drawing.Drawing2D.GraphicsPath) @(57, 36, 56, 46, 48, 55, 36, 58)
    $g.DrawPath($rim, $rimPath)
    if ($edge -gt 0) {
        $outline = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb([int](70 * $edge), 138, 146, 153)), (1.2 * $edge)
        $g.DrawPath($outline, $path)
        $outline.Dispose()
    }
    $top.Dispose(); $topPath.Dispose(); $rim.Dispose(); $rimPath.Dispose(); $path.Dispose()
    $g.ResetTransform()
}

New-Item -ItemType Directory -Force $OutDir | Out-Null

Write-Ico (Join-Path $OutDir 'loadstone.ico') @(16, 24, 32, 48, 64, 128, 256) { param($g, $s) Draw-Pebble $g $s 1.12 0 }
Write-Ico (Join-Path $OutDir 'tray-dark.ico') @(16, 20, 24, 32, 48) { param($g, $s) Draw-Pebble $g $s 1.2 1.6 }
Write-Ico (Join-Path $OutDir 'tray-light.ico') @(16, 20, 24, 32, 48) { param($g, $s) Draw-Pebble $g $s 1.2 0.5 }

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

# The startup splash: the pebble above the wordmark on a rounded plate, one PNG per theme and scale.
# WPF's SplashScreen draws a bitmap one image pixel to one screen pixel and never scales it, so the app
# picks the file by the primary display's DPI (StartupSplash.ResourceName) and every measurement here
# is in 100 percent units times $scale. The plate is opaque and the corners outside it transparent:
# SplashScreen is a layered window, so the rounding is the image's own alpha. On the dark plate the
# near-black pebble takes tray-light.ico's lighter edge so it does not sink into the background; the
# tray-dark edge is tuned for 16 px and at this size reads as a thick grey outline.
function Write-Splash([string]$path, [double]$scale, [bool]$light) {
    $w = [int][math]::Round(320 * $scale); $h = [int][math]::Round(200 * $scale)
    $bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $plateColor = if ($light) { [System.Drawing.Color]::FromArgb(255, 243, 243, 243) } else { [System.Drawing.Color]::FromArgb(255, 32, 32, 32) }
    $borderColor = if ($light) { [System.Drawing.Color]::FromArgb(40, 0, 0, 0) } else { [System.Drawing.Color]::FromArgb(48, 255, 255, 255) }
    $textColor = if ($light) { [System.Drawing.Color]::FromArgb(255, 27, 27, 27) } else { [System.Drawing.Color]::FromArgb(255, 255, 255, 255) }
    $border = [float]$scale
    $plate = New-RoundedRect ($border / 2) ($border / 2) ($w - $border) ($h - $border) (12 * $scale)
    $fill = New-Object System.Drawing.SolidBrush $plateColor
    $g.FillPath($fill, $plate)
    $pen = New-Object System.Drawing.Pen $borderColor, $border
    $g.DrawPath($pen, $plate)
    $fill.Dispose(); $pen.Dispose(); $plate.Dispose()

    $pebbleSize = [int][math]::Round(88 * $scale)
    $pebble = New-Object System.Drawing.Bitmap $pebbleSize, $pebbleSize, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pg = [System.Drawing.Graphics]::FromImage($pebble)
    $pg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $pg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $pg.Clear([System.Drawing.Color]::Transparent)
    Draw-Pebble $pg $pebbleSize 1.12 $(if ($light) { 0 } else { 0.5 })
    $pg.Dispose()
    $g.DrawImage($pebble, [int](($w - $pebbleSize) / 2), [int][math]::Round(30 * $scale), $pebbleSize, $pebbleSize)
    $pebble.Dispose()

    # GDI+ swaps a missing family for Microsoft Sans Serif without a word, and these PNGs are committed.
    $font = New-Object System.Drawing.Font 'Segoe UI Semibold', ([float](28 * $scale)), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    if ($font.Name -ne 'Segoe UI Semibold') { throw "Segoe UI Semibold is not installed; GDI+ substituted $($font.Name)" }
    $text = New-Object System.Drawing.SolidBrush $textColor
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $g.DrawString('Loadstone', $font, $text, (New-Object System.Drawing.RectangleF 0, ([float](126 * $scale)), $w, ([float](48 * $scale))), $format)
    $font.Dispose(); $text.Dispose(); $format.Dispose()

    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    "wrote $path"
}

$splashDir = Join-Path $OutDir 'Splash'
New-Item -ItemType Directory -Force $splashDir | Out-Null

foreach ($scale in 100, 150, 200) {
    foreach ($theme in 'dark', 'light') {
        Write-Splash (Join-Path $splashDir "splash-$theme.scale-$scale.png") ($scale / 100.0) ($theme -eq 'light')
    }
}

New-Item -ItemType Directory -Force $MsixAssetDir | Out-Null

# Square44x44Logo and Square150x150Logo at every scale the Store asks for, StoreLogo at two, and the
# targetsize variants for the taskbar. Without an _altform-unplated variant Windows draws the icon on
# a coloured backplate.
$appIcon = { param($g, $s) Draw-Pebble $g $s 1.12 0 }

foreach ($scale in 100, 125, 150, 200, 400) {
    Write-Png (Join-Path $MsixAssetDir "Square44x44Logo.scale-$scale.png")   ([int][math]::Round(44 * $scale / 100.0))  $appIcon
    Write-Png (Join-Path $MsixAssetDir "Square150x150Logo.scale-$scale.png") ([int][math]::Round(150 * $scale / 100.0)) $appIcon
}

foreach ($scale in 100, 200) {
    Write-Png (Join-Path $MsixAssetDir "StoreLogo.scale-$scale.png") ([int][math]::Round(50 * $scale / 100.0)) $appIcon
}

# The _altform-unplated variants are drawn straight onto the taskbar with no plate behind them, and
# Windows ships one such file regardless of the user's taskbar theme (there is no light/dark
# selection outside high-contrast mode). tray-dark.ico's edge of 1.6 is tuned for exactly this
# "separate from a dark bar at 16 px" case; on a light bar the pebble is dark enough that it reads
# fine either way, so the same value is the one that works on both.
$unplatedIcon = { param($g, $s) Draw-Pebble $g $s 1.12 1.6 }

foreach ($target in 16, 24, 32, 48, 256) {
    Write-Png (Join-Path $MsixAssetDir "Square44x44Logo.targetsize-$target.png") $target $appIcon
    Write-Png (Join-Path $MsixAssetDir "Square44x44Logo.targetsize-${target}_altform-unplated.png") $target $unplatedIcon
}
