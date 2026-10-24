using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using UI.Base.Animation;

namespace UI.Controls
{
    /// <summary>
    /// 一柱香燃烧控件（可复用）：竖直香体随 Progress 从顶端向下燃烧。
    /// 立体感：圆柱受光渐变 + 左缘高光 + 环纹 + 底部竹芯；燃烧三区（燃点红光呼吸 →
    /// 炭化带 → 正常香体）；香灰分段悬挂、间歇重力断落；青烟细丝先直后蜿蜒、随风横漂。
    /// 帧循环 30fps 门限 + AnimationBudget 门控（后台跳帧、ReduceMotion 静态香）。
    /// </summary>
    public class IncenseBurnControl : Canvas
    {
        // ---- 设计尺寸（Viewbox 由宿主等比缩放）----
        public const double DesignWidth = 240;
        public const double DesignHeight = 620;

        private const double X = 120;               // 香的水平中心
        private const double BaseY = 556;           // 香底（插在香座）
        private const double MaxLen = 430;          // 满香长度
        private const double StickW = 8;            // 香体宽
        private const double TipCatch = 6;          // 燃点绘制中心偏移

        public static readonly DependencyProperty ProgressProperty =
            DependencyProperty.Register(nameof(Progress), typeof(double), typeof(IncenseBurnControl),
                new PropertyMetadata(1.0, new PropertyChangedCallback(OnProgressChanged)));

        /// <summary>燃烧比例 0-1（1=满香 0=燃尽）</summary>
        public double Progress
        {
            get { return (double)GetValue(ProgressProperty); }
            set { SetValue(ProgressProperty, value); }
        }

        private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (IncenseBurnControl)d;
            c._progressTarget = double.IsNaN(c.Progress) ? 1 : Math.Max(0, Math.Min(1, c.Progress));
            if (!c._loopActive)
            {
                c._smoothedProgress = c._progressTarget;
                c.UpdateIncense(c._smoothedProgress);
            }
        }

        public static readonly DependencyProperty IsBurningProperty =
            DependencyProperty.Register(nameof(IsBurning), typeof(bool), typeof(IncenseBurnControl),
                new PropertyMetadata(false, new PropertyChangedCallback(OnIsBurningChanged)));

        /// <summary>燃烧中：启动帧循环（燃点/烟/灰特效）</summary>
        public bool IsBurning
        {
            get { return (bool)GetValue(IsBurningProperty); }
            set { SetValue(IsBurningProperty, value); }
        }

