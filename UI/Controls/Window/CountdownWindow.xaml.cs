using Core.Enums;
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using UI.Base.Animation;

namespace UI.Controls.Window
{
    /// <summary>
    /// 倒计时悬浮窗：默认右上角置顶、半透明、连击 3 次隐藏；归零返回 Kill 由调用方结束进程。
    /// </summary>
    public partial class CountdownWindow : System.Windows.Window
    {
        private readonly DispatcherTimer _timer;
        private int _remainingSeconds;
        private readonly bool _hideByTripleClick;
        private readonly bool _floatingTop;

        public TimeLimitDialogResult Result { get; private set; } = TimeLimitDialogResult.Cancelled;

        public CountdownWindow(
            string title,
            string message,
            int seconds,
            string processName,
            string executablePath,
            bool floatingTop = true,
            double opacity = 0.5,
            bool hideByTripleClick = true)
        {
            InitializeComponent();
            Title = title;
            _floatingTop = floatingTop;
            _hideByTripleClick = hideByTripleClick;

            _remainingSeconds = Math.Max(5, seconds);
            CountdownText.Text = _remainingSeconds.ToString();

            if (!floatingTop)
            {
                // 非悬浮：居中、不透明、恢复普通窗口样式（保留原语义）
                WindowStyle = WindowStyle.ToolWindow;
                AllowsTransparency = false;
                Background = (System.Windows.Media.Brush)FindResource("StandardBackgroundBrush");
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                ShowActivated = true;
                HintText.Visibility = Visibility.Collapsed;
                Opacity = 1.0;
            }
            else
            {
                Opacity = Math.Max(0.15, Math.Min(0.95, opacity));
                if (!hideByTripleClick)
                {
                    HintText.Visibility = Visibility.Collapsed;
                }
            }

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Timer_Tick;
            Loaded += (s, e) =>
            {
                //  弹出入场：缩放 + 淡入（倒计时窗"蹦"出来吸引注意）
                Opacity = 0;
                var popScale = new ScaleTransform(0.92, 0.92);
                RootBorder.RenderTransform = popScale;
                RootBorder.RenderTransformOrigin = new Point(0.5, 0.5);

                var spring = new SpringEase { Bounce = 0.4, EasingMode = EasingMode.EaseOut };
                popScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = spring });
                popScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = spring });
                BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, Opacity, TimeSpan.FromMilliseconds(180)));
            };
            Loaded += (s, e) =>
            {
                //  字体比例与显示缩放：按当前全局字号设置缩放本窗口
                UI.Base.UIFontScale.ApplyToWindow(this);

                if (_floatingTop)
                {
                    //  定位到鼠标所在屏幕的右上角（多显示器友好）
                    var cursorPos = System.Windows.Forms.Cursor.Position;
                    var screen = System.Windows.Forms.Screen.FromPoint(cursorPos);
                    var area = screen.WorkingArea;

                    //  像素坐标 → DIP：按鼠标所在屏幕的 DPI 换算（混合缩放下
                    //  本窗口自身 DPI 可能属于另一台显示器，会算错位置）
                    if (!UI.Base.MonitorDpi.TryGetScaleForPoint(cursorPos.X, cursorPos.Y, out double scaleX, out double scaleY))
                    {
                        var hwndSource = PresentationSource.FromVisual(this) as System.Windows.Interop.HwndSource;
                        scaleX = 1;
                        scaleY = 1;
                        if (hwndSource?.CompositionTarget != null)
                        {
                            var matrix = hwndSource.CompositionTarget.TransformFromDevice;
                            scaleX = matrix.M11;
                            scaleY = matrix.M22;
                        }
                    }

                    Left = area.Right / scaleX - ActualWidth - 24;
                    Top = area.Top / scaleY + 24;
                }
                _timer.Start();
            };
            Closed += (s, e) => _timer.Stop();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            _remainingSeconds--;
            CountdownText.Text = _remainingSeconds.ToString();
            if (_remainingSeconds <= 0)
            {
                _timer.Stop();
                Result = TimeLimitDialogResult.Kill;
                Close();
            }
        }

        private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 三击隐藏：必须先于双击判定，否则双击（拖动）分支会在第 2 次点击就 return，
            // 三击永远数不满。隐藏只是把倒计时窗收起来：计时继续、规则保持锁定，
            // 到时照常结束进程（隐藏不是绕过限制）
            if (_hideByTripleClick && e.ClickCount >= 3)
            {
                Hide();
                return;
            }

            // 双击：进入拖动模式（可把倒计时拖动到任意位置/屏幕）；双击不隐藏窗口
            if (e.ClickCount == 2)
            {
                try
                {
                    DragMove();
                }
                catch
                {
                    // 拖动被系统取消时忽略
                }
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            Result = TimeLimitDialogResult.Cancelled;
            Close();
        }

        private void ExtendButton_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            Result = TimeLimitDialogResult.Extend;
            Close();
        }
    }
}
