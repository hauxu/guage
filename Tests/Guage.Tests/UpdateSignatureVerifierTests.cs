using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using System;
using System.IO;

namespace Guage.Tests
{
    public static class UpdateSignatureVerifierTests
    {
        //  测试专用密钥对（与生产公钥常量解耦：无论 PublicKeyV1 换什么，测试都用自己的密钥对）
        private const string TestPrivateKeyBase64 = "0+4B2b6yWHUBH7G+L012ZSMg2RQ3ABEG/ZWHfhhaGgU=";
        private const string TestPublicKeyBase64 = "9ydqWZreo6y6aBk+JdxfZg+RRgjJkwJoxlKapCRgolQ=";

        public static void RunAll()
        {
            Run("签名-有效签名通过", TestValidSignature);
            Run("签名-篡改校验和文件后拒绝", TestTampered);
            Run("签名-文件缺失拒绝", TestMissingFiles);
        }

        private static string TempFile(string name, string content)
        {
            string path = Path.Combine(Path.GetTempPath(), "guage_sig_" + Guid.NewGuid().ToString("N") + "_" + name);
            File.WriteAllText(path, content, new System.Text.UTF8Encoding(false));
            return path;
        }

        private static string Sign(byte[] data)
        {
            var priv = new Ed25519PrivateKeyParameters(Convert.FromBase64String(TestPrivateKeyBase64), 0);
            var signer = new Ed25519Signer();
            signer.Init(true, priv);
            signer.BlockUpdate(data, 0, data.Length);
            return Convert.ToBase64String(signer.GenerateSignature());
        }

        private static void TestValidSignature()
        {
            string shaFile = TempFile("sha256", "abc-hash  update.zip");
            string sigFile = TempFile("sig", Sign(File.ReadAllBytes(shaFile)));
            try
            {
                Assert.IsTrue(Updater.UpdateSignatureVerifier.VerifyWithKeys(shaFile, sigFile, TestPublicKeyBase64), "有效签名应通过");
            }
            finally
            {
                File.Delete(shaFile);
                File.Delete(sigFile);
            }
        }

        private static void TestTampered()
        {
            string shaFile = TempFile("sha256", "abc-hash  update.zip");
            string sigFile = TempFile("sig", Sign(File.ReadAllBytes(shaFile)));
            try
            {
                File.WriteAllText(shaFile, "evil-hash  update.zip", new System.Text.UTF8Encoding(false));
                Assert.IsFalse(Updater.UpdateSignatureVerifier.VerifyWithKeys(shaFile, sigFile, TestPublicKeyBase64), "内容被篡改应拒绝");
            }
            finally
            {
                File.Delete(shaFile);
                File.Delete(sigFile);
            }
        }

        private static void TestMissingFiles()
        {
            Assert.IsFalse(Updater.UpdateSignatureVerifier.VerifyWithKeys("nonexistent.sha256", "nonexistent.sig", TestPublicKeyBase64));
        }

        private static void Run(string name, Action test) => Program.Run(name, test);
    }
}
