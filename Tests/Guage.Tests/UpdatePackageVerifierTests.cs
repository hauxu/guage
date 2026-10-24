using System;
using System.IO;
using System.Text;

namespace Guage.Tests
{
    public static class UpdatePackageVerifierTests
    {
        public static void RunAll()
        {
            Run("哈希-SHA256 计算与已知向量", TestSha256);
            Run("哈希-解析 sha256sum 格式", TestParseSha256sum);
            Run("哈希-解析单行哈希", TestParseSingleHash);
            Run("哈希-解析多行时匹配文件名", TestParseMultiFile);
            Run("哈希-非法文本拒绝", TestParseInvalid);
            Run("哈希-校验匹配与不匹配", TestVerify);
        }

        private static string TempFile(string content)
        {
            string path = Path.Combine(Path.GetTempPath(), "guage_vfy_" + Guid.NewGuid().ToString("N") + ".txt");
            //  使用不带 BOM 的 UTF-8，保证内容字节与输入完全一致（.NET Framework 的 Encoding.UTF8 带 BOM）
            File.WriteAllText(path, content, new UTF8Encoding(false));
            return path;
        }

        private static void TestSha256()
        {
            string path = TempFile("abc");
            try
            {
                string hash = Updater.UpdatePackageVerifier.ComputeSha256Hex(path);
                //  "abc" 的 SHA-256 已知值
                Assert.AreEqual("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static void TestParseSha256sum()
        {
            string line = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad  update.zip";
            Assert.IsTrue(Updater.UpdatePackageVerifier.TryParseExpectedHash(line, "update.zip", out string hash));
            Assert.AreEqual("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
        }

        private static void TestParseSingleHash()
        {
            Assert.IsTrue(Updater.UpdatePackageVerifier.TryParseExpectedHash(
                "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "update.zip", out string hash));
            Assert.AreEqual("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
        }

        private static void TestParseMultiFile()
        {
            string text = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  other.zip"
                + System.Environment.NewLine
                + "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad  update.zip";
            Assert.IsTrue(Updater.UpdatePackageVerifier.TryParseExpectedHash(text, "update.zip", out string hash),
                "多文件时应匹配指定包名");
            Assert.AreEqual("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
        }

        private static void TestParseInvalid()
        {
            Assert.IsFalse(Updater.UpdatePackageVerifier.TryParseExpectedHash("not a hash at all", "update.zip", out _));
            Assert.IsFalse(Updater.UpdatePackageVerifier.TryParseExpectedHash("", "update.zip", out _));
            Assert.IsFalse(Updater.UpdatePackageVerifier.TryParseExpectedHash(null, "update.zip", out _));
        }

        private static void TestVerify()
        {
            string path = TempFile("abc");
            try
            {
                string good = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
                Assert.IsTrue(Updater.UpdatePackageVerifier.VerifyFile(path, good), "匹配哈希应通过");
                Assert.IsFalse(Updater.UpdatePackageVerifier.VerifyFile(path, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"), "不匹配应拒绝");
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static void Run(string name, Action test) => Program.Run(name, test);
    }
}
