using Core.Event;
using Core.Librarys;
using Core.Models.AppObserver;
using Core.Servicers.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace Core.Servicers.Instances
{
    public class AppObserver : IAppObserver
    {
        public event AppObserverEventHandler OnAppActiveChanged;

        #region win32 api
        public delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType,
         IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);
        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr
         hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess,
         uint idThread, uint dwFlags);
        [DllImport("user32.dll")]
        public static extern bool UnhookWinEvent(IntPtr hWinEventHook);
        #endregion

        //  前台窗口检测
        private WinEventDelegate _foregroundEventDelegate;
        private readonly IAppManager _appManager;
        private readonly IWindowManager _windowManager;
        private IntPtr _hook;
        private bool _isStart = false;
        private System.Timers.Timer _delayDetectTimer;
        private IntPtr _lastForegroundHwnd = IntPtr.Zero;
        public AppObserver(IAppManager appManager_, IWindowManager windowManager)
        {
            _appManager = appManager_;
            _windowManager = windowManager;
            _foregroundEventDelegate = new WinEventDelegate(ForegroundEventCallback);
            //  1s 兜底轮询：前台事件钩子在全屏/特殊窗口下可能丢失事件，轮询补检保证统计不卡住
            _delayDetectTimer = new System.Timers.Timer();
            _delayDetectTimer.Interval = 1000;
            _delayDetectTimer.Elapsed += DelayDetectTimer_Elapsed;
        }



        private void ForegroundEventCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            _delayDetectTimer?.Stop();
            _lastForegroundHwnd = hwnd;
            DateTime activeTime = DateTime.Now;
            var args = GetAppInfoEventArgs(hwnd, activeTime);
            //  前台应用变化
            OnAppActiveChanged?.Invoke(this, args);
            //  总是启动 1s 兜底轮询：钩子事件可能丢失（全屏应用/特殊窗口/锁屏），轮询补检
            _delayDetectTimer?.Start();

        }

        private void DelayDetectTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            _delayDetectTimer?.Stop();
            DelayDetect();
            _delayDetectTimer?.Start();
        }

        /// <summary>
        /// 立刻重新探测当前前台应用：清掉上次记录的句柄再走一次 DelayDetect。
        /// </summary>
        public void ForceDetectForeground()
        {
            _lastForegroundHwnd = IntPtr.Zero;
            DelayDetect();
        }

        private void DelayDetect()
        {
            try
            {
                IntPtr w = Win32API.GetForegroundWindow();
                if (w == IntPtr.Zero || w == _lastForegroundHwnd)
                {
                    return;
                }
                _lastForegroundHwnd = w;
                DateTime activeTime = DateTime.Now;
                var args = GetAppInfoEventArgs(w, activeTime);
                if (args.App.Type != Enums.AppType.SystemComponent)
                {
                    //  前台变化补检
                    OnAppActiveChanged?.Invoke(this, args);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex.ToString());
            }
        }

        private AppActiveChangedEventArgs GetAppInfoEventArgs(IntPtr handle_, DateTime activeTime_)
        {
            var app = _appManager.GetAppInfo(handle_);
            var window = _windowManager.GetWindowInfo(handle_);
            return new AppActiveChangedEventArgs(app, window, activeTime_);
        }

        private void HandleForegroundWindow()
        {
            IntPtr w = Win32API.GetForegroundWindow();
            ForegroundEventCallback(IntPtr.Zero, 0, w, 0, 0, 0, 0);
        }

        public void Start()
        {
            if (_isStart)
            {
                return;
            }
            _isStart = true;
            _hook = SetWinEventHook(0x0003, 0x0003, IntPtr.Zero, _foregroundEventDelegate, 0, 0, 0);
            HandleForegroundWindow();
        
            //  启动时先确定一次当前前台应用：否则要等用户切窗口才有第一个事件，
            //  期间时长限制拿不到活跃应用、统计也会空一段
            try
            {
                _lastForegroundHwnd = IntPtr.Zero;
                DelayDetect();
            }
            catch (Exception ex)
            {
                Logger.Error("启动时探测前台应用失败：" + ex.Message);
            }
}

        public void Stop()
        {
            _isStart = false;
            _delayDetectTimer?.Stop();
            UnhookWinEvent(_hook);
        }
    }
}
