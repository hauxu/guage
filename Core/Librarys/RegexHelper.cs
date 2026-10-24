using System;
using System.Text.RegularExpressions;

namespace Core.Librarys
{
    public static class RegexHelper
    {
        private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// 是否正则匹配成功
        /// </summary>
        public static bool IsMatch(string input, string pattern)
        {
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(pattern))
            {
                return false;
            }

            try
            {
                return Regex.IsMatch(input, pattern, RegexOptions.None, MatchTimeout);
            }
            catch (RegexMatchTimeoutException)
            {
                Logger.Error($"正则表达式匹配超时（>{MatchTimeout.TotalMilliseconds}ms），规则未生效：{pattern}");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error("正则表达式错误，规则未生效：" + pattern + "。" + ex.Message);
                return false;
            }
        }
    }
}
