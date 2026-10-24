using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using UI.Base.Animation;

namespace UI.Controls.Base
{
    /// <summary>
    /// 文本超出宽度时以两段相同内容首尾相连、无缝循环滚动；未超出时静态显示。
    /// </summary>
    public class MarqueeTextBlock : Border
    {
        private const string LoopSeparator = "   ";
        private const double ScrollPixelsPerSecond = 48d;

        private readonly Grid _root;
        private readonly TextBlock _measureBlock;
        private readonly TextBlock _staticText;
        private readonly Canvas _marqueeHost;
        private readonly Canvas _track;
        private readonly TextBlock _segment1;
        private readonly TextBlock _segment2;
        private readonly TranslateTransform _trackTransform;

        private DoubleAnimation _marqueeAnimation;
        private bool _isOverflow;
        private bool _staticOverflowClipped;
        private bool _marqueePausedByUser;
        private bool _layoutUpdateScheduled;
        private bool _waitingForWidth;
        private double _lastViewportWidth;
        private string _lastSegmentText;
        private FrameworkElement _widthSource;

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(nameof(Text), typeof(string), typeof(MarqueeTextBlock),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure, OnTextChanged));

        public static readonly DependencyProperty TextFontSizeProperty =
            DependencyProperty.Register(nameof(TextFontSize), typeof(double), typeof(MarqueeTextBlock),
                new PropertyMetadata(15d, OnTextAppearanceChanged));

        public static readonly DependencyProperty TextFontWeightProperty =
            DependencyProperty.Register(nameof(TextFontWeight), typeof(FontWeight), typeof(MarqueeTextBlock),
                new PropertyMetadata(FontWeights.SemiBold, OnTextAppearanceChanged));

        public static readonly DependencyProperty TextForegroundProperty =
            DependencyProperty.Register(nameof(TextForeground), typeof(Brush), typeof(MarqueeTextBlock),
                new PropertyMetadata(null, OnTextAppearanceChanged));

