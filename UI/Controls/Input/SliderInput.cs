using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using UI.Base.Animation;

namespace UI.Controls.Input
{
    /// <summary>
    /// 可拖动的数值滑块，右侧带数字输入框，两者双向同步。
    /// 拖动/键入的 Value 变更按 80ms 合并后再通知下游（避免下游全量管线被
    /// 60-125Hz 的鼠标事件打满）；填充与滑块位置由 FillRatio 动画驱动，平滑跟手。
    /// </summary>
    public class SliderInput : Control
    {
        public int Value
        {
            get { return (int)GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(int), typeof(SliderInput),
                new PropertyMetadata(0, new PropertyChangedCallback(OnValueChanged)));

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = d as SliderInput;
            if (control == null || e.NewValue.Equals(e.OldValue))
            {
                return;
            }

            int clamped = control.Clamp((int)e.NewValue);
            if (clamped != (int)e.NewValue)
            {
                control.Value = clamped;
                return;
            }

            control.SyncVisual();
            control.AnimateFill();
            control.RaiseValueChangedThrottled();
        }

        /// <summary>
        /// 填充比例（0-1）：动画此值驱动滑轨星号权重，填充与滑块平滑移动
        /// </summary>
        private double FillRatio
        {
            get { return (double)GetValue(FillRatioProperty); }
            set { SetValue(FillRatioProperty, value); }
        }
        private static readonly DependencyProperty FillRatioProperty =
            DependencyProperty.Register("FillRatio", typeof(double), typeof(SliderInput),
                new PropertyMetadata(0.0, new PropertyChangedCallback(OnFillRatioChanged)));

        private static void OnFillRatioChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = d as SliderInput;
            if (control != null)
            {
                control.ApplyFillRatio((double)e.NewValue);
            }
        }

        private void ApplyFillRatio(double ratio)
        {
            if (Track != null && Track.ColumnDefinitions.Count >= 2)
            {
                ratio = Math.Min(1, Math.Max(0, ratio));
                Track.ColumnDefinitions[0].Width = new GridLength(ratio, GridUnitType.Star);
                Track.ColumnDefinitions[1].Width = new GridLength(1 - ratio, GridUnitType.Star);
            }
        }

        public int Minimum
        {
            get { return (int)GetValue(MinimumProperty); }
            set { SetValue(MinimumProperty, value); }
        }
        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register("Minimum", typeof(int), typeof(SliderInput), new PropertyMetadata(0));

        public int Maximum
        {
            get { return (int)GetValue(MaximumProperty); }
            set { SetValue(MaximumProperty, value); }
        }
        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register("Maximum", typeof(int), typeof(SliderInput), new PropertyMetadata(100));

        /// <summary>
        /// 数值单位，显示在输入框右侧
        /// </summary>
        public string Unit
        {
            get { return (string)GetValue(UnitProperty); }
            set { SetValue(UnitProperty, value); }
        }
        public static readonly DependencyProperty UnitProperty =
            DependencyProperty.Register("Unit", typeof(string), typeof(SliderInput), new PropertyMetadata(string.Empty));

        public event RoutedEventHandler ValueChanged;

        /// <summary>
        /// 拖动/输入提交完成（松手或输入框失焦）：重活（如 accent 重提交）应只在此触发
        /// </summary>
        public event EventHandler DragCompleted;

        private Grid Track;
        private InputBox ValueBox;
        private System.Windows.Media.ScaleTransform ThumbScale;
        //  防止输入框与滑块相互赋值造成循环
        private bool isSyncing;
        private bool _isDragging;

        //  下游通知节流：拖动中最多 80ms 一发，松手/失焦补发最终值
        private readonly System.Diagnostics.Stopwatch _notifyWatch = System.Diagnostics.Stopwatch.StartNew();
        private double _lastNotifyMs = -1000;
        private bool _notifyPending;
        private DispatcherTimer _notifyTimer;

        public SliderInput()
        {
            DefaultStyleKey = typeof(SliderInput);
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            Track = GetTemplateChild("Track") as Grid;
            ValueBox = GetTemplateChild("ValueBox") as InputBox;
            ThumbScale = GetTemplateChild("ThumbScale") as System.Windows.Media.ScaleTransform;

            if (Track != null)
            {
                Track.MouseLeftButtonDown += Track_MouseLeftButtonDown;
                Track.MouseMove += Track_MouseMove;
                Track.MouseLeftButtonUp += Track_MouseLeftButtonUp;
            }

            if (ValueBox != null)
            {
                ValueBox.TextChanged += ValueBox_TextChanged;
                ValueBox.LostFocus += ValueBox_LostFocus;
            }

            SyncVisual();
        }

        private int Clamp(int value)
        {
            return Math.Min(Maximum, Math.Max(Minimum, value));
        }

        private double CurrentRatio()
        {
            double range = Math.Max(1, Maximum - Minimum);
            return (Value - Minimum) / range;
        }

