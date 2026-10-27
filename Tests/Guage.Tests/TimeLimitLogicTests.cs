using Core.Enums;
using Core.Event;
using Core.Librarys;
using Core.Models.AppObserver;
using Core.Models.Config;
using Core.Servicers.Instances;
using Core.Servicers.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Guage.Tests
{
    /// <summary>
    /// 时长限制逻辑测试：规则匹配、键计算、路径归一、密码生成，以及触发/恢复判定。
    /// 触发/恢复的可测性说明：判定写在 TimeLimitServicer 的私有计时回调 Timer_Elapsed 里，
    /// 既没有可注入的时钟也没有状态入口，不改产品代码就拆不出来；因此这里用假实现占位四个依赖接口
    /// （IData 不在该路径上，传 null），再用反射驱动私有回调，断言它同步写入的用量/标记字典。
    /// 断言只看锁内同步状态，不等毫秒级计时，结果稳定。
    /// </summary>
    public static class TimeLimitLogicTests
    {
        public static void RunAll()
        {
            Run("规则匹配-进程名命中", TestFindByName);
            Run("规则匹配-禁用规则跳过", TestFindSkipsDisabled);
            Run("规则匹配-路径精确与版本化目录", TestFindByPath);
            Run("规则键-命中与兜底", TestRuleKey);
            Run("路径归一化-相对路径与空白", TestNormalizePath);
            Run("随机密码-长度与字符集", TestPassword);
            Run("时长限制-恰好达到限制才触发", TestTriggerThresholdBoundary);
            Run("时长限制-当日标记去重", TestTriggerDedupe);
            Run("时长限制-延长后允许再次触发", TestExtensionAllowsRetrigger);
            Run("时长限制-跨天重置用量与标记", TestNewDayResetsUsageAndMarkers);
            Run("路径归一化-大小写与相对路径", TestNormalizePathCaseAndRelative);
            Run("规则匹配-同名不同安装目录仍命中", TestFindByVersionedPath);
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
            string otherPath = "D:" + System.IO.Path.DirectorySeparatorChar + "Other" + System.IO.Path.DirectorySeparatorChar + "chromium.exe";
            var rules = new List<TimeLimitRuleModel>
            {
                new TimeLimitRuleModel { ProcessName = "Chrome", Enabled = true, ExecutablePath = chromePath }
            };
            var rightApp = new AppInfo(IntPtr.Zero, 1, "chrome", "", chromePath, AppType.Win32);
            var wrongApp = new AppInfo(IntPtr.Zero, 2, "chrome", "", otherPath, AppType.Win32);
            Assert.IsNotNull(TimeLimitLogic.FindRule(rules, rightApp), "路径一致应命中");
            Assert.IsNull(TimeLimitLogic.FindRule(rules, wrongApp), "文件名不同应跳过");
        }

        /// <summary>
        /// 有的软件装在带版本号的子目录里（QQMusic2276.01.13.46 之类），升级后完整路径会变。
        /// 这种情况下只要文件名一致就应当命中，否则用户设的限制会在软件升级后永久失效。
        /// </summary>
        private static void TestFindByVersionedPath()
        {
            var rules = new List<TimeLimitRuleModel>
            {
                new TimeLimitRuleModel { ProcessName = "QQMusic", Enabled = true,
                    ExecutablePath = @"D:\QQMusic\QQMusic.exe" }
            };
            var app = new AppInfo(IntPtr.Zero, 1, "QQMusic", "",
                @"D:\QQMusic\QQMusic2276.01.13.46\QQMusic.exe", AppType.Win32);

            Assert.IsNotNull(TimeLimitLogic.FindRule(rules, app), "版本化子目录应仍命中同名规则");
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

        /// <summary>
        /// 触发阈值边界：用量 限制-1 秒不触发；恰好等于限制（不是超限）就触发。
        /// </summary>
        private static void TestTriggerThresholdBoundary()
        {
            const int limitSeconds = 60;
            TimeLimitRuleModel rule = CreateRule(1);
            string ruleKey = TimeLimitLogic.GetRuleKey(rule);

            using (TempStorageScope.Begin())
            {
                //  限制-1 秒：tick 后正好 59 秒，低于阈值
                TimeLimitServicer below = CreateServicer(rule, limitSeconds - 2);
                Tick(below);
                Assert.AreEqual(limitSeconds - 1, GetUsage(below, ruleKey), "一次 tick 应把用量累加 1 秒");
                Assert.IsFalse(TriggeredToday(below).Contains(ruleKey), "未达限制不得触发");

                //  恰好等于限制：tick 后正好 60 秒，达到阈值必须触发
                TimeLimitServicer exact = CreateServicer(rule, limitSeconds - 1);
                Tick(exact);
                WaitActingCleared(exact, ruleKey);
                Assert.AreEqual(limitSeconds, GetUsage(exact, ruleKey), "恰好等于限制时用量为限制值");
                Assert.IsTrue(TriggeredToday(exact).Contains(ruleKey), "用量恰好等于限制必须触发");
            }
        }

        /// <summary>
        /// 当日标记去重：超限后只做一次重启补偿触发，标记不重复累加，补偿结束后直接返回。
        /// </summary>
        private static void TestTriggerDedupe()
        {
            TimeLimitRuleModel rule = CreateRule(1);
            string ruleKey = TimeLimitLogic.GetRuleKey(rule);

            using (TempStorageScope.Begin())
            {
                TimeLimitServicer servicer = CreateServicer(rule, 59);

                Tick(servicer);
                WaitActingCleared(servicer, ruleKey);
                Assert.IsTrue(TriggeredToday(servicer).Contains(ruleKey), "首次达限应写入当日已触发标记");

                //  超限 1 秒：命中"标记在但补偿没做过"，补触发一次
                Tick(servicer);
                WaitActingCleared(servicer, ruleKey);
                Assert.AreEqual(1, TriggeredToday(servicer).Count, "当日标记数量不得重复累加");
                Assert.IsTrue(RearmedToday(servicer).Contains(ruleKey), "超限后应走重启补偿分支");

                //  补偿已完成：再 tick 直接返回，不再触发动作，只继续累计用量
                Tick(servicer);
                Assert.IsTrue(TriggeredToday(servicer).Contains(ruleKey), "当日已触发标记应保留");
                Assert.AreEqual(62, GetUsage(servicer, ruleKey), "三次 tick 后用量为 59+3 秒");
            }
        }

        /// <summary>
        /// 延长 5 分钟：扣减 300 秒（不足归零）、清除当日已触发标记，之后再次达限必须能重新触发。
        /// </summary>
        private static void TestExtensionAllowsRetrigger()
        {
            TimeLimitRuleModel rule = CreateRule(1);
            string ruleKey = TimeLimitLogic.GetRuleKey(rule);

            using (TempStorageScope.Begin())
            {
                TimeLimitServicer servicer = CreateServicer(rule, 59);
                Tick(servicer);
                WaitActingCleared(servicer, ruleKey);
                Assert.IsTrue(TriggeredToday(servicer).Contains(ruleKey), "前提：已达限并触发");

                servicer.GrantExtension("chrome", string.Empty, 5);

                Assert.AreEqual(0, GetUsage(servicer, ruleKey), "延长 5 分钟扣减 300 秒，不足则归零不为负");
                Assert.IsFalse(TriggeredToday(servicer).Contains(ruleKey), "延长必须清除当日已触发标记");

                //  标记已清：再次达限必须重新触发
                SetUsage(servicer, ruleKey, 59);
                Tick(servicer);
                Assert.AreEqual(60, GetUsage(servicer, ruleKey), "延长后用量重新累计到限制值");
                Assert.IsTrue(TriggeredToday(servicer).Contains(ruleKey), "延长后再次达限必须重新触发");
            }
        }

        /// <summary>
        /// 跨天恢复：进入新一天时清空当日用量与已触发标记，用量从 0 重新累计。
        /// </summary>
        private static void TestNewDayResetsUsageAndMarkers()
        {
            TimeLimitRuleModel rule = CreateRule(1);
            string ruleKey = TimeLimitLogic.GetRuleKey(rule);

            using (TempStorageScope.Begin())
            {
                TimeLimitServicer servicer = CreateServicer(rule, 59);
                Tick(servicer);
                WaitActingCleared(servicer, ruleKey);
                Assert.IsTrue(TriggeredToday(servicer).Contains(ruleKey), "前提：当天已触发");

                //  把跟踪日期拨回昨天，模拟跨天后的第一次 tick
                SetField(servicer, "_trackDate", DateTime.Today.AddDays(-1));
                Tick(servicer);

                Assert.AreEqual(1, GetUsage(servicer, ruleKey), "跨天后用量清零并从 0 重新累计 1 秒");
                Assert.IsFalse(TriggeredToday(servicer).Contains(ruleKey), "跨天后当日已触发标记应清空");
            }
        }

        /// <summary>
        /// 路径归一化：仅大小写不同的路径视为同一应用；相对路径展开为绝对路径，.. 段被折叠。
        /// </summary>
        private static void TestNormalizePathCaseAndRelative()
        {
            string rulePath = @"C:\Apps\Chrome\chrome.exe";
            string appPath = @"c:\apps\chrome\CHROME.EXE";
            var rules = new List<TimeLimitRuleModel>
            {
                new TimeLimitRuleModel { ProcessName = "chrome", Enabled = true, ExecutablePath = rulePath }
            };
            var app = new AppInfo(IntPtr.Zero, 1, "chrome", "", appPath, AppType.Win32);
            Assert.IsNotNull(TimeLimitLogic.FindRule(rules, app), "仅大小写不同的路径应命中");

            Assert.AreEqual(System.IO.Path.GetFullPath("chrome.exe"),
                TimeLimitLogic.NormalizePath("  chrome.exe  "), "相对路径应去空白后展开为绝对路径");
            Assert.AreEqual(@"C:\Apps\chrome.exe",
                TimeLimitLogic.NormalizePath(@"C:\Apps\..\Apps\chrome.exe"), ".. 段应被折叠");
        }

        private static TimeLimitRuleModel CreateRule(int limitMinutes)
        {
            return new TimeLimitRuleModel
            {
                Name = "Chrome",
                ProcessName = "chrome",
                ExecutablePath = string.Empty,
                LimitMinutes = limitMinutes,
                Action = (int)TimeLimitAction.Warn,
                Enabled = true
            };
        }

        private static TimeLimitServicer CreateServicer(TimeLimitRuleModel rule, int usageSeconds)
        {
            var config = new ConfigModel
            {
                TimeLimit = new TimeLimitSettingsModel
                {
                    IsEnabled = true,
                    Rules = new List<TimeLimitRuleModel> { rule }
                }
            };

            //  四个依赖接口全部用假实现；IData 不在触发判定路径上，传 null 即可
            var servicer = new TimeLimitServicer(
                new FakeAppObserver(),
                new FakeAppConfig(config),
                new FakeNotifier(),
                new FakeSleepdiscover(),
                null);

            //  LoadSettings 是私有方法：只装载规则，不启动计时器（避免真实 tick 与测试驱动竞态）
            Invoke(servicer, "LoadSettings", null);
            SetField(servicer, "_activeApp",
                new AppInfo(IntPtr.Zero, 1, rule.ProcessName, "", rule.ExecutablePath, AppType.Win32));
            SetUsage(servicer, TimeLimitLogic.GetRuleKey(rule), usageSeconds);
            return servicer;
        }

        private static void Tick(TimeLimitServicer servicer)
        {
            Invoke(servicer, "Timer_Elapsed", new object[] { null, null });
        }

        private static object Invoke(object target, string methodName, object[] args)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new Exception("私有方法不存在：" + methodName);
            }

            return method.Invoke(target, args);
        }

        private static T GetField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new Exception("私有字段不存在：" + fieldName);
            }

            return (T)field.GetValue(target);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new Exception("私有字段不存在：" + fieldName);
            }

            field.SetValue(target, value);
        }

        private static int GetUsage(TimeLimitServicer servicer, string ruleKey)
        {
            Dictionary<string, int> usage = GetField<Dictionary<string, int>>(servicer, "_todayUsageSeconds");
            int seconds;
            return usage.TryGetValue(ruleKey, out seconds) ? seconds : -1;
        }

        private static void SetUsage(TimeLimitServicer servicer, string ruleKey, int seconds)
        {
            GetField<Dictionary<string, int>>(servicer, "_todayUsageSeconds")[ruleKey] = seconds;
        }

        /// <summary>当日已触发标记集合。</summary>
        private static HashSet<string> TriggeredToday(TimeLimitServicer servicer)
        {
            return GetField<HashSet<string>>(servicer, "_triggeredToday");
        }

        /// <summary>重启补偿触发标记集合。</summary>
        private static HashSet<string> RearmedToday(TimeLimitServicer servicer)
        {
            return GetField<HashSet<string>>(servicer, "_reArmedToday");
        }

        /// <summary>
        /// 等待后台触发动作结束（动作在 Task.Run 里跑，结束后才从 _actingRules 移除），
        /// 否则下一次 tick 会被"正在处理"守卫提前拦截，断言就不稳定了。
        /// </summary>
        private static void WaitActingCleared(TimeLimitServicer servicer, string ruleKey)
        {
            object syncRoot = GetField<object>(servicer, "_syncRoot");
            HashSet<string> acting = GetField<HashSet<string>>(servicer, "_actingRules");
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);

            while (DateTime.UtcNow < deadline)
            {
                bool stillActing;
                lock (syncRoot)
                {
                    stillActing = acting.Contains(ruleKey);
                }

                if (!stillActing)
                {
                    return;
                }

                Thread.Sleep(10);
            }

            throw new Exception("等待触发动作结束超时：_actingRules 未清空");
        }

        private sealed class FakeAppObserver : IAppObserver
        {
            public void ForceDetectForeground() { }

            public void Start() { }

            public void Stop() { }

            public event AppObserverEventHandler OnAppActiveChanged { add { } remove { } }
        }

        private sealed class FakeSleepdiscover : ISleepdiscover
        {
            public void Start() { }

            public void Stop() { }

            public event SleepdiscoverEventHandler SleepStatusChanged { add { } remove { } }
        }

        private sealed class FakeAppConfig : IAppConfig
        {
            private readonly ConfigModel _config;

            public FakeAppConfig(ConfigModel config)
            {
                _config = config;
            }

            public bool IsLoaded
            {
                get { return true; }
            }

            public void Load() { }

            public ConfigModel GetConfig()
            {
                return _config;
            }

            public void Save() { }

            public void CreateDefaultConfig() { }

            public event AppConfigEventHandler ConfigChanged { add { } remove { } }

            public event Action StoragePathChanged { add { } remove { } }
        }

        private sealed class FakeNotifier : ITimeLimitNotifier
        {
            public void ShowWarning(string title, string message, int notifyStyle = 0) { }

            public Task<TimeLimitDialogResult> ShowCountdownAsync(
                string title,
                string message,
                int seconds,
                string processName,
                string executablePath,
                bool floatingTop = true,
                double opacity = 0.6,
                bool hideByTripleClick = true)
            {
                return Task.FromResult(TimeLimitDialogResult.Cancelled);
            }

            public Task<TimeLimitDialogResult> ShowLockOverlayAsync(
                string title,
                string message,
                string processName,
                string executablePath,
                int passwordMode = 1,
                string customPasswordHash = "",
                string randomPassword = "",
                bool randomPasswordViewed = false,
                int relockMinutes = 10)
            {
                return Task.FromResult(TimeLimitDialogResult.Cancelled);
            }
        }

        private static void Run(string name, Action test) => Program.Run(name, test);
    }
}
