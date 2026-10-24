param(
    [Parameter(Mandatory = $true)]
    [string]$ZipPath
)

$zip = Resolve-Path -LiteralPath $ZipPath
$fileName = Split-Path -Leaf $zip
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = "$zip.sha256"
Set-Content -LiteralPath $checksumPath -Value $hash -Encoding ascii -NoNewline
Write-Host "Package: $fileName"
Write-Host "SHA256 : $hash"
Write-Host "Wrote  : $checksumPath"
Write-Host ""
Write-Host "发布到 GitHub Release 时请同时上传："
Write-Host "  1. $fileName"
Write-Host "  2. $fileName.sha256"
