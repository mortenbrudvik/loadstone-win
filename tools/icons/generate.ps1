# Regenerates the .ico assets. Run with the 64-bit PowerShell:
#   & 'C:\Program Files\PowerShell\7\pwsh.exe' -File tools/icons/generate.ps1
# The icon is a lodestone: a smooth black river stone, wider than it is tall, full and round on the
# left and drawn out to a rounded point on the right, with a satin rather than a glassy finish. It
# is lit from the top left: a broad soft light on the upper-left shoulder, a faint sheen across the
# flat crown, a soft reflection along the top edge out to the point, the stone's side showing as a
# lighter band along the lower-left edge with a thin bright line where the ground throws light back,
# and, under the app icon only, a soft shadow.
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
# The stone is traced from the reference photo, scaled so it is 50 units wide and 34 tall about
# (32, 33): four anchors at top, right, bottom and left with handles of uneven length. The long
# handles along the top and bottom toward the right and the short vertical ones at the right anchor
# are what draw the stone out to its rounded point; the left is full and round.
function New-PebblePath {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddBezier(27.0, 15.8, 46.0, 15.8, 57.2, 31.5, 57.2, 36.0)
    $p.AddBezier(57.2, 36.0, 57.2, 40.5, 49.5, 50.2, 30.0, 50.2)
    $p.AddBezier(30.0, 50.2, 17.5, 50.2, 7.2, 43.8, 7.2, 33.8)
    $p.AddBezier(7.2, 33.8, 7.2, 23.8, 15.0, 15.8, 27.0, 15.8)
    $p.CloseFigure()
    return $p
}

# Strokes a stretch of the stone's edge, inset a little, between two bearings measured clockwise
# from three o'clock about the centre. The outline is not an ellipse, so this flattens an inset copy
# of it and draws the points that fall in the range, in bearing order.
function Draw-Rim($g, $path, $pen, [float]$inset, [float]$from, [float]$to) {
    $copy = [System.Drawing.Drawing2D.GraphicsPath]$path.Clone()
    $m = New-Object System.Drawing.Drawing2D.Matrix
    $k = 1.0 - $inset / 24.0
    $m.Translate(32.0, 32.0); $m.Scale($k, $k); $m.Translate(-32.0, -32.0)
    $copy.Transform($m); $m.Dispose()
    $copy.Flatten($null, 0.05)
    $run = foreach ($pt in $copy.PathPoints) {
        $bearing = [math]::Atan2($pt.Y - 32.0, $pt.X - 32.0) * 180.0 / [math]::PI
        if ($bearing -lt 0) { $bearing += 360.0 }
        if ($bearing -ge $from -and $bearing -le $to) { [pscustomobject]@{ Bearing = $bearing; Point = $pt } }
    }
    $sorted = @($run | Sort-Object Bearing)
    # Two passes at just over half strength, the second stopping short of both ends, so the stroke
    # fades in and out instead of stopping with a round cap, the way light on a rim does.
    $soft = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb([int]($pen.Color.A * 0.55), $pen.Color)), $pen.Width
    $soft.StartCap = $pen.StartCap; $soft.EndCap = $pen.EndCap; $soft.LineJoin = $pen.LineJoin
    $margin = [math]::Min(12.0, ($to - $from) / 4.0)
    foreach ($pass in @(@($from, $to), @(($from + $margin), ($to - $margin)))) {
        $points = [System.Drawing.PointF[]]@($sorted | Where-Object { $_.Bearing -ge $pass[0] -and $_.Bearing -le $pass[1] } | ForEach-Object { $_.Point })
        if ($points.Count -ge 2) { $g.DrawLines($soft, $points) }
    }
    $soft.Dispose(); $copy.Dispose()
}

# A rotated ellipse: the pools of light are all this shape. $angle is in degrees about its centre.
function New-Pool([float]$cx, [float]$cy, [float]$w, [float]$h, [float]$angle) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddEllipse($cx - $w / 2, $cy - $h / 2, $w, $h)
    $m = New-Object System.Drawing.Drawing2D.Matrix
    $m.RotateAt($angle, (New-Object System.Drawing.PointF $cx, $cy))
    $p.Transform($m); $m.Dispose()
    return $p
}

# Fills a pool with light that is $alpha strong at its centre and gone at its edge, the fade
# front-loaded so the pool reads as a soft reflection rather than a flat disc.
function Fill-Pool($g, $pool, [int]$alpha, [int]$r = 255, [int]$green = 255, [int]$b = 255) {
    $brush = New-Object System.Drawing.Drawing2D.PathGradientBrush $pool
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend 3
    $blend.Colors = @(
        [System.Drawing.Color]::FromArgb(0, $r, $green, $b),
        [System.Drawing.Color]::FromArgb([int]($alpha * 0.35), $r, $green, $b),
        [System.Drawing.Color]::FromArgb($alpha, $r, $green, $b))
    $blend.Positions = [single[]]@(0.0, 0.5, 1.0)
    $brush.InterpolationColors = $blend
    $g.FillPath($brush, $pool)
    $brush.Dispose()
}

# World transform: unit square to the frame, with the pebble enlarged about its centre so it fills
# the frame the way Windows app icons do (about 84 percent) or a tray glyph does (about 90 percent).
function Set-World($g, [int]$size, [float]$fill) {
    $m = New-Object System.Drawing.Drawing2D.Matrix
    $m.Scale($size / 64.0, $size / 64.0)
    $m.Translate(32.0, 32.0)
    $m.Scale($fill, $fill)
    $m.Translate(-32.0, -32.0)
    $g.Transform = $m
    $m.Dispose()
}

