using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;

namespace UI.Servicers
{
    /// <summary>
    /// 时长统计数据实时刷新调度服务：
    /// - 单一 UI 定时器（5 秒），主窗口可见且非最小化时周期性发出刷新请求；
    /// - 页面在 Loaded 时订阅、Unloaded 时退订——只有当前可见页面刷新，未打开页面零开销；
    /// - 窗口隐藏/最小化（托盘后台）时不触发，后台零查询；
    /// - 每 5 秒仅一次轻量范围查询，且各页面共用既有 LoadGate 版本闸门丢弃过期结果，
    ///   单个页面刷新异常不影响其他页面。
    /// </summary>
    public class UsageDataRefreshServicer
    {
        /// <summary>
        /// 刷新间隔。时长以秒级累计，5 秒粒度既保证"实时感"，又把查询频率压到最低。
        /// </summary>
        public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

        private readonly DispatcherTimer _timer;
        private readonly List<Action> _subscribers = new List<Action>();
        private readonly object _sync = new object();

        public UsageDataRefreshServicer()
        {
            _timer = new DispatcherTimer
            {
                Interval = Interval
            };
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        /// <summary>
        /// 订阅刷新通知，返回取消订阅句柄。仅当前可见页面应订阅。
        /// </summary>
        public IDisposable Subscribe(Action onRefresh)
        {
            if (onRefresh == null)
            {
                return new Subscription(this, null);
            }
            lock (_sync)
            {
                _subscribers.Add(onRefresh);
            }
            return new Subscription(this, onRefresh);
        }

        private void Unsubscribe(Action onRefresh)
        {
            if (onRefresh == null)
            {
                return;
            }
            lock (_sync)
            {
                _subscribers.Remove(onRefresh);
            }
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            // 主窗口不可见或最小化时不刷新（应用在托盘/后台时零查询开销）。
            // 注意：Application.MainWindow 可能指向保活窗口（HideWindow），
            // 因此遍历窗口集合定位真正的主窗口（Views.MainWindow）。
            bool mainVisible = false;
            if (Application.Current != null)
            {
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is MainWindow && window.IsVisible && window.WindowState != WindowState.Minimized)
                    {
                        mainVisible = true;
                        break;
                    }
                }
            }
            if (!mainVisible)
            {
                return;
            }

            Action[] snapshot;
            lock (_sync)
            {
                snapshot = _subscribers.ToArray();
            }
            foreach (var action in snapshot)
            {
                try
                {
                    action();
                }
                catch
                {
                    // 单个页面刷新失败不影响其他页面与后续周期
                }
            }
        }

        private sealed class Subscription : IDisposable
        {
            private readonly UsageDataRefreshServicer _owner;
            private readonly Action _action;

            public Subscription(UsageDataRefreshServicer owner, Action action)
            {
                _owner = owner;
                _action = action;
            }

            public void Dispose()
            {
                if (_action != null)
                {
                    _owner.Unsubscribe(_action);
                }
            }
        }
    }
}