        /// <summary>
        /// 把当前值同步到输入框文本（填充位置由 AnimateFill 驱动）。
        /// </summary>
        private void SyncVisual()
        {
            if (ValueBox != null && !isSyncing)
            {
                isSyncing = true;
                ValueBox.Text = Value.ToString();
                isSyncing = false;
            }
        }

        /// <summary>
        /// 填充/滑块平滑移动到当前值对应位置：拖动中 60ms 短过渡跟手，
        /// 非拖动（程序设值/载入）用弹簧到位。
        /// </summary>
        private void AnimateFill()
        {
            double target = CurrentRatio();
            IEasingFunction easing;
            if (_isDragging)
            {
                easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            }
            else
            {
                easing = new SpringEase { Bounce = 0.18, EasingMode = EasingMode.EaseOut };
            }

            var anim = new DoubleAnimation(target, _isDragging
                ? TimeSpan.FromMilliseconds(60)
                : TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = easing
            };
            BeginAnimation(FillRatioProperty, anim, HandoffBehavior.SnapshotAndReplace);
        }

        private void SetValueFromPoint(Point point)
        {
            if (Track == null || Track.ActualWidth <= 0)
            {
                return;
            }
            double ratio = Math.Min(1, Math.Max(0, point.X / Track.ActualWidth));
            Value = Minimum + (int)Math.Round(ratio * (Maximum - Minimum));
        }

        private void Track_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Track.CaptureMouse();
            _isDragging = true;

            //  按下：滑块弹性放大，提示"抓住"了
            if (ThumbScale != null)
            {
                var press = new DoubleAnimation(1.15, TimeSpan.FromMilliseconds(90))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                ThumbScale.BeginAnimation(ScaleTransform.ScaleXProperty, press);
                ThumbScale.BeginAnimation(ScaleTransform.ScaleYProperty, press);
            }

            SetValueFromPoint(e.GetPosition(Track));
        }

        private void Track_MouseMove(object sender, MouseEventArgs e)
        {
            if (Track.IsMouseCaptured)
            {
                SetValueFromPoint(e.GetPosition(Track));
            }
        }

        private void Track_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            Track.ReleaseMouseCapture();
            EndDrag();
        }

        /// <summary>
        /// 松手：滑块弹簧回弹，立即补发被节流的最终值，并通知拖动提交完成
        /// </summary>
        private void EndDrag()
        {
            _isDragging = false;

            if (ThumbScale != null)
            {
                var release = new DoubleAnimation(1, TimeSpan.FromMilliseconds(240))
                {
                    EasingFunction = new SpringEase { Bounce = 0.38, EasingMode = EasingMode.EaseOut }
                };
                ThumbScale.BeginAnimation(ScaleTransform.ScaleXProperty, release);
                ThumbScale.BeginAnimation(ScaleTransform.ScaleYProperty, release);
            }

            FlushPendingNotify();
            DragCompleted?.Invoke(this, EventArgs.Empty);
        }

        private void RaiseValueChangedThrottled()
        {
            if (ValueChanged == null)
            {
                return;
            }

            double now = _notifyWatch.Elapsed.TotalMilliseconds;
            if (now - _lastNotifyMs >= 80)
            {
                _lastNotifyMs = now;
                _notifyPending = false;
                ValueChanged.Invoke(this, new RoutedEventArgs());
                return;
            }

            _notifyPending = true;
            if (_notifyTimer == null)
            {
                _notifyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
                _notifyTimer.Tick += (s, e) =>
                {
                    _notifyTimer.Stop();
                    if (_notifyPending)
                    {
                        _notifyPending = false;
                        _lastNotifyMs = _notifyWatch.Elapsed.TotalMilliseconds;
                        ValueChanged?.Invoke(this, new RoutedEventArgs());
                    }
                };
            }
            _notifyTimer.Stop();
            _notifyTimer.Start();
        }

        private void FlushPendingNotify()
        {
            if (_notifyTimer != null)
            {
                _notifyTimer.Stop();
            }
            if (_notifyPending)
            {
                _notifyPending = false;
                _lastNotifyMs = _notifyWatch.Elapsed.TotalMilliseconds;
                ValueChanged?.Invoke(this, new RoutedEventArgs());
            }
        }

        private void ValueBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (isSyncing)
            {
                return;
            }

            int parsed;
            if (!int.TryParse(ValueBox.Text, out parsed))
            {
                return;
            }

            //  输入过程中不回写文本，否则光标会被打断
            isSyncing = true;
            Value = Clamp(parsed);
            isSyncing = false;
        }

        private void ValueBox_LostFocus(object sender, RoutedEventArgs e)
        {
            //  失焦时把非法输入或越界值纠正回来
            isSyncing = false;
            SyncVisual();
            AnimateFill();
            FlushPendingNotify();
            DragCompleted?.Invoke(this, EventArgs.Empty);
        }
    }
}
