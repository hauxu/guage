using Core.Enums;
using Core.Servicers.Interfaces;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using UI.Controls.Window;
using UI.ViewModels;

namespace UI.Servicers
{
    /// <summary>
    /// 时长限制 UI 通知：仅提示（Toast / Windows 消息弹窗）、倒计时悬浮窗、锁定遮罩
    /// </summary>
    public class TimeLimitNotifierServicer : ITimeLimitNotifier
    {
        private readonly MainViewModel _mainViewModel;
        private CountdownWindow _countdownWindow;
        private LockOverlayWindow _lockOverlayWindow;
        private FloatingTipWindow _tipWindow;

        public TimeLimitNotifierServicer(MainViewModel mainViewModel_)
        {
            _mainViewModel = mainViewModel_;
        }

        /// <summary>
        /// 仅提示：notifyStyle=0 在被限制应用前面弹出置顶提示框（数秒自动消失，不抢焦点）；
        /// notifyStyle=1 调用 Windows 原生消息弹窗。
        /// </summary>
        public void ShowWarning(string title, string message, int notifyStyle = 0)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (notifyStyle == 1)
                {
                    MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    // 关闭已存在的提示窗，避免重复触发堆叠
                    _tipWindow?.Close();
                }
                catch
                {
                    // 忽略
                }

                var tip = new FloatingTipWindow(title, message);
                _tipWindow = tip;
                tip.Closed += (s, e) =>
                {
                    if (ReferenceEquals(_tipWindow, tip))
                    {
                        _tipWindow = null;
                    }
                };
                tip.Show();
            }));
        }

        /// <summary>
        /// 倒计时关闭：悬浮窗（默认右上角置顶半透明，连击 3 次隐藏）；归零返回 Kill
        /// </summary>
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
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                //  应用正在退出：立即返回，避免调用方 await 永久挂起卡死限制流程
                return Task.FromResult(TimeLimitDialogResult.Cancelled);
            }

            var tcs = new TaskCompletionSource<TimeLimitDialogResult>();
            var operation = dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    //  关闭已存在的倒计时窗口，避免重复触发时多个置顶窗口堆叠
                    try
                    {
                        _countdownWindow?.Close();
                    }
                    catch
                    {
                        //  忽略
                    }

                    var window = new CountdownWindow(title, message, seconds, processName, executablePath,
                        floatingTop, opacity, hideByTripleClick);
                    _countdownWindow = window;
                    window.Closed += (s, e) =>
                    {
                        if (ReferenceEquals(_countdownWindow, window))
                        {
                            _countdownWindow = null;
                        }

                        tcs.TrySetResult(window.Result);
                    };
                    window.Show();
                }
                catch (Exception ex)
                {
                    Core.Librarys.Logger.Error("倒计时窗口创建失败：" + ex);
                    tcs.TrySetResult(TimeLimitDialogResult.Cancelled);
                }
            }));
            HookOperationFailure(operation, tcs);
            return tcs.Task;
        }

        /// <summary>
        /// 锁定遮罩：需输入正确密码才可解锁
        /// </summary>
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
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                return Task.FromResult(TimeLimitDialogResult.Cancelled);
            }

            var tcs = new TaskCompletionSource<TimeLimitDialogResult>();
            var operation = dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    //  关闭已存在的锁定窗口，避免重复触发时多个置顶窗口堆叠
                    try
                    {
                        _lockOverlayWindow?.ForceClose();
                    }
                    catch
                    {
                        //  忽略
                    }

                    var window = new LockOverlayWindow(title, message, processName, executablePath,
                        passwordMode, customPasswordHash, randomPassword, randomPasswordViewed, relockMinutes);
                    _lockOverlayWindow = window;
                    window.Closed += (s, e) =>
                    {
                        if (ReferenceEquals(_lockOverlayWindow, window))
                        {
                            _lockOverlayWindow = null;
                        }

                        tcs.TrySetResult(window.Result);
                    };
                    window.Show();
                }
                catch (Exception ex)
                {
                    Core.Librarys.Logger.Error("锁定遮罩窗口创建失败：" + ex);
                    tcs.TrySetResult(TimeLimitDialogResult.Cancelled);
                }
            }));
            HookOperationFailure(operation, tcs);
            return tcs.Task;
        }

        /// <summary>
        /// 调度被中止（应用正在关闭）时完成 tcs，防止 await 永久挂起；
        /// 委托内部异常已由 try/catch 就地完成
        /// </summary>
        private static void HookOperationFailure(
            DispatcherOperation operation,
            TaskCompletionSource<TimeLimitDialogResult> tcs)
        {
            operation.Aborted += (s, e) => tcs.TrySetResult(TimeLimitDialogResult.Cancelled);
        }
    }
}
