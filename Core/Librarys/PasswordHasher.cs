using System;
using System.Security.Cryptography;
using System.Text;

namespace Core.Librarys
{
    /// <summary>
    /// 时长限制自定义密码的存储格式。
    ///
    /// 旧版直接存 SHA256(password) 的十六进制，没盐，相同密码得到的哈希一样，
    /// 配置泄露后可以被彩虹表反查。新格式为 pbkdf2-sha256$迭代次数$盐$哈希，
    /// 校验时仍接受旧格式，验证通过后调用方可以顺手升级成新格式。
    /// </summary>
    public static class PasswordHasher
    {
        private const string Prefix = "pbkdf2-sha256";
        private const int Iterations = 100000;
        private const int SaltBytes = 16;
        private const int HashBytes = 32;

        /// <summary>
        /// 生成带盐的密码哈希。
        /// </summary>
        public static string Create(string password)
        {
            if (password == null)
            {
                throw new ArgumentNullException(nameof(password));
            }

            byte[] salt = new byte[SaltBytes];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            byte[] hash = Derive(password, salt, Iterations, HashBytes);
            return Prefix + "$" + Iterations + "$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(hash);
        }

        /// <summary>
        /// 校验密码。新旧两种格式都支持。
        /// </summary>
        public static bool Verify(string password, string stored)
        {
            if (string.IsNullOrEmpty(stored))
            {
                return false;
            }

            if (!IsSaltedFormat(stored))
            {
                return VerifyLegacy(password, stored);
            }

            string[] parts = stored.Split('$');
            if (parts.Length != 4)
            {
                return false;
            }

            int iterations;
            if (!int.TryParse(parts[1], out iterations) || iterations <= 0)
            {
                return false;
            }

            byte[] salt;
            byte[] expected;
            try
            {
                salt = Convert.FromBase64String(parts[2]);
                expected = Convert.FromBase64String(parts[3]);
            }
            catch (FormatException)
            {
                return false;
            }

            if (salt.Length == 0 || expected.Length == 0)
            {
                return false;
            }

            return FixedTimeEquals(Derive(password, salt, iterations, expected.Length), expected);
        }

        /// <summary>
        /// 是否是旧的裸哈希格式（64 位十六进制）。
        /// </summary>
        public static bool IsLegacyFormat(string stored)
        {
            if (string.IsNullOrEmpty(stored) || stored.Length != 64)
            {
                return false;
            }

            foreach (char c in stored)
            {
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsSaltedFormat(string stored)
        {
            return stored.StartsWith(Prefix + "$", StringComparison.Ordinal);
        }

        private static bool VerifyLegacy(string password, string stored)
        {
            if (!IsLegacyFormat(stored))
            {
                return false;
            }

            byte[] actual;
            using (var sha = SHA256.Create())
            {
                actual = sha.ComputeHash(Encoding.UTF8.GetBytes(password ?? string.Empty));
            }

            var sb = new StringBuilder(actual.Length * 2);
            foreach (byte b in actual)
            {
                sb.Append(b.ToString("x2"));
            }

            return FixedTimeEquals(Encoding.UTF8.GetBytes(sb.ToString()), Encoding.UTF8.GetBytes(stored.ToLowerInvariant()));
        }

        private static byte[] Derive(string password, byte[] salt, int iterations, int length)
        {
            using (var kdf = new Rfc2898DeriveBytes(password ?? string.Empty, salt, iterations, HashAlgorithmName.SHA256))
            {
                return kdf.GetBytes(length);
            }
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }

            int diff = 0;
            for (int i = 0; i < a.Length; i++)
            {
                diff |= a[i] ^ b[i];
            }

            return diff == 0;
        }
    }
}
