# Generate Chrome extension icons (icon16/32/48/128.png) from the app icon source.
# Prefers the source PNG (UI\Resources\Icons\geng.png) because GDI+ cannot load
# PNG-compressed .ico frames via Image.FromFile; falls back to guage.ico otherwise.
# NOTE: keep this file ASCII-only (Windows PowerShell 5.1 reads BOM-less .ps1 as ANSI).
# Since 2026-09 the extension lives in a single source dir: Guage.Chrome.
# WebExtensions\Chrome is an NTFS junction pointing at it, so writing here
# updates both load paths.
Add-Type -AssemblyName System.Drawing

$scriptRoot = $PSScriptRoot
$sourceImage = Join-Path $scriptRoot '..\UI\Resources\Icons\geng.png'
$sourceIco = Join-Path $scriptRoot '..\UI\Resources\Icons\guage.ico'

$SourceIconPath = $null
if (Test-Path $sourceImage) {
    $SourceIconPath = $sourceImage
} elseif (Test-Path $sourceIco) {
    $SourceIconPath = $sourceIco
} else {
    throw "No icon source found (checked geng.png and guage.ico)"
}

function Export-ExtensionIcon {
    param(
        [string]$SourceIconPath,
        [string]$OutputPath,
        [int]$Size
    )

    $dir = Split-Path $OutputPath -Parent
    if ($dir) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }

    $sourceImage = [System.Drawing.Image]::FromFile($SourceIconPath)

    $canvas = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($canvas)
    $graphics.Clear([System.Drawing.Color]::FromArgb(0, 0, 0, 0))
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.DrawImage($sourceImage, 0, 0, $Size, $Size)
    $canvas.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)

    $graphics.Dispose()
    $canvas.Dispose()
    $sourceImage.Dispose()
}

$root = Join-Path $scriptRoot '..\Guage.Chrome'
$fullRoot = [System.IO.Path]::GetFullPath($root)
$iconsDir = Join-Path $fullRoot 'icons'

$sizes = @(16, 32, 48, 128)
foreach ($size in $sizes) {
    $fileName = "icon$size.png"
    Export-ExtensionIcon -SourceIconPath $SourceIconPath -OutputPath (Join-Path $iconsDir $fileName) -Size $size
    if ($size -eq 48 -or $size -eq 128) {
        Export-ExtensionIcon -SourceIconPath $SourceIconPath -OutputPath (Join-Path $fullRoot $fileName) -Size $size
    }
}

Write-Host "Generated extension icons in $fullRoot"