        public static readonly DependencyProperty CenterStaticTextProperty =
            DependencyProperty.Register(nameof(CenterStaticText), typeof(bool), typeof(MarqueeTextBlock),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure, OnCenterStaticTextChanged));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public double TextFontSize
        {
            get => (double)GetValue(TextFontSizeProperty);
            set => SetValue(TextFontSizeProperty, value);
        }

        public FontWeight TextFontWeight
        {
            get => (FontWeight)GetValue(TextFontWeightProperty);
            set => SetValue(TextFontWeightProperty, value);
        }

        public Brush TextForeground
        {
            get => (Brush)GetValue(TextForegroundProperty);
            set => SetValue(TextForegroundProperty, value);
        }

        /// <summary>
        /// 静态显示（非跑马灯）时，文本在可视宽度内居中；超长截断时仍左对齐。
        /// </summary>
        public bool CenterStaticText
        {
            get => (bool)GetValue(CenterStaticTextProperty);
            set => SetValue(CenterStaticTextProperty, value);
        }

        public MarqueeTextBlock()
        {
            Background = null;
            BorderBrush = null;
            BorderThickness = new Thickness(0);
            Padding = new Thickness(0, 1, 0, 1);
            HorizontalAlignment = HorizontalAlignment.Stretch;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            ClipToBounds = true;

            //  整个子树退出字体缩放（O(1) 判断，替代逐元素父链回溯）
            UI.Base.UIFontScale.SetExcludeScale(this, true);

            _measureBlock = CreateTextBlock();
            _measureBlock.Visibility = Visibility.Collapsed;

            _staticText = CreateTextBlock();
            _staticText.HorizontalAlignment = HorizontalAlignment.Stretch;
            _staticText.TextAlignment = TextAlignment.Left;
            _staticText.VerticalAlignment = VerticalAlignment.Center;

            _segment1 = CreateTextBlock();
            _segment2 = CreateTextBlock();

            _trackTransform = new TranslateTransform();
            _track = new Canvas
            {
                RenderTransform = _trackTransform
            };
            _track.Children.Add(_segment1);
            _track.Children.Add(_segment2);

            _marqueeHost = new Canvas
            {
                Visibility = Visibility.Collapsed,
                ClipToBounds = true,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };
            _marqueeHost.Children.Add(_track);

            _root = new Grid();
            _root.Children.Add(_measureBlock);
            _root.Children.Add(_staticText);
            _root.Children.Add(_marqueeHost);
            Child = _root;

            Loaded += (_, __) => RequestLayoutUpdate();
            Unloaded += (_, __) =>
            {
                DetachWidthSource();
                StopWaitingForWidth();
                StopMarquee();
            };
            SizeChanged += (_, __) => RequestLayoutUpdate();
            IsVisibleChanged += (_, __) =>
            {
                if (!IsVisible)
                {
                    PauseMarquee();
                    return;
                }

                ResumeMarqueeIfNeeded();
                RequestLayoutUpdate();
            };
        }

        protected override void OnVisualParentChanged(DependencyObject oldParent)
        {
            base.OnVisualParentChanged(oldParent);
            DetachWidthSource();
            AttachWidthSource();
            RequestLayoutUpdate();
        }

        protected override Size MeasureOverride(Size constraint)
        {
            ApplyAppearance();

            var width = ResolveContentMeasureWidth(constraint.Width);
            if (width > 0 && !double.IsInfinity(width))
            {
                var innerWidth = Math.Max(0, width - Padding.Left - Padding.Right);
                var innerHeight = double.IsInfinity(constraint.Height)
                    ? double.PositiveInfinity
                    : Math.Max(0, constraint.Height - Padding.Top - Padding.Bottom);
                _root.Measure(new Size(innerWidth, innerHeight));

                var height = _root.DesiredSize.Height + Padding.Top + Padding.Bottom;
                if (!double.IsInfinity(constraint.Height) && constraint.Height > 0)
                {
                    height = Math.Max(height, 1);
                    if (!double.IsInfinity(constraint.Height))
                    {
                        height = Math.Min(height, constraint.Height);
                    }
                }

                return new Size(width, Math.Max(height, 1));
            }

            return base.MeasureOverride(constraint);
        }

        private double ResolveContentMeasureWidth(double constraintWidth)
        {
            var maxWidth = GetEffectiveMaxWidth();
            var text = Text ?? string.Empty;
            if (CenterStaticText && !string.IsNullOrEmpty(text))
            {
                var textWidth = MeasureTextWidth(text);
                if (textWidth > 0 && (maxWidth <= 0 || textWidth <= maxWidth + 0.5))
                {
                    var width = textWidth + Padding.Left + Padding.Right;
                    if (maxWidth > 0)
                    {
                        width = Math.Min(width, maxWidth);
                    }

                    if (constraintWidth > 0 && !double.IsInfinity(constraintWidth))
                    {
                        width = Math.Min(width, constraintWidth);
                    }

                    return width;
                }
            }

            return ResolveMeasureWidth(constraintWidth);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var innerWidth = Math.Max(0, finalSize.Width - Padding.Left - Padding.Right);
            var innerHeight = Math.Max(0, finalSize.Height - Padding.Top - Padding.Bottom);
            _root.Arrange(new Rect(Padding.Left, Padding.Top, innerWidth, innerHeight));
            _marqueeHost.Width = innerWidth;
            return finalSize;
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == TextForegroundProperty
                || e.Property == TextFontSizeProperty
                || e.Property == TextFontWeightProperty
                || e.Property == MaxWidthProperty
                || e.Property == WidthProperty
                || e.Property == MarginProperty)
            {
                if (e.Property != TextForegroundProperty
                    && e.Property != TextFontSizeProperty
                    && e.Property != TextFontWeightProperty)
                {
                    _lastSegmentText = null;
                }
                else
                {
                    ApplyAppearance();
                }

                RequestLayoutUpdate();
            }
        }

        public void RefreshLayout()
        {
            _lastSegmentText = null;
            _lastViewportWidth = 0;
            RequestLayoutUpdate();
        }

        public void PauseMarqueeAnimation()
        {
            if (!_isOverflow)
            {
                return;
            }

            _marqueePausedByUser = true;
            PauseMarquee();
        }

        public void ResumeMarqueeAnimation()
        {
            if (!_isOverflow || !_marqueePausedByUser)
            {
                return;
            }

            _marqueePausedByUser = false;
            if (IsLoaded && IsVisible && AnimationBudget.AllowMarquee)
            {
                ResumeMarqueeFromCurrentPosition();
            }
        }

        private static TextBlock CreateTextBlock()
        {
            var textBlock = new TextBlock
            {
                Background = null,
                TextTrimming = TextTrimming.None,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            TextOptions.SetTextFormattingMode(textBlock, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(textBlock, TextRenderingMode.Auto);
            return textBlock;
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (MarqueeTextBlock)d;
            control._staticText.Text = control.Text ?? string.Empty;
            control._lastSegmentText = null;
            control._lastViewportWidth = 0;
            control.RequestLayoutUpdate();
        }

        private static void OnTextAppearanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (MarqueeTextBlock)d;
            control.ApplyAppearance();
            control._lastSegmentText = null;
            control._lastViewportWidth = 0;
            control.RequestLayoutUpdate();
        }

        private static void OnCenterStaticTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (MarqueeTextBlock)d;
            if (control._staticText.Visibility == Visibility.Visible)
            {
                control.ApplyStaticTextAlignment(control._staticOverflowClipped);
            }

            control.InvalidateMeasure();
        }

        private void ApplyAppearance()
        {
            foreach (var textBlock in new[] { _measureBlock, _staticText, _segment1, _segment2 })
            {
                textBlock.FontSize = TextFontSize;
                textBlock.FontWeight = TextFontWeight;
                textBlock.TextTrimming = TextTrimming.None;
                if (TextForeground != null)
                {
                    textBlock.Foreground = TextForeground;
                }
            }
        }

        private void AttachWidthSource()
        {
            _widthSource = FindWidthSource(Parent as FrameworkElement);
            if (_widthSource == null)
            {
                return;
            }

            _widthSource.SizeChanged += WidthSource_SizeChanged;
        }

        private void DetachWidthSource()
        {
            if (_widthSource == null)
            {
                return;
            }

            _widthSource.SizeChanged -= WidthSource_SizeChanged;
            _widthSource = null;
        }

        private void WidthSource_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.WidthChanged)
            {
                _lastSegmentText = null;
                _lastViewportWidth = 0;
                RequestLayoutUpdate();
            }
        }

        private static FrameworkElement FindWidthSource(FrameworkElement start)
        {
            var current = start;
            while (current != null)
            {
                if (current.ActualWidth > 0)
                {
                    return current;
                }

                current = VisualTreeHelper.GetParent(current) as FrameworkElement;
            }

            return start;
        }

        private double ResolveMeasureWidth(double constraintWidth)
        {
            var maxWidth = GetEffectiveMaxWidth();
            if (maxWidth > 0)
            {
                if (constraintWidth > 0 && !double.IsInfinity(constraintWidth))
                {
                    return Math.Min(constraintWidth, maxWidth);
                }

                return maxWidth;
            }

            if (constraintWidth > 0 && !double.IsInfinity(constraintWidth))
            {
                return constraintWidth;
            }

            if (Width > 0 && !double.IsInfinity(Width))
            {
                return Width;
            }

            return GetViewportWidth();
        }

        private double GetEffectiveMaxWidth()
        {
            if (MaxWidth > 0 && !double.IsInfinity(MaxWidth))
            {
                return MaxWidth;
            }

            if (Width > 0 && !double.IsInfinity(Width))
            {
                return Width;
            }

            return 0;
        }

        private double GetViewportWidth()
        {
            var maxWidth = GetEffectiveMaxWidth();
            if (maxWidth > 0)
            {
                return Math.Max(0, maxWidth - Padding.Left - Padding.Right);
            }

            var width = 0d;

            if (ActualWidth > 0)
            {
                width = ActualWidth;
            }
            else if (RenderSize.Width > 0)
            {
                width = RenderSize.Width;
            }
            else if (maxWidth > 0)
            {
                width = maxWidth;
            }
            else if (Width > 0 && !double.IsInfinity(Width))
            {
                width = Width;
            }
            else
            {
                var parent = Parent as FrameworkElement;
                if (parent != null && parent.ActualWidth > 0)
                {
                    width = parent.ActualWidth - Margin.Left - Margin.Right;
                }
            }

            width = Math.Max(0, width - Padding.Left - Padding.Right);

            if (maxWidth > 0 && width > maxWidth)
            {
                width = maxWidth;
            }

            return width;
        }

        private void RequestLayoutUpdate()
        {
            if (_layoutUpdateScheduled)
            {
                return;
            }

            _layoutUpdateScheduled = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _layoutUpdateScheduled = false;
                UpdateOverflowState();
            }), DispatcherPriority.Loaded);

            Dispatcher.BeginInvoke(new Action(UpdateOverflowState), DispatcherPriority.Render);
        }

        private void BeginWaitingForWidth()
        {
            if (_waitingForWidth)
            {
                return;
            }

            _waitingForWidth = true;
            LayoutUpdated += OnLayoutUpdatedWhileWaitingWidth;
        }

        private void StopWaitingForWidth()
        {
            if (!_waitingForWidth)
            {
                return;
            }

            _waitingForWidth = false;
            LayoutUpdated -= OnLayoutUpdatedWhileWaitingWidth;
        }

        private void OnLayoutUpdatedWhileWaitingWidth(object sender, EventArgs e)
        {
            if (GetViewportWidth() > 0)
            {
                StopWaitingForWidth();
                UpdateOverflowState();
            }
        }

        private double MeasureTextWidth(string content)
        {
            _measureBlock.Text = content ?? string.Empty;

            var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (pixelsPerDip <= 0)
            {
                pixelsPerDip = 1;
            }

            _measureBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            var width = _measureBlock.DesiredSize.Width;
            if (width <= 0 && !string.IsNullOrEmpty(content))
            {
                var formatted = new FormattedText(
                    content,
                    System.Globalization.CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    new Typeface(_measureBlock.FontFamily, _measureBlock.FontStyle, _measureBlock.FontWeight, _measureBlock.FontStretch),
                    _measureBlock.FontSize,
                    Brushes.Black,
                    pixelsPerDip);
                width = formatted.WidthIncludingTrailingWhitespace;
            }

            return width;
        }

        private void UpdateOverflowState()
        {
            ApplyAppearance();

            var text = Text ?? string.Empty;
            _staticText.Text = text;

            if (string.IsNullOrEmpty(text))
            {
                ShowStaticMode();
                StopWaitingForWidth();
                return;
            }

            var viewportWidth = GetViewportWidth();
            if (viewportWidth <= 0)
            {
                ShowStaticMode();
                BeginWaitingForWidth();
                return;
            }

            StopWaitingForWidth();

            var textWidth = MeasureTextWidth(text);
            var overflow = textWidth > viewportWidth + 0.5;
            ToolTip = overflow ? text : null;

            if (!overflow)
            {
                ShowStaticMode();
                _lastViewportWidth = viewportWidth;
                _lastSegmentText = null;
                InvalidateMeasure();
                return;
            }

            if (!AnimationBudget.AllowMarquee)
            {
                ShowStaticMode(overflowClipped: true);
                ToolTip = text;
                return;
            }

            var segmentText = text + LoopSeparator;
            var segmentWidth = MeasureTextWidth(segmentText);
            if (segmentWidth <= 0)
            {
                ShowStaticMode();
                return;
            }

            _lastViewportWidth = viewportWidth;
            _lastSegmentText = segmentText;
            _isOverflow = true;

            ShowMarqueeMode(segmentText, segmentWidth);
            InvalidateMeasure();

            if (IsLoaded && IsVisible && !_marqueePausedByUser)
            {
                StartMarquee(segmentWidth);
            }
        }

        private void ResumeMarqueeIfNeeded()
        {
            if (!_isOverflow || !IsLoaded || !IsVisible || string.IsNullOrEmpty(_lastSegmentText) || _marqueePausedByUser)
            {
                return;
            }

            if (!AnimationBudget.AllowMarquee)
            {
                return;
            }

            ResumeMarqueeFromCurrentPosition();
        }

        private void ResumeMarqueeFromCurrentPosition()
        {
            var segmentWidth = MeasureTextWidth(_lastSegmentText);
            if (segmentWidth <= 0)
            {
                return;
            }

            StopMarquee(resetTransform: false);

            var currentX = _trackTransform.X;
            while (currentX <= -segmentWidth)
            {
                currentX += segmentWidth;
            }

            while (currentX > 0)
            {
                currentX -= segmentWidth;
            }

            _trackTransform.X = currentX;

            var scrollSeconds = Math.Max(3.5, segmentWidth / ScrollPixelsPerSecond);
            _marqueeAnimation = new DoubleAnimation
            {
                From = currentX,
                To = currentX - segmentWidth,
                Duration = TimeSpan.FromSeconds(scrollSeconds),
                RepeatBehavior = RepeatBehavior.Forever
            };

            _trackTransform.BeginAnimation(TranslateTransform.XProperty, _marqueeAnimation);
        }

        private void PauseMarquee()
        {
            if (_marqueeAnimation == null)
            {
                return;
            }

            //  移除动画前先把当前动画值写入本地值：直接 BeginAnimation(X, null)
            //  会让 X 立即回落到基础值 0，恢复时从起点重新滚（视觉跳变）
            double currentX = _trackTransform.X;
            _trackTransform.BeginAnimation(TranslateTransform.XProperty, null);
            _trackTransform.X = currentX;
        }

        private void ShowStaticMode(bool overflowClipped = false)
        {
            _isOverflow = false;
            _marqueePausedByUser = false;
            _staticOverflowClipped = overflowClipped;
            StopMarquee();
            ApplyStaticTextAlignment(overflowClipped);
            _staticText.Visibility = Visibility.Visible;
            _marqueeHost.Visibility = Visibility.Collapsed;
        }

        private void ApplyStaticTextAlignment(bool overflowClipped = false)
        {
            if (CenterStaticText && !overflowClipped)
            {
                _staticText.HorizontalAlignment = HorizontalAlignment.Center;
                _staticText.TextAlignment = TextAlignment.Center;
            }
            else
            {
                _staticText.HorizontalAlignment = HorizontalAlignment.Stretch;
                _staticText.TextAlignment = TextAlignment.Left;
            }
        }

        private void ShowMarqueeMode(string segmentText, double segmentWidth)
        {
            _staticText.Visibility = Visibility.Collapsed;
            _marqueeHost.Visibility = Visibility.Visible;

            _segment1.Text = segmentText;
            _segment2.Text = segmentText;

            _segment1.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var textHeight = _segment1.DesiredSize.Height;
            _marqueeHost.Height = textHeight;
            _track.Height = textHeight;

            Canvas.SetLeft(_segment1, 0);
            Canvas.SetTop(_segment1, 0);
            Canvas.SetLeft(_segment2, segmentWidth);
            Canvas.SetTop(_segment2, 0);
            _track.Width = segmentWidth * 2;
        }

        private void StartMarquee(double segmentWidth)
        {
            StopMarquee(resetTransform: true);

            var scrollSeconds = Math.Max(3.5, segmentWidth / ScrollPixelsPerSecond);
            _marqueeAnimation = new DoubleAnimation
            {
                From = 0,
                To = -segmentWidth,
                Duration = TimeSpan.FromSeconds(scrollSeconds),
                RepeatBehavior = RepeatBehavior.Forever
            };

            _trackTransform.BeginAnimation(TranslateTransform.XProperty, _marqueeAnimation);
        }

        private void StopMarquee(bool resetTransform = true)
        {
            _trackTransform.BeginAnimation(TranslateTransform.XProperty, null);
            if (resetTransform)
            {
                _trackTransform.X = 0;
            }

            _marqueeAnimation = null;
        }
    }
}
