using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace UI.Controls.Base
{
    public class Placeholder : Control
    {
        public CornerRadius CornerRadius
        {
            get { return (CornerRadius)GetValue(CornerRadiusProperty); }
            set { SetValue(CornerRadiusProperty, value); }
        }
        public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register("CornerRadius",
                typeof(CornerRadius),
                typeof(Placeholder));

        private Border Flash;
        private bool IsAddEvent = false;
        private Storyboard _storyboard;

        public Placeholder()
        {
            DefaultStyleKey = typeof(Placeholder);
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            Flash = GetTemplateChild("Flash") as Border;

            if (!IsAddEvent)
            {
                IsAddEvent = true;
                Loaded += Placeholder_Loaded;
                Unloaded += Placeholder_Unloaded;
                IsVisibleChanged += Placeholder_IsVisibleChanged;
            }

            UpdateAnimation();
        }

        private void Placeholder_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateAnimation();
        }

        private void Placeholder_Unloaded(object sender, RoutedEventArgs e)
        {
            StopAnimation();
        }

        private void Placeholder_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            UpdateAnimation();
        }

        /// <summary>
        /// 闪烁动画只在可见时跑。列表每一行都带占位块，动画永不停会逼着整窗
        /// 一直按 60fps 重绘——数据一多 CPU 直接吃满。
        /// </summary>
        private void UpdateAnimation()
        {
            if (IsVisible && IsLoaded)
            {
                StartAnimation();
            }
            else
            {
                StopAnimation();
            }
        }

        private void StartAnimation()
        {
            if (Flash == null || _storyboard != null)
            {
                return;
            }

            var scrollAnimation = new DoubleAnimation
            {
                From = -10,
                To = Math.Max(ActualWidth, 40),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn },
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                Duration = new Duration(TimeSpan.FromSeconds(1))
            };

            Storyboard.SetTarget(scrollAnimation, Flash);
            Storyboard.SetTargetProperty(scrollAnimation, new PropertyPath("RenderTransform.Children[0].X"));

            _storyboard = new Storyboard();
            _storyboard.Children.Add(scrollAnimation);
            _storyboard.Begin();
        }

        private void StopAnimation()
        {
            if (_storyboard == null)
            {
                return;
            }

            try
            {
                _storyboard.Stop();
                _storyboard.Remove();
            }
            catch
            {
                //  停动画失败不该影响 UI 线程
            }

            _storyboard = null;
        }
    }
}
