using Core.Librarys;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Guage.Tests
{
    /// <summary>
    /// 套圈水机 分钟↔圈 贪心分配的回归测试：
    /// 重点守住"满柱 round-trip 恒等"（420→6+6→420、210→3+3→210），
    /// 旧取模算法会在这里静默缩减用户设置。
    /// </summary>
    public static class RingAllocationTests
    {
        private const int LeftUnit = 60;
        private const int RightUnit = 10;
        private const int MaxPerColumn = 6;

        public static void RunAll()
        {
            Run("分配-满柱 round-trip 恒等（工作 420）", TestFullCapacityWork);
            Run("分配-满柱 round-trip 恒等（休息 210）", TestFullCapacityBreak);
            Run("分配-常规值恒等", TestCommonValues);
            Run("分配-低 granularity 余数向下取整", TestRemainderFloored);
            Run("分配-超容量夹取", TestOverCapacityClamped);
            Run("分配-零与负值", TestZeroAndNegative);
            Run("路径清洗-双反斜杠折叠", TestSanitizePath);
            Run("路径清洗-UNC 前缀保留", TestSanitizeUncPath);
        }

        private static void Run(string name, Action test) => Program.Run(name, test);

        private static int ToMinutes(int left, int right)
        {
            return left * LeftUnit + right * RightUnit;
        }

        private static void TestFullCapacityWork()
        {
            var allocation = RingAllocation.Allocate(420, LeftUnit, RightUnit, MaxPerColumn);
            Assert.AreEqual(6, allocation.Item1, "左柱应铺满 6 圈");
            Assert.AreEqual(6, allocation.Item2, "右柱应铺满 6 圈");
            Assert.AreEqual(420, ToMinutes(allocation.Item1, allocation.Item2), "420 分钟 round-trip 必须恒等");
        }

        private static void TestFullCapacityBreak()
        {
            var allocation = RingAllocation.Allocate(210, 30, 5, MaxPerColumn);
            Assert.AreEqual(6, allocation.Item1, "左柱应铺满 6 圈");
            Assert.AreEqual(6, allocation.Item2, "右柱应铺满 6 圈");
            Assert.AreEqual(210, allocation.Item1 * 30 + allocation.Item2 * 5, "210 分钟 round-trip 必须恒等");
        }

        private static void TestCommonValues()
        {
            //  可被圈数精确表达的值必须 round-trip 恒等
            foreach (int total in new[] { 10, 50, 60, 70, 120, 130, 200, 240, 360, 410 })
            {
                var allocation = RingAllocation.Allocate(total, LeftUnit, RightUnit, MaxPerColumn);
                int roundTrip = ToMinutes(allocation.Item1, allocation.Item2);
                Assert.AreEqual(total, roundTrip, total + " 分钟重开不应缩水");
                Assert.IsTrue(allocation.Item1 <= MaxPerColumn, "左柱不得超过容量");
                Assert.IsTrue(allocation.Item2 <= MaxPerColumn, "右柱不得超过容量");
            }
        }

        private static void TestRemainderFloored()
        {
            //  59 分钟无法被 10 分钟圈表达：应向下取整到 50（0 左 + 5 右），且不得超出
            var allocation = RingAllocation.Allocate(59, LeftUnit, RightUnit, MaxPerColumn);
            int roundTrip = ToMinutes(allocation.Item1, allocation.Item2);
            Assert.IsTrue(roundTrip <= 59, "不可表达的余量只能向下取整");
            Assert.AreEqual(50, roundTrip, "59 分钟应分配为 0+5（50 分钟）");
        }

        private static void TestOverCapacityClamped()
        {
            //  超出两柱容量（>420）时按容量夹取，不得越界
            var allocation = RingAllocation.Allocate(779, LeftUnit, RightUnit, MaxPerColumn);
            Assert.AreEqual(6, allocation.Item1);
            Assert.AreEqual(6, allocation.Item2);
            Assert.AreEqual(420, ToMinutes(allocation.Item1, allocation.Item2));
        }

        private static void TestZeroAndNegative()
        {
            var zero = RingAllocation.Allocate(0, LeftUnit, RightUnit, MaxPerColumn);
            Assert.AreEqual(0, zero.Item1 + zero.Item2, "0 分钟应无圈");

            var negative = RingAllocation.Allocate(-30, LeftUnit, RightUnit, MaxPerColumn);
            Assert.AreEqual(0, negative.Item1 + negative.Item2, "负值按 0 处理");
        }

        private static void TestSanitizePath()
        {
            Assert.AreEqual(@"D:\QQMusic\QQMusic.exe",
                TimeLimitLogic.SanitizeExecutablePath(@"D:\\QQMusic\\QQMusic.exe"),
                "本地路径双反斜杠应折叠");
            Assert.AreEqual(@"C:\Program Files\x\y.exe",
                TimeLimitLogic.SanitizeExecutablePath(@"C:\\Program Files\\x\\y.exe"),
                "含空格路径同样折叠");
            Assert.AreEqual(@"C:\ok\path.exe",
                TimeLimitLogic.SanitizeExecutablePath(@"C:\ok\path.exe"),
                "正常路径原样返回");
            Assert.AreEqual(string.Empty,
                TimeLimitLogic.SanitizeExecutablePath(string.Empty),
                "空路径不抛异常");
            Assert.IsNull(TimeLimitLogic.SanitizeExecutablePath(null), "null 不抛异常");
        }

        private static void TestSanitizeUncPath()
        {
            //  UNC 开头的双反斜杠是协议必需：保留开头两道，其余折叠
            Assert.AreEqual(@"\\server\share\x.exe",
                TimeLimitLogic.SanitizeExecutablePath(@"\\\\server\\share\\x.exe"),
                "UNC 开头双反斜杠保留，其余折叠");
            Assert.AreEqual(@"\\server\share",
                TimeLimitLogic.SanitizeExecutablePath(@"\\\\server\\share"),
                "UNC 正常路径原样返回");
        }
    }
}
