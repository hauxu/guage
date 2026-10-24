using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Updater
{
    public static class UpdatePackageVerifier
    {
        private static readonly Regex HexHashRegex = new Regex("^[0-9a-fA-F]{64}$", RegexOptions.Compiled);

        public static string ComputeSha256Hex(string filePath)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(filePath))
            {
                byte[] hash = sha256.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        public static bool TryParseExpectedHash(string checksumText, string packageFileName, out string expectedHash)
        {
            expectedHash = null;
            if (string.IsNullOrWhiteSpace(checksumText))
            {
                return false;
            }

            string[] lines = checksumText
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            string packageName = packageFileName?.Trim();
            bool hasFileName = !string.IsNullOrEmpty(packageName);
            string matchedNameHash = null;
            int entryLines = 0;

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                //  纯哈希行：只在校验文件仅含单条记录时接受（多文件汇总时无法确认归属）
                if (HexHashRegex.IsMatch(line))
                {
                    if (matchedNameHash == null)
                    {
                        expectedHash = line.ToLowerInvariant();
                    }
                    entryLines++;
                    continue;
                }

                string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && HexHashRegex.IsMatch(parts[0]))
                {
                    entryLines++;
                    string entryName = parts[parts.Length - 1].Trim().TrimStart('*');
                    if (hasFileName && entryName.Equals(packageName, StringComparison.OrdinalIgnoreCase))
                    {
                        //  文件名精确匹配优先（多文件 SHA256SUMS 的权威来源）
                        matchedNameHash = parts[0].ToLowerInvariant();
                        expectedHash = matchedNameHash;
                    }
                }
            }

            if (expectedHash != null)
            {
                //  命中文件名匹配：直接采用；纯哈希行：仅当文件里没有多条记录时才可信
                if (matchedNameHash != null || entryLines <= 1)
                {
                    return true;
                }
                expectedHash = null;
            }

            return false;
        }

        public static bool VerifyFile(string filePath, string expectedHash)
        {
            if (string.IsNullOrWhiteSpace(expectedHash) || !File.Exists(filePath))
            {
                return false;
            }

            string actualHash = ComputeSha256Hex(filePath);
            return string.Equals(actualHash, expectedHash.Trim().ToLowerInvariant(), StringComparison.Ordinal);
        }
    }
}
