using Core.Librarys;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Guage.Tests
{
    public static class DurationBucketingTests
    {
        public static void RunAll()
        {
            Run("分桶-跨小时会话（10:30:15 起 3600 秒）", TestCrossHour);
            Run("分桶-整点两小时（10:00:00 起 7200 秒）", TestFullHours);
            Run("分桶-跨天（23:30:00 起 3600 秒）", TestCrossDay);
            Run("分桶-59:59 边界（23:59:59 起 2 秒）", TestSecondBoundary);
            Run("分桶-小时内不拆分（10:00:00 起 600 秒）", TestWithinHour);
        }

        private static void TestCrossHour()
        {
            // 10:30:15 起 3600 秒：10 点桶 1785 秒，11 点桶 1815 秒
            var start = new DateTime(2026, 8, 14, 10, 30, 15);
            DurationBucketing.SplitHours(start, 3600, out Dictionary<DateTime, int> hours, out DateTime endTime);

            Assert.AreEqual(2, hours.Count);
            Assert.AreEqual(1785, hours[new DateTime(2026, 8, 14, 10, 0, 0)]);
            Assert.AreEqual(1815, hours[new DateTime(2026, 8, 14, 11, 0, 0)]);
            Assert.AreEqual(new DateTime(2026, 8, 14, 11, 0, 0), endTime);
        }

        private static void TestFullHours()
        {
            var start = new DateTime(2026, 8, 14, 10, 0, 0);
            DurationBucketing.SplitHours(start, 7200, out Dictionary<DateTime, int> hours, out DateTime endTime);

            Assert.AreEqual(2, hours.Count);
            Assert.AreEqual(3600, hours[new DateTime(2026, 8, 14, 10, 0, 0)]);
            Assert.AreEqual(3600, hours[new DateTime(2026, 8, 14, 11, 0, 0)]);
            Assert.AreEqual(new DateTime(2026, 8, 14, 11, 0, 0), endTime);
        }

        private static void TestCrossDay()
        {
            // 23:30:00 起 3600 秒：23 点桶 1800、次日 0 点桶 1800；天桶：当天 1800、次日 1800
            var start = new DateTime(2026, 8, 14, 23, 30, 0);
            DurationBucketing.SplitHours(start, 3600, out Dictionary<DateTime, int> hours, out DateTime hourEnd);
            DurationBucketing.SplitDays(start, 3600, out Dictionary<DateTime, int> days, out DateTime dayEnd);

            Assert.AreEqual(1800, hours[new DateTime(2026, 8, 14, 23, 0, 0)]);
            Assert.AreEqual(1800, hours[new DateTime(2026, 8, 15, 0, 0, 0)]);
            Assert.AreEqual(1800, days[new DateTime(2026, 8, 14)]);
            Assert.AreEqual(1800, days[new DateTime(2026, 8, 15)]);
            Assert.AreEqual(new DateTime(2026, 8, 15), dayEnd);
        }

        private static void TestSecondBoundary()
        {
            var start = new DateTime(2026, 8, 14, 23, 59, 59);
            DurationBucketing.SplitHours(start, 2, out Dictionary<DateTime, int> hours, out DateTime endTime);

            Assert.AreEqual(1, hours[new DateTime(2026, 8, 14, 23, 0, 0)]);
            Assert.AreEqual(1, hours[new DateTime(2026, 8, 15, 0, 0, 0)]);
        }

        private static void TestWithinHour()
        {
            var start = new DateTime(2026, 8, 14, 10, 0, 0);
            DurationBucketing.SplitHours(start, 600, out Dictionary<DateTime, int> hours, out DateTime endTime);

            Assert.AreEqual(1, hours.Count);
            Assert.AreEqual(600, hours[new DateTime(2026, 8, 14, 10, 0, 0)]);
            Assert.AreEqual(new DateTime(2026, 8, 14, 10, 0, 0), endTime);
        }

        private static void Run(string name, Action test) => Program.Run(name, test);
    }
}