        private static void OnIsBurningChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (IncenseBurnControl)d;
            if ((bool)e.NewValue)
            {
                c.StartLoop();
            }
            else
            {
                c.StopLoop();
                c.SetSmokeVisible(false);
                c.UpdateIncense(c._smoothedProgress);
            }
        }

        public static readonly DependencyProperty IsPausedProperty =
            DependencyProperty.Register(nameof(IsPaused), typeof(bool), typeof(IncenseBurnControl),
                new PropertyMetadata(false));

        /// <summary>暂停静滞：火焰与烟的幅度/速度减半</summary>
        public bool IsPaused
        {
            get { return (bool)GetValue(IsPausedProperty); }
            set { SetValue(IsPausedProperty, value); }
        }

        // ---- 视觉件 ----
        private Rectangle _holderDish;
        private Rectangle _holderStem;
        private Rectangle _stickBody;
        private Rectangle _stickHighlight;
        private Rectangle _bambooFoot;
        private Rectangle _ashCap;
        private Rectangle _charBand;
        private Ellipse _emberGlow;
        private Ellipse _ember;
        private Ellipse _emberCore;
        private readonly List<Rectangle> _ashRings = new List<Rectangle>();
        private Path _smokeMain;
        private Path _smokeHalo;
        private PathGeometry _smokeGeometry;
        private PointCollection _smokePoints;
        private bool _incenseBuilt;

        // ---- 帧循环状态 ----
        private bool _loopActive;
        private double _progressTarget = 1.0;           // 目标燃烧比例
        private double _lastFrameSec = -1;
        private double _lastRenderSec = -1;
        private System.Diagnostics.Stopwatch _loopWatch;
        private double _smoothedProgress = double.NaN;  // 帧间平滑后的燃烧比例
        private double _flareBoost;
        private double _ashHangLen;                     // 当前悬挂香灰长度
        private double _ashTrim;                        // 断落后的灰长削减（随时间缓慢恢复）
        private double _nextAshFallSec = 35;            // 下次香灰断落倒计时
        private readonly Random _random = new Random();

        public IncenseBurnControl()
        {
            Width = DesignWidth;
            Height = DesignHeight;
            IsHitTestVisible = false;
            Loaded += (s, e) =>
            {
                BuildIncense();
                UpdateIncense(_smoothedProgress);
                if (IsBurning)
                {
                    StartLoop();
                }
            };
        }

        // ================================================================
        // 构建
        // ================================================================

        private void BuildIncense()
        {
            if (_incenseBuilt || Children.Count > 0)
            {
                return;
            }
            _incenseBuilt = true;

            //  香座：托碟 + 托柱（黄铜色）
            _holderStem = new Rectangle
            {
                Width = 12,
                Height = 20,
                RadiusX = 3.5,
                RadiusY = 3.5,
                Fill = Shade(Color.FromRgb(0x8A, 0x6E, 0x4B), Color.FromRgb(0x3E, 0x2E, 0x1A)),
                IsHitTestVisible = false
            };
            SetRect(_holderStem, X - 6, BaseY - 24);
            Children.Add(_holderStem);

            _holderDish = new Rectangle
            {
                Width = 78,
                Height = 17,
                RadiusX = 8,
                RadiusY = 8,
                Fill = Shade(Color.FromRgb(0xA8, 0x86, 0x5A), Color.FromRgb(0x46, 0x34, 0x20)),
                Stroke = new SolidColorBrush(Color.FromArgb(0x90, 0x2A, 0x1C, 0x0E)),
                StrokeThickness = 1,
                IsHitTestVisible = false
            };
            SetRect(_holderDish, X - 39, BaseY - 12);
            Children.Add(_holderDish);

            //  底部竹芯：香底露出的浅黄签芯段
            _bambooFoot = new Rectangle
            {
                Width = StickW - 1.4,
                RadiusX = 2.4,
                RadiusY = 2.4,
                Fill = Shade(Color.FromRgb(0xD2, 0xB2, 0x7A), Color.FromRgb(0x8E, 0x70, 0x40)),
                IsHitTestVisible = false
            };
            SetRect(_bambooFoot, X - (StickW - 1.4) / 2, BaseY - 6);
            Children.Add(_bambooFoot);

            //  香体：圆柱受光（左亮右暗）+ 环纹渐变停顿
            var stickFill = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5),
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(0xE0, 0xB4, 0x76), 0.0),
                    new GradientStop(Color.FromRgb(0xB0, 0x82, 0x46), 0.22),
                    new GradientStop(Color.FromRgb(0x7A, 0x54, 0x28), 0.55),
                    new GradientStop(Color.FromRgb(0xA8, 0x7A, 0x40), 0.78),
                    new GradientStop(Color.FromRgb(0x54, 0x38, 0x1C), 1.0)
                }
            };
            stickFill.Freeze();
            _stickBody = new Rectangle
            {
                Width = StickW,
                RadiusX = 3,
                RadiusY = 3,
                Fill = stickFill,
                Stroke = new SolidColorBrush(Color.FromArgb(0x50, 0x30, 0x20, 0x0E)),
                StrokeThickness = 0.5,
                IsHitTestVisible = false
            };
            Children.Add(_stickBody);

            //  左缘高光（圆柱受光高光）
            _stickHighlight = new Rectangle
            {
                Width = 1.4,
                RadiusX = 0.7,
                RadiusY = 0.7,
                Fill = new SolidColorBrush(Color.FromArgb(0x5A, 0xFF, 0xF2, 0xD8)),
                IsHitTestVisible = false
            };
            Children.Add(_stickHighlight);

            //  炭化带：燃点下方近黑→焦褐→透明
            var charFill = new LinearGradientBrush(
                Color.FromArgb(0xF0, 0x14, 0x0A, 0x04),
                Color.FromArgb(0x00, 0x3E, 0x1F, 0x0E),
                new Point(0, 0), new Point(0, 1));
            charFill.Freeze();
            _charBand = new Rectangle
            {
                Width = StickW + 0.8,
                RadiusX = 2.8,
                RadiusY = 2.8,
                Fill = charFill,
                IsHitTestVisible = false
            };
            Children.Add(_charBand);

            //  香灰：5 个分节小段（微旋转差/宽度差），挂在燃点上方
            for (int i = 0; i < 5; i++)
            {
                var seg = new Rectangle
                {
                    Width = 7.4 + (i % 3) * 0.5,
                    Height = 8.6,
                    RadiusX = 2.2,
                    RadiusY = 2.2,
                    Fill = Shade(Color.FromRgb(0xAA, 0xA6, 0xA0), Color.FromRgb(0x62, 0x5E, 0x58)),
                    Stroke = new SolidColorBrush(Color.FromArgb(0x70, 0x40, 0x3C, 0x38)),
                    StrokeThickness = 0.5,
                    IsHitTestVisible = false,
                    RenderTransform = new RotateTransform((i % 2 == 0 ? 1 : -1) * (1.5 + i * 0.9))
                };
                _ashRings.Add(seg);
                Children.Add(seg);
            }

            //  燃点三件套：外围光晕 + 红光 + 白热内核（不冻结，随阶段换色）
            _emberGlow = new Ellipse
            {
                Width = 30,
                Height = 30,
                IsHitTestVisible = false
            };
            _emberGlow.Fill = Radial(Colors.White, Color.FromRgb(0xFF, 0x7A, 0x1E), 0x70, 0x00, freeze: false);
            Children.Add(_emberGlow);

            _ember = new Ellipse
            {
                Width = 12,
                Height = 12,
                IsHitTestVisible = false
            };
            _ember.Fill = Radial(Color.FromRgb(0xFF, 0xD9, 0x8A), Color.FromRgb(0xE2, 0x3B, 0x0E), 0xF0, 0x20, freeze: false);
            Children.Add(_ember);

            _emberCore = new Ellipse
            {
                Width = 5,
                Height = 5,
                IsHitTestVisible = false
            };
            _emberCore.Fill = Radial(Colors.White, Color.FromRgb(0xFF, 0xE9, 0xB0), 0xFF, 0x40);
            Children.Add(_emberCore);

            //  青烟细丝：主丝 1.8px + 扩散晕 5px（共享点集）
            const int smokePts = 14;
            _smokePoints = new PointCollection(smokePts);
            for (int i = 0; i < smokePts; i++)
            {
                _smokePoints.Add(new Point(X, BaseY - MaxLen));
            }
            _smokeGeometry = new PathGeometry();
            var smokeFigure = new PathFigure
            {
                StartPoint = new Point(X, BaseY - MaxLen),
                IsFilled = false,
                IsClosed = false
            };
            smokeFigure.Segments.Add(new System.Windows.Media.PolyLineSegment(_smokePoints, true));
            _smokeGeometry.Figures.Add(smokeFigure);

            var mainStroke = new LinearGradientBrush(
                Color.FromArgb(0xC0, 0xC9, 0xCE, 0xD4),
                Color.FromArgb(0x00, 0xC9, 0xCE, 0xD4),
                new Point(0.5, 1), new Point(0.5, 0));
            mainStroke.Freeze();
            _smokeMain = new Path
            {
                Data = _smokeGeometry,
                Stroke = mainStroke,
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };
            Children.Add(_smokeMain);

            var haloStroke = new LinearGradientBrush(
                Color.FromArgb(0x30, 0xC9, 0xCE, 0xD4),
                Color.FromArgb(0x00, 0xC9, 0xCE, 0xD4),
                new Point(0.5, 1), new Point(0.5, 0));
            haloStroke.Freeze();
            _smokeHalo = new Path
            {
                Data = _smokeGeometry,
                Stroke = haloStroke,
                StrokeThickness = 5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };
            Children.Add(_smokeHalo);
            Canvas.SetZIndex(_smokeHalo, 1);
            Canvas.SetZIndex(_smokeMain, 2);   // 主烟丝压在扩散晕之上

            _ashHangLen = 0;
            _ashTrim = 0;
            _nextAshFallSec = 35;
            LayoutIncense(_smoothedProgress);
        }

        private static void SetRect(FrameworkElement el, double x, double y)
        {
            SetLeft(el, x);
            SetTop(el, y);
        }

        /// <summary>圆柱受光横向渐变</summary>
        private static LinearGradientBrush Shade(Color light, Color dark)
        {
            var brush = new LinearGradientBrush(light, dark, new Point(0, 0.5), new Point(1, 0.5));
            brush.Freeze();
            return brush;
        }

        private static RadialGradientBrush Radial(Color inner, Color outer, byte alphaIn, byte alphaOut, bool freeze = true)
        {
            var brush = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5),
                GradientOrigin = new Point(0.5, 0.5)
            };
            brush.GradientStops.Add(new GradientStop(
                Color.FromArgb((byte)(alphaIn * inner.A / 255), inner.R, inner.G, inner.B), 0));
            brush.GradientStops.Add(new GradientStop(
                Color.FromArgb((byte)(alphaOut * outer.A / 255), outer.R, outer.G, outer.B), 1));
            if (freeze)
            {
                brush.Freeze();
            }
            return brush;
        }

        // ================================================================
        // 布局与燃烧
        // ================================================================

        private double IncenseTop(double progress)
        {
            return BaseY - MaxLen * Math.Max(0, Math.Min(1, progress));
        }

        /// <summary>
        /// 按燃烧比例布置香体/炭化带/香灰/燃点/竹芯。progress: 1=满香 0=燃尽。
        /// </summary>
        private void LayoutIncense(double progress)
        {
            //  视觉件在自己的 Loaded（BuildIncense）之前为 null：
            //  WPF 的 Loaded 自上而下广播，宿主常在子控件 Loaded 前就触发本方法
            if (_stickBody == null)
            {
                return;
            }
            if (double.IsNaN(progress))
            {
                progress = 1;
            }
            progress = Math.Max(0, Math.Min(1, progress));
            double top = IncenseTop(progress);                    // 燃点 Y
            double remaining = BaseY - top;                        // 未燃长度

            //  香体与高光
            _stickBody.Height = remaining;
            SetRect(_stickBody, X - StickW / 2, top);
            _stickHighlight.Height = Math.Max(0, remaining - 4);
            SetRect(_stickHighlight, X - StickW / 2 + 1.1, top + 2);

            //  竹芯：露出满长的最后 4%（接近燃尽时才见到）
            double footH = progress < 0.06 ? 8 * (0.06 - progress) / 0.06 : 0;
            _bambooFoot.Height = footH;
            SetRect(_bambooFoot, X - (StickW - 1.4) / 2, BaseY - footH);

            //  炭化带：贴燃点上方
            _charBand.Height = Math.Min(15, 9 + 6 * (1 - progress));
            SetRect(_charBand, X - (StickW + 0.8) / 2, top - 4);

            //  香灰段悬挂：从燃点向上堆叠；灰总长随已燃比例增长（上限 46），扣除断落削减
            _ashHangLen = Math.Max(0, Math.Min(46, (1 - progress) * 60) - _ashTrim);
            double cursor = top - 2;
            for (int i = 0; i < _ashRings.Count; i++)
            {
                var seg = _ashRings[i];
                double segLen = _ashHangLen / _ashRings.Count;
                if (cursor - segLen < top - _ashHangLen || cursor <= top)
                {
                    seg.Visibility = Visibility.Collapsed;
                    continue;
                }
                seg.Visibility = Visibility.Visible;
                seg.Height = segLen;
                SetRect(seg, X - seg.Width / 2 + (i % 2 == 0 ? -0.6 : 0.6), cursor - segLen);
                cursor -= segLen;
            }

            //  燃点三件套
            SetRect(_emberGlow, X - 15, top - 15);
            SetRect(_ember, X - 6, top - 6.5);
            SetRect(_emberCore, X - 2.5, top - 5);

            //  青烟几何原点跟随燃点（点集由帧循环写入）
            if (_smokePoints != null)
            {
                for (int i = 0; i < _smokePoints.Count; i++)
                {
                    _smokePoints[i] = new Point(X, top - 4 - i * 2);
                }
            }
        }

        private void UpdateIncense(double progress)
        {
            if (_stickBody == null)
            {
                return;
            }
            LayoutIncense(progress);
        }

        // ================================================================
        // 帧循环（30fps 门限 + 预算门控）
        // ================================================================

        private void StartLoop()
        {
            //  IsLoaded 在控件挂入已加载树后即为 true，早于自身 Loaded（BuildIncense），
            //  不能作为视觉件就绪依据
            if (_loopActive || !_incenseBuilt)
            {
                return;
            }
            if (AnimationBudget.ReduceMotion)
            {
                UpdateIncense(_smoothedProgress);
                return;
            }
            _loopActive = true;
            _lastFrameSec = -1;
            _lastRenderSec = -1;
            _nextAshFallSec = 25 + _random.NextDouble() * 20;
            _loopWatch = System.Diagnostics.Stopwatch.StartNew();
            SetSmokeVisible(true);
            CompositionTarget.Rendering += OnFrameRendering;
        }

        private void StopLoop()
        {
            if (!_loopActive)
            {
                return;
            }
            _loopActive = false;
            CompositionTarget.Rendering -= OnFrameRendering;
            if (_loopWatch != null)
            {
                _loopWatch.Stop();
                _loopWatch = null;
            }
        }

        private void SetSmokeVisible(bool visible)
        {
            if (_smokeMain == null)
            {
                return;
            }
            var v = visible ? Visibility.Visible : Visibility.Collapsed;
            _smokeMain.Visibility = v;
            _smokeHalo.Visibility = v;
        }

        private void OnFrameRendering(object sender, EventArgs e)
        {
            if (!_loopActive || _loopWatch == null || !_incenseBuilt)
            {
                return;
            }
            //  窗口后台（最小化/完全遮挡）时跳过全部粒子帧工作
            if (AnimationBudget.IsBackground)
            {
                return;
            }

            double now = _loopWatch.Elapsed.TotalSeconds;
            if (_lastRenderSec > 0 && now - _lastRenderSec < 0.031)
            {
                return;   // 30fps 门限
            }
            double dt = _lastFrameSec >= 0 ? Math.Min(0.1, now - _lastFrameSec) : 0.03;
            _lastFrameSec = now;
            _lastRenderSec = now;

            double lull = IsPaused ? 0.45 : 1.0;

            //  香长平滑收缩：指数逼近目标进度
            if (double.IsNaN(_smoothedProgress))
            {
                _smoothedProgress = _progressTarget;
            }
            else
            {
                _smoothedProgress += (_progressTarget - _smoothedProgress) * Math.Min(1, dt * 3);
            }
            bool layoutDirty = Math.Abs(_progressTarget - _smoothedProgress) > 0.0004;

            //  断落削减缓慢长回（香灰重新积攒）
            if (_ashTrim > 0)
            {
                _ashTrim = Math.Max(0, _ashTrim - dt * 0.7);
                layoutDirty = true;
            }

            //  香灰间歇断落：随机计时到点且灰够长时，顶端一节按重力坠落
            if (!IsPaused)
            {
                _nextAshFallSec -= dt;
                if (_nextAshFallSec <= 0)
                {
                    _nextAshFallSec = 30 + _random.NextDouble() * 30;
                    if (_ashHangLen > 16)
                    {
                        DropAshSegment();
                        layoutDirty = true;
                    }
                }
            }

            if (layoutDirty)
            {
                LayoutIncense(_smoothedProgress);
            }

            //  燃点连续呼吸：双频叠加似随机明暗 + 随机爆闪（均值 8s 一次）
            bool flare = _random.NextDouble() < dt / 8.0;
            if (flare)
            {
                _flareBoost = 0.8;
            }
            _flareBoost *= Math.Exp(-3.0 * dt);
            double bright = 0.78 + 0.14 * Math.Sin(now * 5.1) + 0.08 * Math.Sin(now * 13.7 + 1.3)
                + 0.5 * _flareBoost;
            _ember.Opacity = Math.Min(1, 0.55 + 0.5 * bright);
            _emberCore.Opacity = Math.Min(1, 0.55 + 0.5 * bright);
            _emberGlow.Opacity = Math.Min(1, 0.30 + 0.7 * bright);

            //  青烟细丝逐帧重写：燃点上方前段近直（层流），之后振幅随高度^1.5 增长；
            //  风横漂（慢正弦，周期约 18s）+ 湍流扰动（双频行波）
            double tipTop = IncenseTop(_smoothedProgress);
            for (int i = 0; i < _smokePoints.Count; i++)
            {
                double f = i / (double)(_smokePoints.Count - 1);
                double rise = f * 150;
                double amp = Math.Pow(f, 1.5) * 20 * lull * (1 + 0.8 * _flareBoost);
                double wind = Math.Sin(now * 0.35 + 0.8) * 6 * lull;
                double wave = Math.Sin(f * 5.2 - now * 2.0 + 1.7)
                    + 0.45 * Math.Sin(f * 8.6 - now * 3.3 + 0.6);
                double x = X + wind + wave * amp;
                double y = tipTop - 8 - rise;
                _smokePoints[i] = new Point(x, y);
            }
        }

        /// <summary>顶端一节香灰脱落：克隆体重力下坠旋转消散，灰长削减随后缓慢恢复</summary>
        private void DropAshSegment()
        {
            Rectangle source = null;
            foreach (var seg in _ashRings)
            {
                if (seg.Visibility == Visibility.Visible)
                {
                    source = seg;
                    break;
                }
            }
            if (source == null)
            {
                return;
            }

            _ashTrim = Math.Min(_ashHangLen + 2, _ashTrim + _ashHangLen / _ashRings.Count + 2);

            var fall = new Rectangle
            {
                Width = source.Width,
                Height = source.Height,
                RadiusX = 2.2,
                RadiusY = 2.2,
                Fill = source.Fill,
                Stroke = source.Stroke,
                StrokeThickness = 0.5,
                IsHitTestVisible = false
            };
            SetRect(fall, GetLeft(source), GetTop(source));
            Children.Add(fall);

            var translate = new TranslateTransform();
            var rotate = new RotateTransform(0);
            var tf = new TransformGroup();
            tf.Children.Add(translate);
            tf.Children.Add(rotate);
            fall.RenderTransform = tf;
            var moveY = new DoubleAnimation(0, 130, TimeSpan.FromMilliseconds(900))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            var moveX = new DoubleAnimation((_random.NextDouble() - 0.5) * 26, TimeSpan.FromMilliseconds(900));
            var spin = new DoubleAnimation((_random.NextDouble() - 0.5) * 160, TimeSpan.FromMilliseconds(900));
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(450))
            {
                BeginTime = TimeSpan.FromMilliseconds(450)
            };
            fade.Completed += (s, e2) => Children.Remove(fall);
            translate.BeginAnimation(TranslateTransform.XProperty, moveX);
            translate.BeginAnimation(TranslateTransform.YProperty, moveY);
            rotate.BeginAnimation(RotateTransform.AngleProperty, spin);
            fall.BeginAnimation(OpacityProperty, fade);
        }

        // ================================================================
        // 对外动作
        // ================================================================

        /// <summary>
        /// 阶段色：重涂燃点红光/光晕（工作=主题色、休息=绿由宿主传入）
        /// </summary>
        public void ApplyPhaseColor(Color c)
        {
            //  宿主可能在服务线程池事件里调用：视觉树只认 UI 线程，切回后执行
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ApplyPhaseColor(c)));
                return;
            }

            if (_ember != null && _ember.Fill is RadialGradientBrush ember && !ember.IsFrozen)
            {
                ember.GradientStops[1].Color = Color.FromArgb(0xF0, c.R, c.G, c.B);
            }
            if (_emberGlow != null && _emberGlow.Fill is RadialGradientBrush glow && !glow.IsFrozen)
            {
                glow.GradientStops[1].Color = Color.FromArgb(0x70, c.R, c.G, c.B);
            }
        }

        /// <summary>
        /// 点香动画：火苗从 0 绽放 + 白热爆闪 + 烟涌（约 0.8s）
        /// </summary>
        public void PlayIgnition()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(PlayIgnition));
                return;
            }

            if (AnimationBudget.ReduceMotion || !_incenseBuilt)
            {
                return;
            }

            _flareBoost = 1.0;
            double top = IncenseTop(_smoothedProgress);

            var burstScale = new ScaleTransform(0, 0);
            _ember.RenderTransform = burstScale;
            _ember.RenderTransformOrigin = new Point(0.5, 0.5);
            _emberGlow.RenderTransform = burstScale;
            _emberCore.RenderTransform = burstScale;
            var spring = new SpringEase { Bounce = 0.45, EasingMode = EasingMode.EaseOut };
            var grow = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = spring };
            burstScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            burstScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);

            var flash = new Ellipse
            {
                Width = 34,
                Height = 34,
                Fill = Radial(Colors.White, Color.FromRgb(0xFF, 0xC4, 0x66), 0xE6, 0x00),
                IsHitTestVisible = false
            };
            SetRect(flash, X - 17, top - 17);
            Children.Add(flash);
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(500))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            fade.Completed += (s, e2) => Children.Remove(flash);
            flash.BeginAnimation(OpacityProperty, fade);
        }

        /// <summary>
        /// 燃尽动画：火星爆闪 + 灰烬散落
        /// </summary>
        public void PlayBurnout()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(PlayBurnout));
                return;
            }

            if (AnimationBudget.ReduceMotion || !_incenseBuilt)
            {
                return;
            }

            var flare = new Ellipse
            {
                Width = 26,
                Height = 26,
                Fill = Radial(Colors.White, Color.FromRgb(0xFF, 0x9A, 0x3A), 0xE0, 0x00),
                IsHitTestVisible = false
            };
            SetRect(flare, X - 13, BaseY - MaxLen - 13);
            Children.Add(flare);
            var flareFade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(420));
            flareFade.Completed += (s, e2) => Children.Remove(flare);
            flare.BeginAnimation(OpacityProperty, flareFade);

            //  灰烬散落：3 颗灰粒重力下落 + 旋转
            for (int i = 0; i < 3; i++)
            {
                var ash = new Rectangle
                {
                    Width = 4 - i * 0.8,
                    Height = 4 - i * 0.8,
                    Fill = new SolidColorBrush(Color.FromRgb(0x8A, 0x86, 0x80)),
                    IsHitTestVisible = false
                };
                double sx = X + (_random.NextDouble() - 0.5) * 10;
                double sy = BaseY - MaxLen;
                SetRect(ash, sx, sy);
                Children.Add(ash);

                var translate = new TranslateTransform();
                ash.RenderTransform = translate;
                double vx = (_random.NextDouble() - 0.5) * 70;
                var fallX = new DoubleAnimation(vx * 0.8, TimeSpan.FromMilliseconds(700))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                var fallY = new DoubleAnimation(0, 90 + _random.NextDouble() * 40, TimeSpan.FromMilliseconds(700))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                };
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(700));
                fade.Completed += (s, e2) => Children.Remove(ash);
                translate.BeginAnimation(TranslateTransform.XProperty, fallX);
                translate.BeginAnimation(TranslateTransform.YProperty, fallY);
                ash.BeginAnimation(OpacityProperty, fade);
            }
        }
    }
}
