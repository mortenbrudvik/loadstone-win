# Regenerates the .ico assets. Run with the 64-bit PowerShell:
#   & 'C:\Program Files\PowerShell\7\pwsh.exe' -File tools/icons/generate.ps1
# The icon is a lodestone: a polished black pebble, broad at the top and narrowing toward one
# corner, lit from the top left with a soft highlight and a faint reflection along its lower rim.
# tray-dark.ico  : white pebble silhouette, for the dark taskbar
# tray-light.ico : near-black pebble silhouette, for the light taskbar
# loadstone.ico  : the app icon, the shaded pebble on a transparent ground
param([string]$OutDir = (Join-Path $PSScriptRoot '..\..\src\Loadstone.UI\Assets'))

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
# the frame the way Windows app icons do (about 84 percent) or the tray glyph does (about 90 percent).
function Set-World($g, [int]$size, [float]$fill) {
    $m = New-Object System.Drawing.Drawing2D.Matrix
    $m.Scale($size / 64.0, $size / 64.0)
    $m.Translate(35.0, 34.5)
    $m.Scale($fill, $fill)
    $m.Translate(-35.0, -34.5)
    $g.Transform = $m
    $m.Dispose()
}

function Draw-Silhouette($g, [int]$size, [System.Drawing.Color]$color) {
    Set-World $g $size 1.2
    $path = New-PebblePath
    $brush = New-Object System.Drawing.SolidBrush $color
    $g.FillPath($brush, $path)
    $brush.Dispose(); $path.Dispose()
    $g.ResetTransform()
}

function Draw-Pebble($g, [int]$size) {
    Set-World $g $size 1.12
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
    $shineBlend.Colors = @(
        [System.Drawing.Color]::FromArgb(0, 255, 255, 255),
        [System.Drawing.Color]::FromArgb(36, 255, 255, 255),
        [System.Drawing.Color]::FromArgb(140, 255, 255, 255))
    $shineBlend.Positions = [single[]]@(0.0, 0.45, 1.0)
    $shine.InterpolationColors = $shineBlend
    $g.SetClip($path)
    $g.FillPath($shine, $spot)
    $g.ResetClip()
    $shine.Dispose(); $spot.Dispose()

    # A crisp highlight along the top edge and a faint reflection along the lower rim.
    $top = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(82, 255, 255, 255)), 1.4
    $top.StartCap = [System.Drawing.Drawing2D.LineCap]::Round; $top.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $topPath = Add-Bezier (New-Object System.Drawing.Drawing2D.GraphicsPath) @(22, 13, 32, 9, 46, 12, 54, 22)
    $g.DrawPath($top, $topPath)
    $rim = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(128, 154, 162, 170)), 1.2
    $rim.StartCap = [System.Drawing.Drawing2D.LineCap]::Round; $rim.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $rimPath = Add-Bezier (New-Object System.Drawing.Drawing2D.GraphicsPath) @(57, 36, 56, 46, 48, 55, 36, 58)
    $g.DrawPath($rim, $rimPath)
    $top.Dispose(); $topPath.Dispose(); $rim.Dispose(); $rimPath.Dispose(); $path.Dispose()
    $g.ResetTransform()
}

New-Item -ItemType Directory -Force $OutDir | Out-Null

Write-Ico (Join-Path $OutDir 'tray-dark.ico') @(16, 20, 24, 32, 48) { param($g, $s) Draw-Silhouette $g $s ([System.Drawing.Color]::White) }
Write-Ico (Join-Path $OutDir 'tray-light.ico') @(16, 20, 24, 32, 48) { param($g, $s) Draw-Silhouette $g $s ([System.Drawing.Color]::FromArgb(255, 31, 31, 31)) }
Write-Ico (Join-Path $OutDir 'loadstone.ico') @(16, 24, 32, 48, 64, 128, 256) { param($g, $s) Draw-Pebble $g $s }
