using Core.Librarys;
using Core.Servicers.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using UI.Controls.Window;
using UI.ViewModels;

namespace UI.Servicers
{
    /// <summary>
    /// 休息提醒通知服务：监听计时服务的阶段完成事件，弹出提醒窗口 + 托盘系统通知
    /// 应用生命周期单例，在 App 启动时激活
    /// </summary>
    public class BreakReminderNotifierServicer
    {
        private readonly IBreakReminderServicer _servicer;
        private readonly MainViewModel _mainVM;
        private readonly IStatusBarIconServicer _statusBarIconServicer;
        private BreakReminderWindow _window;

        public BreakReminderNotifierServicer(
            IBreakReminderServicer servicer,
            MainViewModel mainVM,
            IStatusBarIconServicer statusBarIconServicer)
        {
            _servicer = servicer;
            _mainVM = mainVM;
            _statusBarIconServicer = statusBarIconServicer;

            _servicer.PhaseCompleted += Servicer_PhaseCompleted;
        }

        private void Servicer_PhaseCompleted(BreakReminderPhase phase, bool sleepElapsed)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                ShowReminder(phase, sleepElapsed);
            }
            else
            {
                dispatcher.BeginInvoke(new Action(() => ShowReminder(phase, sleepElapsed)));
            }
        }

        private void ShowReminder(BreakReminderPhase phase, bool sleepElapsed)
        {
            string title;
            string message;
            string balloon;

            if (phase == BreakReminderPhase.Work)
            {
                title = "该休息了";
                message = sleepElapsed
                    ? "工作阶段已在休眠期间结束，起来活动一下吧。"
                    : "工作阶段结束，起来活动一下、看看远处吧。";
                balloon = "该休息了：工作阶段结束";
            }
            else
            {
                title = "休息结束";
                message = sleepElapsed
                    ? "休息阶段已在休眠期间结束。"
                    : "休息阶段结束，继续工作吧。";
                balloon = "休息结束：继续工作吧";
            }

            // 托盘系统通知（应用在后台/最小化时也能收到）
            try
            {
                _statusBarIconServicer.ShowBalloonTip("休息提醒", balloon);
            }
            catch (Exception ex)
            {
                Logger.Error(ex.ToString());
            }

            // 非模态置顶提醒窗口（不阻断操作）
            try
            {
                if (_window == null || !_window.IsLoaded)
                {
                    _window = new BreakReminderWindow();
                    _window.Closed += (s, e) => _window = null;
                }
                _window.UpdateContent(title, message);

                Window owner = Application.Current?.MainWindow;
                //  主窗口收进托盘时 IsLoaded 仍为 true：Owner 指向不可见窗口会让
                //  CenterOwner 按隐藏窗口定位、Z 序联动异常，必须用 IsVisible 判断
                if (owner != null && owner.IsLoaded && owner.IsVisible)
                {
                    _window.Owner = owner;
                    _window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }
                else
                {
                    _window.Owner = null;
                    _window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }

                if (!_window.IsLoaded)
                {
                    _window.Show();
                }
                if (_window.WindowState == WindowState.Minimized)
                {
                    _window.WindowState = WindowState.Normal;
                }
                _window.Activate();
            }
            catch (Exception ex)
            {
                Logger.Error(ex.ToString());
            }
        }
    }
}
