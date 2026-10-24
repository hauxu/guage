using Core.Enums;
using Core.Librarys;
using Core.Models.AppObserver;
using Core.Models.Config;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Guage.Tests
{
    public static class TimeLimitLogicTests
    {
        public static void RunAll()
        {
            Run("规则匹配-进程名命中", TestFindByName);
            Run("规则匹配-禁用规则跳过", TestFindSkipsDisabled);
            Run("规则匹配-路径精确匹配", TestFindByPath);
            Run("规则键-命中与兜底", TestRuleKey);
            Run("路径归一化-相对路径与空白", TestNormalizePath);
            Run("随机密码-长度与字符集", TestPassword);
        }

        private static void TestFindByName()
        {
            var rules = new List<TimeLimitRuleModel>
            {
                new TimeLimitRuleModel { ProcessName = "Chrome", Enabled = true, ExecutablePath = string.Empty }
            };
            var app = new AppInfo(IntPtr.Zero, 1, "chrome", "desc", "", AppType.Win32);
            Assert.IsNotNull(TimeLimitLogic.FindRule(rules, app), "进程名匹配（忽略大小写）应命中");
        }

        private static void TestFindSkipsDisabled()
        {
            var rules = new List<TimeLimitRuleModel>
            {
                new TimeLimitRuleModel { ProcessName = "Chrome", Enabled = false }
            };
            var app = new AppInfo(IntPtr.Zero, 1, "chrome", "desc", "", AppType.Win32);
            Assert.IsNull(TimeLimitLogic.FindRule(rules, app), "禁用规则应跳过");
        }

        private static void TestFindByPath()
        {
            string chromePath = "C:" + System.IO.Path.DirectorySeparatorChar + "Apps" + System.IO.Path.DirectorySeparatorChar + "Chrome" + System.IO.Path.DirectorySeparatorChar + "chrome.exe";
            string otherPath = "D:" + System.IO.Path.DirectorySeparatorChar + "Other" + System.IO.Path.DirectorySeparatorChar + "chrome.exe";
            var rules = new List<TimeLimitRuleModel>
            {
                new TimeLimitRuleModel { ProcessName = "Chrome", Enabled = true, ExecutablePath = chromePath }
            };
            var rightApp = new AppInfo(IntPtr.Zero, 1, "chrome", "", chromePath, AppType.Win32);
            var wrongApp = new AppInfo(IntPtr.Zero, 2, "chrome", "", otherPath, AppType.Win32);
            Assert.IsNotNull(TimeLimitLogic.FindRule(rules, rightApp), "路径一致应命中");
            Assert.IsNull(TimeLimitLogic.FindRule(rules, wrongApp), "路径不一致应跳过");
        }

        private static void TestRuleKey()
        {
            var rule = new TimeLimitRuleModel { ProcessName = "Chrome", ExecutablePath = "C:\\chrome.exe" };
            Assert.AreEqual("Chrome|C:\\chrome.exe", TimeLimitLogic.GetRuleKey(rule));
            Assert.AreEqual("Chrome|C:\\chrome.exe",
                TimeLimitLogic.ResolveRuleKey(new List<TimeLimitRuleModel> { rule }, "chrome", "C:\\chrome.exe"),
                "命中规则应返回规则键");
            Assert.AreEqual("Foo|", TimeLimitLogic.ResolveRuleKey(new List<TimeLimitRuleModel> { rule }, "Foo", null),
                "未命中应返回兜底键");
        }

        private static void TestNormalizePath()
        {
            Assert.AreEqual(System.IO.Path.GetFullPath("chrome.exe"), TimeLimitLogic.NormalizePath(" chrome.exe "));
            Assert.IsNull(TimeLimitLogic.NormalizePath(null));
        }

        private static void TestPassword()
        {
            string p1 = TimeLimitLogic.GenerateRandomPassword(6);
            string p2 = TimeLimitLogic.GenerateRandomPassword(6);
            Assert.AreEqual(6, p1.Length, "默认长度 6");
            Assert.IsTrue(p1.Any(char.IsDigit) && p1.Any(char.IsLetter), "必须同时包含数字与字母");
            Assert.IsTrue(!p1.Contains('0') && !p1.Contains('1') && !p1.Contains('O') && !p1.Contains('I')
                && !p1.Contains('l'), "排除易混淆字符");
            Assert.IsTrue(p1 != p2, "两次生成不应相同");
        }

        private static void Run(string name, Action test) => Program.Run(name, test);
    }
}
