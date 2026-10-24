# Generate the three guage desktop app icon files from a source image:
#   guage.ico     - exe icon / about-page logo (PNG-compressed frames 16..256)
#   guage32.ico   - main window + tray icon (BMP frames 16/24/32 for NotifyIcon compat)
#   guagebusy.ico - tray busy-state icon (red-tinted variant of the normal icon)
# Usage: powershell -ExecutionPolicy Bypass -File scripts\generate-app-icons.ps1 [-SourceImage <png path>]
# NOTE: keep this file ASCII-only. Windows PowerShell 5.1 reads BOM-less .ps1 as ANSI,
# and non-ASCII comments can corrupt parsing.
param(
    [string]$SourceImage = (Join-Path (Join-Path $PSScriptRoot '..\UI\Resources\Icons') 'geng.png')
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $SourceImage)) {
    throw "Source image not found: $SourceImage"
}

$OutDir = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\UI\Resources\Icons'))

function New-ResizedBitmap([System.Drawing.Image]$src, [int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($src, 0, 0, $size, $size)
    $g.Dispose()
    return $bmp
}

function Tint-Bitmap([System.Drawing.Bitmap]$bmp, [double]$r, [double]$g, [double]$b) {
    for ($y = 0; $y -lt $bmp.Height; $y++) {
        for ($x = 0; $x -lt $bmp.Width; $x++) {
            $p = $bmp.GetPixel($x, $y)
            $nr = [int][Math]::Min(255.0, [double]$p.R * $r)
            $ng = [int][Math]::Min(255.0, [double]$p.G * $g)
            $nb = [int][Math]::Min(255.0, [double]$p.B * $b)
            $bmp.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($p.A, $nr, $ng, $nb))
        }
    }
}

# Encode a bitmap as a classic BMP icon frame (32bpp BGRA + zero AND mask)
function Get-BmpIcoFrame([System.Drawing.Bitmap]$bmp) {
    $size = $bmp.Width
    $xor = New-Object System.IO.MemoryStream
    for ($y = $size - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $size; $x++) {
            $p = $bmp.GetPixel($x, $y)
            $xor.WriteByte($p.B)
            $xor.WriteByte($p.G)
            $xor.WriteByte($p.R)
            $xor.WriteByte($p.A)
        }
    }
    $rowBytes = [int][Math]::Ceiling($size / 8.0)
    $paddedRowBytes = [int][Math]::Ceiling($rowBytes / 4.0) * 4
    $maskLen = $paddedRowBytes * $size

    $frame = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($frame)
    $bw.Write([int]40)                      # biSize
    $bw.Write([int]$size)                   # biWidth
    $bw.Write([int]($size * 2))             # biHeight (XOR + AND)
    $bw.Write([int16]1)                     # biPlanes
    $bw.Write([int16]32)                    # biBitCount
    $bw.Write([int]0)                       # biCompression
    $bw.Write([int]($xor.Length + $maskLen))# biSizeImage
    $bw.Write([int]0)                       # XPelsPerMeter
    $bw.Write([int]0)                       # YPelsPerMeter
    $bw.Write([int]0)                       # ClrUsed
    $bw.Write([int]0)                       # ClrImportant
    $xor.Position = 0
    $xor.CopyTo($frame)
    $zeros = New-Object byte[] $maskLen
    $frame.Write($zeros, 0, $maskLen)
    return $frame.ToArray()
}

