using Core.Librarys;
using System;
using System.Security.Cryptography;
using System.Text;

namespace Guage.Tests
{
    /// <summary>
    /// 解锁密码哈希：新格式带盐，旧格式（裸 SHA256）仍要能验证通过，否则老用户的密码会失效。
    /// </summary>
    public static class PasswordHasherTests
    {
        public static void RunAll()
        {
            Run("密码哈希-新格式可验证", TestRoundTrip);
            Run("密码哈希-相同密码两次不同", TestSaltDiffers);
            Run("密码哈希-旧格式兼容", TestLegacyAccepted);
            Run("密码哈希-脏数据不抛异常", TestGarbage);
        }

        private static void TestRoundTrip()
        {
            string stored = PasswordHasher.Create("abc123");
            Assert.IsTrue(stored.StartsWith("pbkdf2-sha256$"), "应使用新格式");
            Assert.IsTrue(PasswordHasher.Verify("abc123", stored), "正确密码应通过");
            Assert.IsFalse(PasswordHasher.Verify("abc124", stored), "错误密码应拒绝");
            Assert.IsFalse(PasswordHasher.Verify("", stored), "空密码应拒绝");
        }

        private static void TestSaltDiffers()
        {
            string a = PasswordHasher.Create("same");
            string b = PasswordHasher.Create("same");
            Assert.IsTrue(a != b, "同样的密码两次生成应不同（有盐）");
            Assert.IsTrue(PasswordHasher.Verify("same", a) && PasswordHasher.Verify("same", b), "两者都应能验证");
        }

        private static void TestLegacyAccepted()
        {
            string legacy = LegacySha256("oldpass");
            Assert.IsTrue(PasswordHasher.IsLegacyFormat(legacy), "旧格式应被识别");
            Assert.IsTrue(PasswordHasher.Verify("oldpass", legacy), "旧格式密码应通过");
            Assert.IsFalse(PasswordHasher.Verify("oldpas", legacy), "旧格式错误密码应拒绝");
        }

        private static void TestGarbage()
        {
            Assert.IsFalse(PasswordHasher.Verify("x", null), "null 不应通过");
            Assert.IsFalse(PasswordHasher.Verify("x", ""), "空串不应通过");
            Assert.IsFalse(PasswordHasher.Verify("x", "pbkdf2-sha256$0$AAA$BBB"), "迭代次数非法不应通过");
            Assert.IsFalse(PasswordHasher.Verify("x", "pbkdf2-sha256$1000$***$***"), "非法 base64 不应通过");
            Assert.IsFalse(PasswordHasher.Verify("x", "随便一段字符串"), "随机串不应通过");
        }

        private static string LegacySha256(string text)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (byte b in bytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }

        private static void Run(string name, Action test) => Program.Run(name, test);
    }
}
