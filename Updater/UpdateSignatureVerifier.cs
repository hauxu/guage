using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using System;
using System.IO;

namespace Updater
{
    /// <summary>
    /// 更新包签名校验（Ed25519）：校验 .sha256 校验和文件的签名，防止发布包被篡改。
    /// 公钥由发布机通过 tools\sign-release.ps1 -Action GenerateKey 生成后嵌入本文件。
    /// </summary>
    public static class UpdateSignatureVerifier
    {
        /// <summary>
        /// 正式发布公钥（由发布机 tools\sign-release.ps1 -Action GenerateKey 生成）。
        /// 对应私钥 guage-release.key 仅存于发布机，绝不进入仓库。
        /// 若私钥泄露需要轮换：新公钥先放 PublicKeyV2 过渡发布，随后移除旧公钥。
        /// </summary>
        public const string PublicKeyV1 = "pkHmKHJ++6JB/Cx/BmTonmWvmoSY3aHmmFLyRZLSgAs=";

        /// <summary>
        /// 密钥轮换过渡期使用：新公钥先放 V2，发布若干版本后移除旧公钥并把 V2 提升为 V1。
        /// </summary>
        public const string PublicKeyV2 = "";

        /// <summary>
        /// 验证 .sha256 文件的 Ed25519 签名（使用内置公钥 V1/V2）。
        /// </summary>
        public static bool VerifySha256File(string sha256FilePath, string signatureFilePath)
        {
            return VerifyWithKeys(sha256FilePath, signatureFilePath, PublicKeyV1, PublicKeyV2);
        }

        /// <summary>
        /// 验证 .sha256 文件的 Ed25519 签名（使用调用方提供的公钥，供单元测试独立使用）。
        /// 任一公钥验证通过即返回 true。
        /// </summary>
        public static bool VerifyWithKeys(string sha256FilePath, string signatureFilePath, params string[] publicKeysBase64)
        {
            if (string.IsNullOrWhiteSpace(sha256FilePath) || string.IsNullOrWhiteSpace(signatureFilePath))
            {
                return false;
            }

            if (!File.Exists(sha256FilePath) || !File.Exists(signatureFilePath))
            {
                return false;
            }

            byte[] signature;
            try
            {
                signature = Convert.FromBase64String(File.ReadAllText(signatureFilePath).Trim());
            }
            catch
            {
                return false;
            }

            byte[] data;
            try
            {
                data = File.ReadAllBytes(sha256FilePath);
            }
            catch
            {
                return false;
            }

            string[] keys = publicKeysBase64;
            foreach (string keyBase64 in keys)
            {
                if (string.IsNullOrWhiteSpace(keyBase64))
                {
                    continue;
                }

                try
                {
                    var publicKey = new Ed25519PublicKeyParameters(Convert.FromBase64String(keyBase64), 0);
                    var signer = new Ed25519Signer();
                    signer.Init(false, publicKey);
                    signer.BlockUpdate(data, 0, data.Length);
                    if (signer.VerifySignature(signature))
                    {
                        return true;
                    }
                }
                catch
                {
                    //  单把密钥解析失败不影响其他密钥尝试
                }
            }

            return false;
        }
    }
}
