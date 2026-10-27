using Core.Models.AppObserver;
using Core.Models.Config;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Core.Librarys
{
    /// <summary>
    /// 时长限制纯逻辑（从 TimeLimitServicer 抽取，便于单元测试）：
    /// 规则匹配、规则键、路径归一化与随机密码生成。
    /// </summary>
    public static class TimeLimitLogic
    {
        public static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            try
            {
                return System.IO.Path.GetFullPath(path.Trim());
            }
            catch
            {
                return path.Trim();
            }
        }

        /// <summary>
        /// 清洗历史脏数据：本地路径里的双反斜杠（如 "D:\\\\Apps\\\\x.exe"）折叠为单反斜杠。
        /// UNC 路径（\\\\server\\share）开头的双反斜杠是协议必需，原样保留
        /// </summary>
        public static string SanitizeExecutablePath(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.Contains(@"\\"))
            {
                return path;
            }

            //  成对折叠：4 道开头自然折叠为 2 道（UNC 正确形态）
            string collapsed = path.Replace(@"\\", @"\");
            if (path.StartsWith(@"\\", StringComparison.Ordinal)
                && !collapsed.StartsWith(@"\\", StringComparison.Ordinal))
            {
                //  原 UNC 前缀折叠后只剩一道时补回，保证恰好两道开头
                collapsed = @"\" + collapsed;
            }
            return collapsed;
        }

        /// <summary>
        /// 两个路径的文件名是否相同（忽略大小写）。用于版本化安装目录的兼容判断。
        /// </summary>
        private static bool IsSameFileName(string a, string b)
        {
            try
            {
                return string.Equals(
                    System.IO.Path.GetFileName(NormalizePath(a)),
                    System.IO.Path.GetFileName(NormalizePath(b)),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static string GetRuleKey(TimeLimitRuleModel rule)
        {
            if (rule == null)
            {
                return string.Empty;
            }

            return rule.ProcessName + "|" + (rule.ExecutablePath ?? string.Empty);
        }

        /// <summary>
        /// 按进程名（+可选路径精确匹配）解析规则键；未命中时返回 name|path 兜底键。
        /// </summary>
        public static string ResolveRuleKey(List<TimeLimitRuleModel> rules, string processName, string executablePath)
        {
            if (rules != null)
            {
                foreach (TimeLimitRuleModel rule in rules)
                {
                    if (rule == null || !string.Equals(rule.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(rule.ExecutablePath)
                        && !string.IsNullOrWhiteSpace(executablePath)
                        && !string.Equals(
                            NormalizePath(rule.ExecutablePath),
                            NormalizePath(executablePath),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    return GetRuleKey(rule);
                }
            }

            return processName + "|" + (executablePath ?? string.Empty);
        }

        /// <summary>
        /// 查找匹配当前前台应用的已启用规则。
        /// </summary>
        public static TimeLimitRuleModel FindRule(List<TimeLimitRuleModel> rules, AppInfo app)
        {
            if (app == null || string.IsNullOrEmpty(app.Process) || rules == null)
            {
                return null;
            }

            foreach (TimeLimitRuleModel rule in rules)
            {
                if (rule == null || !rule.Enabled || string.IsNullOrEmpty(rule.ProcessName))
                {
                    continue;
                }

                if (!string.Equals(rule.ProcessName, app.Process, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(rule.ExecutablePath)
                    && !string.IsNullOrWhiteSpace(app.ExecutablePath))
                {
                    bool samePath = string.Equals(
                        NormalizePath(rule.ExecutablePath),
                        NormalizePath(app.ExecutablePath),
                        StringComparison.OrdinalIgnoreCase);

                    //  有的软件装在带版本号的子目录里（如 QQMusic\QQMusic2276.01.13.46\QQMusic.exe），
                    //  升级后完整路径就变了，只比全路径会让规则彻底失效。进程名已经对上时，
                    //  再退一步只比文件名
                    if (!samePath && !IsSameFileName(rule.ExecutablePath, app.ExecutablePath))
                    {
                        continue;
                    }
                }

                return rule;
            }

            return null;
        }

        /// <summary>
        /// 生成 n 位数字字母混合随机密码（排除易混淆字符，保证至少 1 位数字 + 1 位字母；加密随机源）。
        /// </summary>
        public static string GenerateRandomPassword(int length)
        {
            if (length < 2)
            {
                length = 6;
            }

            const string digits = "23456789";
            const string letters = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz";
            var chars = new char[length];
            using (var rng = new RNGCryptoServiceProvider())
            {
                //  长度预留余量：循环内最后一位读取 i*4+2 起的 4 字节，避免越界
                var randomBytes = new byte[length * 4 + 8];
                rng.GetBytes(randomBytes);

                chars[0] = digits[Math.Abs(BitConverter.ToInt32(randomBytes, 0)) % digits.Length];
                chars[1] = letters[Math.Abs(BitConverter.ToInt32(randomBytes, 4)) % letters.Length];
                for (int i = 2; i < length; i++)
                {
                    int selector = BitConverter.ToInt32(randomBytes, i * 4) & 1;
                    string pool = selector == 0 ? digits : letters;
                    int index = Math.Abs(BitConverter.ToInt32(randomBytes, i * 4 + 2)) % pool.Length;
                    chars[i] = pool[index];
                }
            }

            // 打乱顺序（继续使用加密随机源）
            using (var rng = new RNGCryptoServiceProvider())
            {
                var shuffleBytes = new byte[length * 4];
                rng.GetBytes(shuffleBytes);
                for (int i = length - 1; i > 0; i--)
                {
                    int j = Math.Abs(BitConverter.ToInt32(shuffleBytes, i * 4)) % (i + 1);
                    char tmp = chars[i]; chars[i] = chars[j]; chars[j] = tmp;
                }
            }

            return new string(chars);
        }
    }
}
