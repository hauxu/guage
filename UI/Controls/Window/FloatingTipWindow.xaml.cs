using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace UI.Controls.Window
{
    /// <summary>
    /// 置顶提示窗：在受限应用窗口前面弹出（Topmost、不抢焦点），
    /// 显示数秒后自动淡出关闭；点击立即关闭。用于「仅提示」的非 Windows 消息模式。
    /// </summary>
    public partial class FloatingTipWindow : System.Windows.Window
    {
        private readonly DispatcherTimer _closeTimer;
        private bool _closing;

        public FloatingTipWindow(string title, string message)
        {
            InitializeComponent();
            TitleText.Text = title;
            MessageText.Text = message;

            _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
            _closeTimer.Tick += (s, e) =>
            {
                _closeTimer.Stop();
                BeginClose();
            };

            Loaded += (s, e) =>
            {
                // 字体比例与显示缩放：按当前全局字号设置缩放本窗口
                UI.Base.UIFontScale.ApplyToWindow(this);

                // 定位到鼠标所在屏幕工作区顶部居中（多显示器友好）
                var cursorPos = System.Windows.Forms.Cursor.Position;
                var screen = System.Windows.Forms.Screen.FromPoint(cursorPos);
                var area = screen.WorkingArea;

                var hwndSource = PresentationSource.FromVisual(this) as System.Windows.Interop.HwndSource;
                double scaleX = 1;
                if (hwndSource?.CompositionTarget != null)
                {
                    scaleX = hwndSource.CompositionTarget.TransformFromDevice.M11;
                }

                Left = area.Left / scaleX + (area.Width / scaleX - ActualWidth) / 2;
                Top = area.Top / scaleX + 28;

                Opacity = 0;
                //  从上方重力下坠入场（通知从顶部掉下来），带落地反弹
                var translate = new System.Windows.Media.TranslateTransform(0, -26);
                RenderTransform = translate;
                RenderTransformOrigin = new Point(0.5, 0);

                BeginAnimation(UIElement.OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));

                translate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(-26, 0, TimeSpan.FromMilliseconds(450))
                    {
                        EasingFunction = new UI.Base.Animation.GravityBounceEase()
                    });

                _closeTimer.Start();
            };
            Closed += (s, e) => _closeTimer.Stop();
        }

        private void BeginClose()
        {
            if (_closing)
            {
                return;
            }
            _closing = true;
            _closeTimer.Stop();
            //  上浮退场（与下坠入场对称）
            var translate = RenderTransform as System.Windows.Media.TranslateTransform;
            if (translate != null)
            {
                var up = new System.Windows.Media.Animation.DoubleAnimation(0, -26, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
                };
                translate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, up);
            }
            var fade = new System.Windows.Media.Animation.DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(200));
            fade.Completed += (s, e) => Close();
            BeginAnimation(UIElement.OpacityProperty, fade);
        }

        private void TipCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            BeginClose();
            e.Handled = true;
        }
    }
}
