using Core.Enums;
using Core.Librarys;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace UI.Controls.Window
{
    /// <summary>
    /// 锁定遮罩：仅覆盖受限应用的窗口（支持多显示器、多窗口）。
    /// 必须输入正确密码才可解锁；锁定期间应用进程被强制结束后立即禁止其再次启动。
    /// 不允许通过 Alt+F4 关闭。
    /// </summary>
    public partial class LockOverlayWindow : System.Windows.Window
    {
        //  默认 Cancelled：应用退出/注销等非正常关闭不得被当作"密码验证成功"
        //  （否则会触发密码作废+重锁逻辑）；只有验证成功路径显式设 Confirmed
        public TimeLimitDialogResult Result { get; private set; } = TimeLimitDialogResult.Cancelled;

        private readonly int _passwordMode;              // 0=自定义 1=随机
        private readonly string _customPasswordHash;     // 自定义密码 SHA256
        private readonly string _randomPassword;         // 随机密码明文
        private readonly bool _randomPasswordViewed;     // 随机密码是否已查看
        private readonly string _processName;

        private readonly List<int> _processIds = new List<int>();
        private readonly Dictionary<IntPtr, MaskState> _masks = new Dictionary<IntPtr, MaskState>();
        private readonly DispatcherTimer _syncTimer;
        private readonly DispatcherTimer _noWindowTimer;
        private int _syncTick;
        private bool _allowClose;

        /// <summary>
        /// 遮罩窗口及其关闭许可。
        /// </summary>
        private sealed class MaskState
        {
            public System.Windows.Window Window;
            public bool AllowClose;
        }

        public LockOverlayWindow(
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
            InitializeComponent();
            Title = title;
            TitleText.Text = title;
            MessageText.Text = message;
            _passwordMode = passwordMode;
            _customPasswordHash = customPasswordHash;
            _randomPassword = randomPassword;
            _randomPasswordViewed = randomPasswordViewed;
            _processName = processName;

            if (passwordMode == 0 || string.IsNullOrEmpty(randomPassword))
            {
                // 自定义密码：不显示查看按钮
                ShowPwdButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                ShowPwdButton.Visibility = _randomPasswordViewed ? Visibility.Collapsed : Visibility.Visible;
            }

            PasswordInput.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    ConfirmButton_Click(s, e);
                }
            };

            //  拦截 Alt+F4：不允许通过快捷键关闭锁定遮罩
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.System && e.SystemKey == System.Windows.Input.Key.F4)
                {
                    e.Handled = true;
                }
            };

            Closing += (s, e) =>
            {
                if (!_allowClose)
                {
                    e.Cancel = true;
                }
            };

            _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _syncTimer.Tick += (s, e) => SyncMasks();

            //  目标应用最小化到托盘（进程存活、无可见窗口）时，密码面板无处依附且永不关闭，
            //  会把该规则的遮罩永久挂起——持续 60 秒无窗口则按取消关闭，
            //  服务端保持规则锁定，应用再次到前台时重新遮罩
            _noWindowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _noWindowTimer.Tick += (s, e) =>
            {
                _noWindowTimer.Stop();
                RequestClose(TimeLimitDialogResult.Cancelled);
            };

            Loaded += (s, e) =>
            {
                //  字体比例与显示缩放：按当前全局字号设置缩放本窗口
                UI.Base.UIFontScale.ApplyToWindow(this);

                PasswordInput.Focus();
                RefreshProcessIds();
                SyncMasks();
                _syncTimer.Start();
            };
        }

        /// <summary>
        /// 仅允许通过内部 RequestClose 关闭（输入密码/延长/进程退出）。
        /// </summary>
        private void RequestClose(TimeLimitDialogResult result)
        {
            _allowClose = true;
            Result = result;
            Close();
        }

        /// <summary>
        /// 由通知服务在重复触发时强制关闭旧窗口（视为取消，不产生解锁副作用）。
        /// </summary>
        public void ForceClose()
        {
            Result = TimeLimitDialogResult.Cancelled;
            _allowClose = true;
            Close();
        }

        private void ShowPwdButton_Click(object sender, RoutedEventArgs e)
        {
            // 随机密码临时查看一次：显示明文后立即失效（按钮禁用且不再显示）
            RandomPasswordText.Text = "本次随机密码：" + _randomPassword;
            ShowPwdButton.IsEnabled = false;
            ShowPwdButton.Content = "密码已失效";
            ErrorText.Text = string.Empty;
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            string input = PasswordInput.Password;
            if (string.IsNullOrEmpty(input))
            {
                ErrorText.Text = "请输入密码";
                return;
            }

            bool ok = false;
            if (_passwordMode == 0)
            {
                // 自定义密码：SHA256 比较
                ok = string.Equals(Sha256(input), _customPasswordHash, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                // 随机密码：明文比较（会话级）
                ok = string.Equals(input, _randomPassword, StringComparison.Ordinal);
            }

            if (ok)
            {
                RequestClose(TimeLimitDialogResult.Confirmed);
            }
            else
            {
                ErrorText.Text = "密码错误，请重试（连续错误不会锁定，可继续尝试）";
                PasswordInput.Clear();
                PasswordInput.Focus();
            }
        }

        private void ExtendButton_Click(object sender, RoutedEventArgs e)
        {
            //  延长是解锁的一种：有密码体系时必须先验证密码，
            //  否则"延长 5 分钟"可以无限连点，锁定形同虚设
            bool hasPassword = (_passwordMode == 0 && !string.IsNullOrEmpty(_customPasswordHash))
                || (_passwordMode == 1 && !string.IsNullOrEmpty(_randomPassword));
            if (hasPassword)
            {
                string input = PasswordInput.Password;
                bool ok = _passwordMode == 0
                    ? string.Equals(Sha256(input), _customPasswordHash, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(input, _randomPassword, StringComparison.Ordinal);
                if (!ok)
                {
                    ErrorText.Text = "延长需先输入正确密码";
                    PasswordInput.Clear();
                    PasswordInput.Focus();
                    return;
                }
            }

            RequestClose(TimeLimitDialogResult.Extend);
        }

        #region 应用窗口遮罩

        private void RefreshProcessIds()
        {
            _processIds.Clear();
            try
            {
                foreach (var proc in Process.GetProcessesByName(_processName))
                {
                    _processIds.Add(proc.Id);
                    proc.Dispose();
                }
            }
            catch
            {
                //  进程可能刚退出
            }
        }

        /// <summary>
        /// 枚举目标进程的全部可见顶层窗口（覆盖多显示器场景）。
        /// </summary>
        private static List<IntPtr> GetProcessWindows(IEnumerable<int> processIds)
        {
            var handles = new List<IntPtr>();
            var idSet = new HashSet<int>(processIds);
            if (idSet.Count == 0)
            {
                return handles;
            }

            Win32API.EnumWindowsProc callback = (hWnd, lParam) =>
            {
                if (!Win32API.IsWindowVisible(hWnd))
                {
                    return true;
                }

                uint pid;
                Win32API.GetWindowThreadProcessId(hWnd, out pid);
                if (idSet.Contains((int)pid))
                {
                    handles.Add(hWnd);
                }

                return true;
            };
            Win32API.EnumWindows(callback, IntPtr.Zero);
            return handles;
        }

        private bool IsAnyProcessAlive()
        {
            foreach (int id in _processIds)
            {
                try
                {
                    using (var p = Process.GetProcessById(id))
                    {
                        if (!p.HasExited)
                        {
                            return true;
                        }
                    }
                }
                catch
                {
                    //  该进程已退出
                }
            }

            return false;
        }

        /// <summary>
        /// 同步遮罩：新增/移除/移动遮罩窗口；应用进程被强制结束后返回 ProcessExited。
        /// </summary>
        private void SyncMasks()
        {
            try
            {
                //  周期性刷新 PID：锁定期间目标进程被重启（未开禁止重启时）也能补上遮罩
                if (++_syncTick % 20 == 0)
                {
                    RefreshProcessIds();
                }

                var windows = GetProcessWindows(_processIds);

                if (windows.Count == 0 && !IsAnyProcessAlive())
                {
                    //  锁定期间应用被强制结束：通知服务端禁止再次启动
                    _noWindowTimer.Stop();
                    RequestClose(TimeLimitDialogResult.ProcessExited);
                    return;
                }

                if (windows.Count == 0)
                {
                    //  进程存活但无可见窗口：启动 60 秒超时（见 _noWindowTimer 注释）
                    if (!_noWindowTimer.IsEnabled)
                    {
                        _noWindowTimer.Start();
                    }
                }
                else
                {
                    _noWindowTimer.Stop();
                }

                //  移除已消失窗口的遮罩
                foreach (var hwnd in _masks.Keys.Where(k => !windows.Contains(k)).ToList())
                {
                    var state = _masks[hwnd];
                    state.AllowClose = true;
                    try
                    {
                        state.Window.Close();
                    }
                    catch
                    {
                        //  忽略
                    }

                    _masks.Remove(hwnd);
                }

                //  新增或更新遮罩位置
                foreach (var hwnd in windows)
                {
                    MaskState state;
                    if (!_masks.TryGetValue(hwnd, out state))
                    {
                        state = new MaskState { Window = CreateMaskWindow() };
                        _masks[hwnd] = state;
                    }

                    UpdateMaskPosition(state.Window, hwnd);
                }

                PositionPanel(windows);
            }
            catch
            {
                //  枚举失败不致命，下一轮重试
            }
        }

        private System.Windows.Window CreateMaskWindow()
        {
            var mask = new System.Windows.Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Topmost = true,
                ShowActivated = false,
                Focusable = false,
                Background = new SolidColorBrush(Color.FromArgb(0xC8, 0, 0, 0)),
                Left = -10000,
                Top = -10000,
                Width = 1,
                Height = 1,
            };

            //  遮罩自身同样不允许 Alt+F4 关闭（关闭许可由 MaskState 控制）
            mask.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.System && e.SystemKey == System.Windows.Input.Key.F4)
                {
                    e.Handled = true;
                }
            };
            mask.Closing += (s, e) =>
            {
                MaskState owner = _masks.Values.FirstOrDefault(v => ReferenceEquals(v.Window, mask));
                if (owner == null || !owner.AllowClose)
                {
                    e.Cancel = true;
                }
            };
            mask.Show();
            return mask;
        }

        /// <summary>
        /// 物理像素 → DIP 换算（DPI 缩放环境下保持遮罩与目标窗口对齐）。
        /// </summary>
        private void GetDpiScale(out double scaleX, out double scaleY)
        {
            scaleX = 1;
            scaleY = 1;
            var hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            if (hwndSource?.CompositionTarget != null)
            {
                var matrix = hwndSource.CompositionTarget.TransformFromDevice;
                scaleX = matrix.M11;
                scaleY = matrix.M22;
            }
        }

        /// <summary>
        /// 按目标窗口所在显示器取 DPI 缩放：多屏混合缩放（如 100%+150%）时，
        /// 用主窗口的 DPI 换算其他屏幕的坐标会错位，必须逐屏取值
        /// </summary>
        private void GetDpiScaleForWindow(IntPtr hwnd, out double scaleX, out double scaleY)
        {
            if (UI.Base.MonitorDpi.TryGetScaleForWindow(hwnd, out scaleX, out scaleY))
            {
                return;
            }
            GetDpiScale(out scaleX, out scaleY);
        }

        private void UpdateMaskPosition(System.Windows.Window mask, IntPtr hwnd)
        {
            var rect = Win32API.GetWindowRect(hwnd);
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return;
            }

            //  用目标窗口所在屏幕的 DPI 换算（而非遮罩/主窗口自身的 DPI）
            GetDpiScaleForWindow(hwnd, out double scaleX, out double scaleY);
            mask.Left = rect.Left / scaleX;
            mask.Top = rect.Top / scaleY;
            mask.Width = rect.Width / scaleX;
            mask.Height = rect.Height / scaleY;
        }

        /// <summary>
        /// 将密码面板定位到第一个被遮罩窗口的屏幕中心。
        /// </summary>
        private void PositionPanel(List<IntPtr> windows)
        {
            if (windows == null || windows.Count == 0)
            {
                return;
            }

            var rect = Win32API.GetWindowRect(windows[0]);
            GetDpiScaleForWindow(windows[0], out double scaleX, out double scaleY);

            double width = ActualWidth > 0 ? ActualWidth : 440;
            double height = ActualHeight > 0 ? ActualHeight : 320;

            Left = rect.Left / scaleX + (rect.Width / scaleX - width) / 2;
            Top = rect.Top / scaleY + (rect.Height / scaleY - height) / 2;
        }

        protected override void OnClosed(EventArgs e)
        {
            _syncTimer?.Stop();
            _noWindowTimer?.Stop();
            _allowClose = true;

            foreach (var state in _masks.Values)
            {
                state.AllowClose = true;
                try
                {
                    state.Window.Close();
                }
                catch
                {
                    //  忽略
                }
            }

            _masks.Clear();
            base.OnClosed(e);
        }

        #endregion

        private static string Sha256(string text)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}
