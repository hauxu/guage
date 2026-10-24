# 更矩 (guage) 发布包签名脚本（Ed25519，BouncyCastle）
# 用法：
#   1) 生成密钥对（仅需一次）：
#        powershell -ExecutionPolicy Bypass -File tools\sign-release.ps1 -Action GenerateKey -PrivateKey tools\keys\guage-release.key -PublicKey tools\keys\guage-release.pub
#   2) 签名发布包（每次发布执行）：
#        powershell -ExecutionPolicy Bypass -File tools\sign-release.ps1 -Zip update.zip -PrivateKey tools\keys\guage-release.key
#      产出：update.zip.sha256（校验和）与 update.zip.sha256.sig（签名），随 GitHub Release 一起上传。
# 说明：私钥只应存在于发布机，绝不提交仓库；公钥需嵌入 Updater 源码（UpdateSignatureVerifier.PublicKeyV1）。
param(
    [string]$Action = "Sign",
    [Parameter(Mandatory = $false)][string]$Zip,
    [Parameter(Mandatory = $false)][string]$PrivateKey,
    [Parameter(Mandatory = $false)][string]$PublicKey
)

$ErrorActionPreference = "Stop"

$dllPath = Join-Path $PSScriptRoot "..\packages\Portable.BouncyCastle.1.8.9\lib\net40\BouncyCastle.Crypto.dll"
if (-not (Test-Path $dllPath)) {
    throw "找不到 BouncyCastle 程序集：$dllPath"
}

Add-Type -Path $dllPath

# 内嵌 C# 封装：避免 PowerShell 5.1 对 New-Object 构造参数的绑定歧义
Add-Type -TypeDefinition @"
using System;
using System.IO;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

public static class GuageSigning
{
    public static string GeneratePrivateKeyBase64()
    {
        var gen = new Ed25519KeyPairGenerator();
        gen.Init(new Org.BouncyCastle.Crypto.KeyGenerationParameters(new SecureRandom(), 256));
        var pair = gen.GenerateKeyPair();
        return Convert.ToBase64String(((Ed25519PrivateKeyParameters)pair.Private).GetEncoded());
    }

    public static string GetPublicKeyBase64(string privateKeyBase64)
    {
        var priv = new Ed25519PrivateKeyParameters(Convert.FromBase64String(privateKeyBase64), 0);
        return Convert.ToBase64String(priv.GeneratePublicKey().GetEncoded());
    }

    public static string SignFile(string privateKeyBase64, string filePath)
    {
        var priv = new Ed25519PrivateKeyParameters(Convert.FromBase64String(privateKeyBase64), 0);
        var signer = new Ed25519Signer();
        signer.Init(true, priv);
        byte[] data = File.ReadAllBytes(filePath);
        signer.BlockUpdate(data, 0, data.Length);
        return Convert.ToBase64String(signer.GenerateSignature());
    }

    public static bool VerifyFile(string publicKeyBase64, string filePath, string signatureBase64)
    {
        var pub = new Ed25519PublicKeyParameters(Convert.FromBase64String(publicKeyBase64), 0);
        var signer = new Ed25519Signer();
        signer.Init(false, pub);
        byte[] data = File.ReadAllBytes(filePath);
        signer.BlockUpdate(data, 0, data.Length);
        return signer.VerifySignature(Convert.FromBase64String(signatureBase64));
    }
}
"@ -ReferencedAssemblies $dllPath

function Write-KeyFile($path, $content) {
    $dir = Split-Path $path -Parent
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    [System.IO.File]::WriteAllText($path, $content)
}

if ($Action -eq "GenerateKey") {
    if (-not $PrivateKey -or -not $PublicKey) {
        throw "GenerateKey 需要 -PrivateKey 与 -PublicKey 参数"
    }
    if (Test-Path $PrivateKey) { throw "私钥文件已存在，拒绝覆盖：$PrivateKey" }

    $privB64 = [GuageSigning]::GeneratePrivateKeyBase64()
    $pubB64 = [GuageSigning]::GetPublicKeyBase64($privB64)

    Write-KeyFile $PrivateKey $privB64
    Write-KeyFile $PublicKey $pubB64

    Write-Host "密钥对已生成："
    Write-Host "  私钥（保管好，绝不提交）：$PrivateKey"
    Write-Host "  公钥（嵌入 Updater 源码）：$PublicKey"
    Write-Host "公钥内容：$pubB64"
    exit 0
}

if (-not $Zip -or -not $PrivateKey) {
    throw "签名需要 -Zip 与 -PrivateKey 参数"
}
if (-not (Test-Path $Zip)) { throw "找不到发布包：$Zip" }
if (-not (Test-Path $PrivateKey)) { throw "找不到私钥：$PrivateKey（可先执行 GenerateKey）" }

# 1) 计算 SHA-256 校验和（与更新器 UpdatePackageVerifier 格式一致：hash 两个空格 文件名）
$hash = (Get-FileHash $Zip -Algorithm SHA256).Hash.ToLowerInvariant()
$shaFile = "$Zip.sha256"
"$hash  $(Split-Path $Zip -Leaf)" | Set-Content -Path $shaFile -Encoding ascii

# 2) 用私钥签名校验和文件
$privB64 = [System.IO.File]::ReadAllText($PrivateKey).Trim()
$signatureB64 = [GuageSigning]::SignFile($privB64, $shaFile)

$sigFile = "$shaFile.sig"
[System.IO.File]::WriteAllText($sigFile, $signatureB64)

# 3) 自检：用公钥验证签名
$pubB64 = [GuageSigning]::GetPublicKeyBase64($privB64)
if (-not [GuageSigning]::VerifyFile($pubB64, $shaFile, $signatureB64)) {
    throw "签名自检失败，请检查脚本与 BouncyCastle 版本"
}

Write-Host "签名完成（自检通过）："
Write-Host "  校验和：$shaFile"
Write-Host "  签名：  $sigFile"
Write-Host "请将两者与 $Zip 一起上传到 GitHub Release。"
