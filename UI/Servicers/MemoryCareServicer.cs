using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace UI.Servicers
{
    /// <summary>
    /// 内存养护服务：空闲时压缩 GC 堆并修剪工作集，将进程内存稳定控制在低水位。
    /// 仅在用户空闲超过阈值时执行，避免影响操作中的性能。
    /// </summary>
    public static class MemoryCareServicer
    {
        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        private static DispatcherTimer _timer;
        private static volatile int _lastActivityTick;

        public static void Start()
        {
            if (_timer != null)
            {
                return;
            }

            _lastActivityTick = Environment.TickCount;

            // 用户输入活动刷新空闲时间戳（轻量，仅一次 TickCount 读取）
            System.Windows.Input.InputManager.Current.PostProcessInput += (s, e) =>
            {
                _lastActivityTick = Environment.TickCount;
            };

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(60)
            };
            _timer.Tick += (s, e) =>
            {
                // 空闲超过 120 秒才执行养护，避免频繁 GC 影响交互性能
                int idleMs = Environment.TickCount - _lastActivityTick;
                if (idleMs < 120 * 1000)
                {
                    return;
                }

                try
                {
                    GC.Collect(2, GCCollectionMode.Optimized, true, compacting: true);
                    GC.WaitForPendingFinalizers();
                    using (var p = System.Diagnostics.Process.GetCurrentProcess())
                    {
                        EmptyWorkingSet(p.Handle);
                    }
                }
                catch
                {
                    // 养护失败不影响主流程
                }
            };
            _timer.Start();
        }
    }
}