# $edge is 0 for the app icon. Higher values strengthen the highlights and add a grey outline so the
# stone separates from a dark taskbar at 16 px; the ground shadow is drawn only at 0, since the tray
# variants sit on the bar and want nothing beneath them.
function Draw-Pebble($g, [int]$size, [float]$fill, [float]$edge) {
    Set-World $g $size $fill
    $path = New-PebblePath

    if ($edge -eq 0) {
        $shadow = New-Pool 34 51 46 9 0
        Fill-Pool $g $shadow 110 0 0 0
        $shadow.Dispose()
    }

    # Body: near-black at the rim, lifting to a dark grey where the light lands up and to the left.
    $body = New-Object System.Drawing.Drawing2D.PathGradientBrush $path
    $body.CenterPoint = New-Object System.Drawing.PointF 24, 27
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend 4
    $blend.Colors = @(
        [System.Drawing.Color]::FromArgb(255, 12, 13, 15),
        [System.Drawing.Color]::FromArgb(255, 26, 27, 30),
        [System.Drawing.Color]::FromArgb(255, 46, 48, 52),
        [System.Drawing.Color]::FromArgb(255, 74, 77, 82))
    $blend.Positions = [single[]]@(0.0, 0.14, 0.5, 1.0)
    $body.InterpolationColors = $blend
    $g.FillPath($body, $path)
    $body.Dispose()

    # Satin light, all soft pools clipped to the stone: a broad one on the upper-left shoulder with a
    # brighter heart where the crown meets the rim, a faint sheen across the flat crown, and a faint
    # bounce from the ground on the lower right.
    $g.SetClip($path)
    $crown = New-Pool 22 24 34 20 -22
    Fill-Pool $g $crown ([int][math]::Min(170, 95 + 35 * $edge)) 230 233 238
    $shoulder = New-Pool 14 22 18 12 -35
    Fill-Pool $g $shoulder ([int][math]::Min(220, 150 + 40 * $edge)) 235 238 242
    $sheen = New-Pool 34 31 26 12 -12
    Fill-Pool $g $sheen 26 220 224 230
    $bounce = New-Pool 44 45 22 7 20
    Fill-Pool $g $bounce 30 200 206 214
    $crown.Dispose(); $shoulder.Dispose(); $sheen.Dispose(); $bounce.Dispose()
    $g.ResetClip()

    # Rim lights, by bearing clockwise from three o'clock. The top edge, 235 to 345 degrees, carries
    # a soft wide reflection set a little in from the edge with a thinner, brighter line at the edge
    # itself over its middle; the lower edge, 20 to 200 degrees, from the point round the bottom to
    # the left, a thin line where the ground throws light back.
    function New-RimPen([int]$alpha, [float]$width, [int]$r, [int]$green, [int]$b) {
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb($alpha, $r, $green, $b)), $width
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round; $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        return $pen
    }
    $topSoft = New-RimPen ([int][math]::Min(150, 55 + 20 * $edge)) (2.4 + 1.0 * $edge) 205 210 216
    Draw-Rim $g $path $topSoft 1.8 235.0 345.0
    $topLine = New-RimPen ([int][math]::Min(190, 90 + 50 * $edge)) (0.8 + 0.6 * $edge) 255 255 255
    Draw-Rim $g $path $topLine 0.6 255.0 335.0
    # The side of the stone shows along the lower-left edge as a wide soft band, with the thin bright
    # line at the edge itself; the band runs from the point round to the left, the line a little less.
    $side = New-RimPen ([int][math]::Min(120, 50 + 12 * $edge)) (3.0 + 1.0 * $edge) 170 176 184
    Draw-Rim $g $path $side 1.9 20.0 200.0
    $lower = New-RimPen ([int][math]::Min(210, 110 + 50 * $edge)) (1.0 + 1.0 * $edge) 200 206 214
    Draw-Rim $g $path $lower 0.7 30.0 195.0
    $topSoft.Dispose(); $topLine.Dispose(); $side.Dispose(); $lower.Dispose()

    if ($edge -gt 0) {
        $outline = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb([int](32 * $edge), 150, 158, 166)), (1.0 * $edge)
        $g.DrawPath($outline, $path)
        $outline.Dispose()
    }
    $path.Dispose()
    $g.ResetTransform()
}

New-Item -ItemType Directory -Force $OutDir | Out-Null

Write-Ico (Join-Path $OutDir 'loadstone.ico') @(16, 24, 32, 48, 64, 128, 256) { param($g, $s) Draw-Pebble $g $s 1.08 0 }
Write-Ico (Join-Path $OutDir 'tray-dark.ico') @(16, 20, 24, 32, 48) { param($g, $s) Draw-Pebble $g $s 1.15 1.6 }
Write-Ico (Join-Path $OutDir 'tray-light.ico') @(16, 20, 24, 32, 48) { param($g, $s) Draw-Pebble $g $s 1.15 0.5 }

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
    Draw-Pebble $pg $pebbleSize 1.08 $(if ($light) { 0 } else { 0.5 })
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
$appIcon = { param($g, $s) Draw-Pebble $g $s 1.08 0 }

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
$unplatedIcon = { param($g, $s) Draw-Pebble $g $s 1.08 1.6 }

foreach ($target in 16, 24, 32, 48, 256) {
    Write-Png (Join-Path $MsixAssetDir "Square44x44Logo.targetsize-$target.png") $target $appIcon
    Write-Png (Join-Path $MsixAssetDir "Square44x44Logo.targetsize-${target}_altform-unplated.png") $target $unplatedIcon
}