function Get-PngIcoFrame([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return $ms.ToArray()
}

function New-IcoEntryBytes([int]$dim, [byte[]]$data, [int]$offset) {
    if ($dim -ge 256) { $dim = 0 }   # ICO convention: 0 means 256
    $e = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($e)
    $bw.Write([byte]$dim)
    $bw.Write([byte]$dim)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([int16]1)
    $bw.Write([int16]32)
    $bw.Write([int]$data.Length)
    $bw.Write([int]$offset)
    return $e.ToArray()
}

function Write-IcoFile([string]$path, [byte[][]]$frames, [int[]]$sizes) {
    $count = $sizes.Count
    $offset = 6 + 16 * $count
    $offsets = New-Object int[] $count
    for ($i = 0; $i -lt $count; $i++) {
        $offsets[$i] = $offset
        $offset += $frames[$i].Length
    }

    $out = New-Object System.IO.MemoryStream
    $out.WriteByte(0); $out.WriteByte(0)              # reserved
    $out.WriteByte(1); $out.WriteByte(0)              # type = icon
    $out.WriteByte([byte]($count -band 255)); $out.WriteByte([byte]([int](($count -shr 8) -band 255)))
    for ($i = 0; $i -lt $count; $i++) {
        $entry = New-IcoEntryBytes $sizes[$i] $frames[$i] $offsets[$i]
        $out.Write($entry, 0, $entry.Length)
    }
    for ($i = 0; $i -lt $count; $i++) {
        $out.Write($frames[$i], 0, $frames[$i].Length)
    }
    [System.IO.File]::WriteAllBytes($path, $out.ToArray())
}

$src = [System.Drawing.Image]::FromFile($SourceImage)
Write-Host ("Source: {0} ({1}x{2})" -f $SourceImage, $src.Width, $src.Height)
Write-Host ("Output dir: {0}" -f $OutDir)

# guage.ico: PNG-compressed multi-size frames
$pngSizes = New-Object int[] 7
$pngSizes[0] = 256; $pngSizes[1] = 128; $pngSizes[2] = 64; $pngSizes[3] = 48
$pngSizes[4] = 32; $pngSizes[5] = 24; $pngSizes[6] = 16
$pngFrames = New-Object 'byte[][]' 7
for ($i = 0; $i -lt 7; $i++) {
    $bmp = New-ResizedBitmap $src $pngSizes[$i]
    $pngFrames[$i] = Get-PngIcoFrame $bmp
    $bmp.Dispose()
}
Write-IcoFile (Join-Path $OutDir 'guage.ico') $pngFrames $pngSizes
Write-Host "Generated guage.ico (256/128/64/48/32/24/16, PNG frames)"

# guage32.ico: BMP frames for NotifyIcon / WPF window icon compatibility
$smallSizes = New-Object int[] 3
$smallSizes[0] = 32; $smallSizes[1] = 24; $smallSizes[2] = 16
$smallFrames = New-Object 'byte[][]' 3
for ($i = 0; $i -lt 3; $i++) {
    $bmp = New-ResizedBitmap $src $smallSizes[$i]
    $smallFrames[$i] = Get-BmpIcoFrame $bmp
    $bmp.Dispose()
}
Write-IcoFile (Join-Path $OutDir 'guage32.ico') $smallFrames $smallSizes
Write-Host "Generated guage32.ico (32/24/16, BMP frames)"

# guagebusy.ico: red-tinted variant (channel ratios match the original busy icon)
$busyFrames = New-Object 'byte[][]' 3
for ($i = 0; $i -lt 3; $i++) {
    $bmp = New-ResizedBitmap $src $smallSizes[$i]
    Tint-Bitmap $bmp 0.93 0.64 0.65
    $busyFrames[$i] = Get-BmpIcoFrame $bmp
    $bmp.Dispose()
}
Write-IcoFile (Join-Path $OutDir 'guagebusy.ico') $busyFrames $smallSizes
Write-Host "Generated guagebusy.ico (32/24/16, red tint)"

$src.Dispose()

# Validate: load every generated icon with System.Drawing
foreach ($name in @('guage.ico', 'guage32.ico', 'guagebusy.ico')) {
    $path = Join-Path $OutDir $name
    $ico = New-Object System.Drawing.Icon($path)
    Write-Host ("OK {0}: default frame {1}x{2}, file {3} bytes" -f $name, $ico.Width, $ico.Height, (Get-Item $path).Length)
    $ico.Dispose()
}
