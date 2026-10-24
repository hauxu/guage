using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using UI.Base.Animation;

namespace UI.Controls.Window
{
    /// <summary>
    /// 休息提醒弹窗：阶段完成时弹出的非模态置顶小窗
    /// </summary>
    public partial class BreakReminderWindow : System.Windows.Window
    {
        public BreakReminderWindow()
        {
            InitializeComponent();

            // 图标颜色跟随应用主题色（资源不可用时保持默认紫色）
            try
            {
                var themeBrush = Application.Current?.TryFindResource("ThemeBrush") as Brush;
                if (themeBrush != null)
                {
                    ReminderIcon.Foreground = themeBrush;
                }
            }
            catch (Exception)
            {
                // 保持默认色
            }

            //  弹出入场：内容根 Border 缩放（Window 本身不支持 RenderTransform，
            //  对 Window 赋值会在 HwndTarget.OnResize 广播时抛 InvalidOperationException）
            Loaded += (s, e) =>
            {
                Opacity = 0;
                var scale = new ScaleTransform(0.9, 0.9);
                RootBorder.RenderTransform = scale;
                RootBorder.RenderTransformOrigin = new Point(0.5, 0.5);

                var spring = new SpringEase { Bounce = 0.45, EasingMode = EasingMode.EaseOut };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = spring });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = spring });
                BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));

                //  离开座位期间弹出的提醒不该永远置顶悬停：
                //  30 秒后取消置顶，60 秒自动关闭（定时器随 Closed 停止，无泄漏）
                var topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
                topmostTimer.Tick += (s1, e1) =>
                {
                    topmostTimer.Stop();
                    Topmost = false;
                };
                closeTimer.Tick += (s1, e1) =>
                {
                    closeTimer.Stop();
                    Close();
                };
                topmostTimer.Start();
                closeTimer.Start();
                Closed += (s1, e1) =>
                {
                    topmostTimer.Stop();
                    closeTimer.Stop();
                };
            };
        }

        /// <summary>
        /// 更新并显示提醒内容（窗口复用时调用）
        /// </summary>
        public void UpdateContent(string title, string message)
        {
            TitleText.Text = title;
            MessageText.Text = message;
            Title = title;
        }
        private void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
