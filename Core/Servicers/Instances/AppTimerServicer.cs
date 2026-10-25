using Core.Enums;
using Core.Event;
using Core.Models.AppObserver;
using Core.Servicers.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace Core.Servicers.Instances
{
    public class AppTimerServicer : IAppTimerServicer
    {
        public event AppTimerEventHandler OnAppDurationUpdated;

        private readonly IAppObserver _appObserver;

        private bool _isStart = false;
        private int _appDuration = 0;
        private DateTime _startTime = DateTime.MinValue;
        private DateTime _endTime = DateTime.MinValue;
        private string _activeProcess;

        private Dictionary<string, AppData> _appData;
        private System.Timers.Timer _timer;
        private AppDurationUpdatedEventArgs _lastInvokeEventArgs;

        //  会话状态锁：WinEvent 回调线程与兜底轮询线程可能并发进入
        private readonly object _sessionLock = new object();

        public struct AppData
        {
            public AppInfo App { get; set; }
            public WindowInfo Window { get; set; }

        }
        public AppTimerServicer(IAppObserver appObserver_)
        {
            _appObserver = appObserver_;

        }

        private void Init()
        {
            _appData = new Dictionary<string, AppData>();
            _appDuration = 0;
            _activeProcess = string.Empty;

            //  每次睡眠/唤醒都会再走一遍 Init；不释放旧定时器就会在 TimerQueue 里
            //  累积已停止但仍被引用的实例（一天睡十次就多十个）
            if (_timer != null)
            {
                _timer.Elapsed -= Timer_Elapsed;
                _timer.Dispose();
            }

            _timer = new System.Timers.Timer();
            _timer.Interval = 1000;
            _timer.Elapsed += Timer_Elapsed;
        }



        public void Start()
        {
            if (_isStart) { return; }

            Init();

            _isStart = true;
            _appObserver.OnAppActiveChanged += AppObserver_OnAppActiveChanged;
        }


        public void Stop()
        {
            _isStart = false;
            StopTimer();
            _appObserver.OnAppActiveChanged -= AppObserver_OnAppActiveChanged;
        }

        private void AppObserver_OnAppActiveChanged(object sender, AppActiveChangedEventArgs e)
        {
            if (e?.App == null)
            {
                return;
            }

            lock (_sessionLock)
            {
                string processName = e.App.Process;

                // guage self foreground: keep previous session so stats keep counting on overview return
                if (string.Equals(processName, "guage", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                bool isStatistical = IsStatistical(e.App);

                Debug.WriteLine(processName + " -> " + _activeProcess);
                if (processName != _activeProcess)
                {
                    StopTimer();
                    InvokeEvent();

                    if (isStatistical)
                    {
                        StartTimer();
                    }

                    _activeProcess = e.App.Type == AppType.SystemComponent ? string.Empty : processName;
                }

                if (isStatistical)
                {
                    var data = new AppData()
                    {
                        App = e.App,
                        Window = e.Window,
                    };
                    if (!_appData.ContainsKey(processName))
                    {
                        //  缓存应用信息
                        _appData.Add(processName, data);
                    }
                    else
                    {
                        //  更新缓存
                        _appData[processName] = data;
                    }
                }
            }
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            // time-delta accumulation: missed/duplicated ticks no longer lose/gain time
            if (_startTime != DateTime.MinValue)
            {
                //  用墙钟差算时长，但要挡住两种系统时间异常：前跳会把几秒记成上万秒，
                //  回拨会得到负值并让这段时间一秒都不记
                double delta = (DateTime.Now - _startTime).TotalSeconds;
                if (delta < 0)
                {
                    _startTime = DateTime.Now;
                    _appDuration = 0;
                }
                else
                {
                    _appDuration = (int)Math.Min(86400, Math.Floor(delta));
                }
            }
        }

        private void StartTimer()
        {
            _timer.Start();
            _startTime = DateTime.Now;
            _appDuration = 0;
        }

        private void StopTimer()
        {
            _timer.Stop();
            _endTime = DateTime.Now;
        }

        private void InvokeEvent()
        {
            var info = GetAppDuration();
            if (info != null)
            {
                //  直接用 DateTime 值比较去重，避免 ToString 依赖文化格式与秒级精度
                if (_lastInvokeEventArgs == null
                    || _lastInvokeEventArgs.ActiveTime != _startTime
                    || _lastInvokeEventArgs.EndTime != _endTime)
                {
                    Debug.WriteLine("【计时更新】" + info);
                    OnAppDurationUpdated?.Invoke(this, info);
                }
                else
                {
                    Debug.WriteLine("【重复！！】" + _lastInvokeEventArgs + "，【now】" + info);
                }
                _lastInvokeEventArgs = info;
            }
        }

        /// <summary>
        /// 判断应用是否需要被统计
        /// </summary>
        /// <param name="app_"></param>
        /// <returns>需要统计时返回 true </returns>
        private bool IsStatistical(AppInfo app_)
        {
            return !string.IsNullOrEmpty(app_.Process) && app_.Type != AppType.SystemComponent && !string.IsNullOrEmpty(app_.ExecutablePath);
        }

        public AppDurationUpdatedEventArgs GetAppDuration()
        {
            if (_appDuration > 0 && !string.IsNullOrEmpty(_activeProcess) && _appData.ContainsKey(_activeProcess))
            {
                var data = _appData[_activeProcess];
                var args = new AppDurationUpdatedEventArgs(_appDuration, data.App, data.Window, _startTime, _endTime);
                return args;
            }
            return null;
        }

        /// <summary>
        /// 结算当前会话（退出前调用）：停表并强制提交一次当前时长，绕过重复去重
        /// </summary>
        public AppDurationUpdatedEventArgs Flush()
        {
            if (!_isStart)
            {
                return null;
            }

            StopTimer();
            if (_startTime != DateTime.MinValue)
            {
                double flushDelta = (_endTime - _startTime).TotalSeconds;
                _appDuration = flushDelta <= 0 ? 0 : (int)Math.Min(86400, Math.Floor(flushDelta));
            }

            var info = GetAppDuration();
            if (info != null)
            {
                OnAppDurationUpdated?.Invoke(this, info);
                _lastInvokeEventArgs = info;
                _appDuration = 0;
            }
            return info;
        }
    }
}
