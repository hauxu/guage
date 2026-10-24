using Core.Servicers.Instances;
using Core.Servicers.Interfaces;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Guage.Tests
{
    /// <summary>
    /// 休息提醒（番茄钟）核心流转测试：通过反射拨快时钟并触发内部 tick，避免真实等待。
    /// </summary>
    public static class BreakReminderTests
    {
        public static void RunAll()
        {
            Run("休息提醒-双勾选开始进入工作阶段", TestStartWork);
            Run("休息提醒-工作完成自动进入休息", TestWorkToBreak);
            Run("休息提醒-休息完成回到工作", TestBreakToWork);
            Run("休息提醒-仅工作模式完成后回空闲", TestWorkOnlyIdle);
            Run("休息提醒-都未勾选抛异常", TestStartThrows);
            Run("休息提醒-暂停冻结/继续恢复", TestPauseResume);
            Run("休息提醒-停止回空闲", TestStop);
        }

        private static BreakReminderServicer Create()
        {
            return new BreakReminderServicer(null);
        }

        private static T GetField<T>(object target, string name)
        {
            var f = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f == null)
            {
                throw new Exception("私有字段不存在：" + name);
            }
            return (T)f.GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            var f = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f == null)
            {
                throw new Exception("私有字段不存在：" + name);
            }
            f.SetValue(target, value);
        }

        /// <summary>
        /// 停掉真实计时器（避免真实 tick 与测试驱动的 tick 竞态），把目标时间拨到过去，触发一次内部 tick。
        /// </summary>
        private static void AdvanceAndTick(BreakReminderServicer servicer)
        {
            var timer = GetField<System.Timers.Timer>(servicer, "_timer");
            timer.Stop();

            SetField(servicer, "_phaseEndTime", DateTime.Now.AddSeconds(-1));
            var method = servicer.GetType().GetMethod("Timer_Elapsed", BindingFlags.Instance | BindingFlags.NonPublic);
            method.Invoke(servicer, new object[] { null, null });
        }

        private static void TestStartWork()
        {
            var s = Create();
            s.Start(25, 5, true, true);
            Assert.AreEqual(BreakReminderPhase.Work, s.CurrentPhase, "双勾选应从工作阶段开始");
            Assert.IsTrue(s.IsRunning);
            Assert.AreEqual(TimeSpan.FromMinutes(25), s.PhaseDuration);
            s.Stop();
        }

        private static void TestWorkToBreak()
        {
            var s = Create();
            var phases = new List<BreakReminderPhase>();
            s.PhaseChanged += phases.Add;
            s.Start(25, 5, true, true);
            AdvanceAndTick(s);
            Assert.AreEqual(BreakReminderPhase.Break, s.CurrentPhase, "工作完成应进入休息");
            Assert.IsTrue(s.IsRunning);
            s.Stop();
        }

        private static void TestBreakToWork()
        {
            var s = Create();
            s.Start(25, 5, true, true);
            AdvanceAndTick(s); // Work -> Break
            AdvanceAndTick(s); // Break -> Work
            Assert.AreEqual(BreakReminderPhase.Work, s.CurrentPhase, "休息完成应回到工作");
            s.Stop();
        }

        private static void TestWorkOnlyIdle()
        {
            var s = Create();
            BreakReminderPhase? completed = null;
            s.PhaseCompleted += (phase, sleepElapsed) => completed = phase;
            s.Start(25, 5, true, false);
            AdvanceAndTick(s);
            Assert.AreEqual(BreakReminderPhase.Idle, s.CurrentPhase, "仅工作模式完成后应回空闲");
            Assert.IsFalse(s.IsRunning);
            Assert.AreEqual(BreakReminderPhase.Work, completed);
            s.Stop();
        }

        private static void TestStartThrows()
        {
            var s = Create();
            Assert.Throws<InvalidOperationException>(() => s.Start(25, 5, false, false), "都未勾选应抛异常");
        }

        private static void TestPauseResume()
        {
            var s = Create();
            s.Start(25, 5, true, true);
            s.Pause();
            Assert.IsTrue(s.IsPaused, "暂停后 IsPaused 应为 true");

            var frozen = s.Remaining;
            SetField(s, "_phaseEndTime", DateTime.Now.AddSeconds(-1));
            Assert.AreEqual(frozen, s.Remaining, "暂停期间剩余时间应冻结");

            s.Resume();
            Assert.IsFalse(s.IsPaused, "继续后 IsPaused 应为 false");
            Assert.IsTrue(s.IsRunning);
            s.Stop();
        }

        private static void TestStop()
        {
            var s = Create();
            s.Start(25, 5, true, true);
            s.Stop();
            Assert.AreEqual(BreakReminderPhase.Idle, s.CurrentPhase);
            Assert.IsFalse(s.IsRunning);
        }

        private static void Run(string name, Action test) => Program.Run(name, test);
    }
}
