using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace UI.Controls.Window
{
    /// <summary>
    /// 套圈水机（长六边形立体屏样式）时长设置弹窗。
    ///
    /// 水流模型（环量叠加，遵循流体动力学公式）：
    /// - 速度场取 Lamb-Oseen 涡的切向分布 v_θ(r)=Γ/(2πr)·(1−e^{−r²/rc²})，
    ///   涡心位于水箱中心，环流覆盖整个屏幕（不分区）；
    /// - 左泵钮注入顺时针环量 Γ_cw，右泵钮注入逆时针环量 Γ_ccw：
    ///   多按一次就向对应方向再叠加一档环量（上限 Γ_max）；
    /// - 注入过程按一阶时间常数 τ_in 平滑逼近（按压的机械过程），
    ///   环量按粘性扩散规律 Γ(t)=Γ0·e^{−t/τ_ν} 衰减；
    /// - 两股反向环流线性叠加（无粘叠加原理），净环量 Γ=Γ_cw−Γ_ccw，
    ///   同时/相近时间按下两钮时两股环量并存、依物理相互抵消，只剩净差；
    /// - 两股环量并存时速度剪切大，按剪切湍流强度
    ///   ε ∝ 2Γ_cwΓ_ccw/(Γ_cw+Γ_ccw) 对圈与气泡施加随机扰动；
    /// - 水流的表现只有气泡：气泡生成率与透明度跟随环流活动度
    ///   A=(Γ_cw+Γ_ccw)/Γ_ref，产生与消退随环量注入/衰减自然持续。
    ///
    /// 圈动力学：一阶阻力追随局部流场 a=(v_flow−v)/τ + 有效重力 + 扰动。
    /// 圈为轻质塑料（低有效重力、小惯性），姿态跟随局部流速与涡度。
    /// 固定模式：目标圈被柱区上涌流卷携，三频随机噪声轨迹 + 回落引导 +
    /// 超时直升级三重保证套中。随机模式：柱区上涌 + 近针弱吸引，套中率适中。
    ///
    /// 左柱每圈 = 1 小时（工作）/30 分钟（休息），右柱每圈 = 10 分钟（工作）/5 分钟（休息），
    /// 每柱最多 6 圈；确认回传时长。键盘：←/→ 泵动、R 重置、Enter 确认、Esc 关闭。
    /// </summary>
    public partial class RingTossWindow : System.Windows.Window
    {
        // ---------------- 画面常量 ----------------
        private const double BoardW = 472;
        private const double BoardH = 400;
        private const double FloorY = 380;
        private const double FloatTop = 318;
        private const double FloatBottom = 376;
        private const double TipY = 112;
        private const double NeedleBaseY = 148;
        private const double ShaftBottomY = 370;
        private const double PlinthTopY = 360;
        private const double StackY0 = 156;
        private const double StackStep = 11;
        private const double RingR = 16;
        private const int MaxRingsPerColumn = 6;

        private static readonly double[] ColumnX = { 118, 354 };

        // ---------------- 环流物理常量 ----------------
        private const double VortexCx = BoardW * 0.5;   // 涡心（水箱中心）
        private const double VortexCy = 226;
        private const double CoreR = 96;                // Lamb-Oseen 涡核半径 rc
        private const double SwirlScale = 36000;        // Γ/(2π)·κ 的归一化吸收系数
        private const double GammaMax = 2.2;            // 单方向环量上限
        private const double GammaPerPump = 0.8;        // 每次泵动注入的环量档
        private const double InjectTau = 0.45;          // 注入时间常数（按压过程）
        private const double ViscTau = 10;             // 粘性衰减时间常数 τ_ν
        private const double FlowClampX = 320;          // 合成流场限幅（稳定性）
        private const double FlowClampY = 400;

        // ---------------- 圈物理常量（轻质塑料圈） ----------------
        private const double GWater = 150;              // 水中净重力（减轻）
        private const double GFree = 190;               // 脱离水流后的下沉加速度
        private const double MaxRise = 400;
        private const double ThreadBand = 15;           // 针尖捕捉半径（提高随机模式命中）
        private const double WeakGuideBand = 44;        // 随机模式近针弱吸引范围

        // 圈状态机
        private const int StateFloat = 0;
        private const int StateCarried = 1;
        private const int StateBallistic = 2;
        private const int StateThreaded = 3;
        private const int StateStacked = 4;

        private sealed class Ring
        {
            public Grid Element;
            public ScaleTransform Squash;
            public RotateTransform Tilt;
            public Ellipse Shadow;
            public int State;
            public double X, Y;
            public double Vx, Vy;
            public double PrevY;
            public double BaseY;
            public double BobFreq, BobPhase;
            public double P1, P2, P3;
            public double TurbAmp;
            public double AngVel;
            public double JetStart;
            public bool Guided;
            public int Column;
            public int StackIndex;
            public int Bounces;
            /// <summary>低速/卡角持续时长，用于确定性脱困</summary>
            public double Idle;
        }

        private sealed class Bubble
        {
            public Ellipse Element;
            public double X, Y;
            public double Speed;
            public double SwayPhase;
            public double SwayAmp;
            public double Birth;
            public double StartAt;
            public double Life;
        }

        private sealed class Ripple
        {
            public Ellipse Element;
            public double CenterX, CenterY;
            public double Birth;
        }

        private sealed class Caustic
        {
            public Ellipse Element;
            public double BaseX;
            public double Phase;
        }

        private sealed class LightRay
        {
            public Polygon Element;
            public RotateTransform Rot;
            public double BaseAngle;
            public double Phase;
        }

        private readonly bool _isWork;
        private readonly int _leftUnitMinutes;
        private readonly int _rightUnitMinutes;
        private readonly Action<bool, int> _applyResult;

        private readonly List<Ring> _rings = new List<Ring>();
        private readonly List<Bubble> _bubbles = new List<Bubble>();
        private readonly List<Ripple> _ripples = new List<Ripple>();
        private readonly List<Caustic> _caustics = new List<Caustic>();
        private readonly List<LightRay> _rays = new List<LightRay>();

        private Canvas _shadowCanvas;
        private Canvas _fxCanvas;

        private bool _frameHooked;
        private readonly Random _random = new Random();
        private readonly Stopwatch _clock = new Stopwatch();
        private double _lastSec;

        // 双环量状态（顺时针/逆时针各自独立，注入-衰减）
        private double _gammaCW;
        private double _gammaCCW;
        private double _gammaCWTarget;
        private double _gammaCCWTarget;

        private double _nextBubbleAt;
        private double _unitRestoreAt;
        private bool _ledFlashOn;
        private bool _ledLastBright;
        private bool _fixedMode = true;

        // 重置搅动增强：重置后短时间内增强竖直收拢、压低下沉二次流，
        // 把底部角落的圈吸离死水区（底部角落是环流的"下沉区"，切向+下沉双向下压）
        private double _stirBoost;

        // ---------------- 性能：缓存冻结画刷 ----------------
        private static readonly SolidColorBrush BubbleBrush;
        private static readonly SolidColorBrush RippleStroke;
        private static readonly SolidColorBrush ShadowFill;
        private static readonly SolidColorBrush LedNormal;
        private static readonly SolidColorBrush LedFlash;

        static RingTossWindow()
        {
            BubbleBrush = new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF));
            BubbleBrush.Freeze();
            RippleStroke = new SolidColorBrush(Color.FromArgb(0xA8, 0xFF, 0xFF, 0xFF));
            RippleStroke.Freeze();
            ShadowFill = new SolidColorBrush(Color.FromArgb(0x2A, 0x00, 0x18, 0x30));
            ShadowFill.Freeze();
            LedNormal = new SolidColorBrush(Color.FromRgb(0x8C, 0xF7, 0xDE));
            LedNormal.Freeze();
            LedFlash = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            LedFlash.Freeze();
        }

        private static readonly Color[] RingColors =
        {
            Color.FromRgb(0xE6, 0x4C, 0x3C),
            Color.FromRgb(0xF2, 0x91, 0x3D),
            Color.FromRgb(0xF2, 0xCE, 0x3F),
            Color.FromRgb(0x4F, 0xB8, 0x6A),
            Color.FromRgb(0xE8, 0xE8, 0xF2),
            Color.FromRgb(0x9B, 0x6F, 0xD8)
        };

        public RingTossWindow(bool isWork, int initialMinutes, Action<bool, int> applyResult)
        {
            InitializeComponent();
            _isWork = isWork;
            _applyResult = applyResult;
            _leftUnitMinutes = isWork ? 60 : 30;
            _rightUnitMinutes = isWork ? 10 : 5;

            UpdateUnitText();

            SetupJetButton(LeftJetButton, 0);
            SetupJetButton(RightJetButton, 1);

            ShellRoot.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    try { DragMove(); } catch (InvalidOperationException) { }
                }
            };

            PreviewKeyDown += Window_PreviewKeyDown;

            Loaded += (s, e) =>
            {
                if (_frameHooked)
                {
                    return;
                }
                _frameHooked = true;

                //  字体缩放：机身全部是固定坐标 Path，直接放大窗口会留出大片死区
                //  （150% 时整机缩在左上角）——用 LayoutTransform 让机身连同字号整体等比缩放
                double scale = UI.Base.UIFontScale.CurrentScale;
                if (Math.Abs(scale - 1.0) > 0.001)
                {
                    SizeToContent = SizeToContent.WidthAndHeight;
                    ShellRoot.LayoutTransform = new System.Windows.Media.ScaleTransform(scale, scale);
                }

                UI.Base.UIFontScale.ApplyToWindow(this);
                ApplyCacheDpiScale();
                BuildBoard();
                SeedInitialRings(initialMinutes);
                UpdateTotal();
                Focus();
                _clock.Start();
                _lastSec = 0;
                CompositionTarget.Rendering += FrameTimer_Tick;
            };
            Closed += (s, e) =>
            {
                _frameHooked = false;
                CompositionTarget.Rendering -= FrameTimer_Tick;
            };
        }

        /// <summary>
        /// 静态层 BitmapCache 默认 RenderAtScale=1，高 DPI 屏（125%/150%）上会被拉伸发虚；
        /// 按当前窗口实际 DPI 设置缓存的渲染倍率
        /// </summary>
        private void ApplyCacheDpiScale()
        {
            try
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                ApplyCacheDpiScale(ShellRoot, (float)dpi.DpiScaleX);
            }
            catch
            {
                //  DPI 取不到时保持默认 1x
            }
        }

        private static void ApplyCacheDpiScale(System.Windows.DependencyObject root, float scale)
        {
            if (root is System.Windows.UIElement element && element.CacheMode is System.Windows.Media.BitmapCache cache)
            {
                cache.RenderAtScale = scale;
            }

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                ApplyCacheDpiScale(VisualTreeHelper.GetChild(root, i), scale);
            }
        }

        // ---------------------------------------------------------------
        // 环流流场（Lamb-Oseen 涡 + 双环量叠加 + 剪切湍流）
        // ---------------------------------------------------------------

        /// <summary>Lamb-Oseen 涡切向速度 v_θ(r)=Γ/(2πr)·(1−e^{−r²/rc²})。</summary>
        private static double TangentialSpeed(double gammaNet, double r)
        {
            if (gammaNet == 0)
            {
                return 0;
            }
            if (r < 1e-3)
            {
                r = 1e-3;
            }
            double core = 1 - Math.Exp(-(r * r) / (CoreR * CoreR));
            return gammaNet * SwirlScale * core / r;
        }

        /// <summary>剪切湍流强度 ε ∝ 2Γ_cw·Γ_ccw/(Γ_cw+Γ_ccw)（反向环量并存时最大）。</summary>
        private double ShearTurbulence()
        {
            double sum = _gammaCW + _gammaCCW;
            if (sum < 1e-4)
            {
                return 0;
            }
            return 2 * _gammaCW * _gammaCCW / sum;
        }

        /// <summary>环流活动度（气泡的产生与消退驱动）。</summary>
        private double Activity()
        {
            //  这里用两股环量之和、不是净值：左右泵反向注入时净环量会抵消，
            //  但水里确实被搅动了，圈应该照样被带起来（旧写法会出现"怎么泵都不动"）
            return Math.Min(1, (_gammaCW + _gammaCCW) / 1.2);
        }

        private void FlowAt(double x, double y, out double fx, out double fy)
        {
            double gammaNet = _gammaCW - _gammaCCW;   // 反向环量叠加：净环量即抵消

            //  环境底流：净环量接近零时保留一丝微环流（0.05 档），
            //  圈永远不会完全静止在死水里，随时可以被下一泵卷起
            if (Math.Abs(gammaNet) < 0.10)
            {
                gammaNet = 0.10 * Math.Sign(gammaNet == 0 ? 1 : gammaNet);
            }

            fx = 0;
            fy = 0;
            {
                double dx = x - VortexCx;
                double dy = y - VortexCy;
                double r2 = dx * dx + dy * dy;
                double r = Math.Sqrt(r2) + 1e-3;
                double vt = TangentialSpeed(gammaNet, r);
                // 屏幕坐标（y 向下）：Γ>0（顺时针）切向 = (−dy,dx)/r；Γ<0（逆时针）反向
                fx = vt * (-dy) / r;
                fy = vt * (dx) / r;

                // 涡旋径向收拢（真实涡旋：中心低压把漂浮物吸向涡心）：
                // 角落/远缘的圈被拉进环流路径，不再滞留角落飞不起来；
                // 收拢强度随净环量增强，离涡心越远越强（角落在最远，吸力最大）；
                // 重置搅动期间竖直收拢临时增强（把底部死水区的圈吸离底角）
                if (r > 60)
                {
                    double pull = Math.Min(150, Math.Abs(gammaNet) * 95 * (r - 60) / 220.0);
                    double vertPull = 0.65 * (1 + 2.0 * _stirBoost);
                    fx += (VortexCx - x) / r * pull * 0.65;
                    fy += (VortexCy - y) / r * pull * vertPull;
                }

                // 二次流（茶杯效应）：中心/柱区上涌、边壁下沉；
                // 重置搅动期间暂时压低下沉分量，避免底角圈被二次流压死
                double depthMask = Math.Min(1, Math.Max(0, (y - 130) / 200));
                if (depthMask > 0)
                {
                    double sink = 1 - 0.6 * _stirBoost;
                    double par = (x - VortexCx) / (BoardW * 0.46);
                    par *= par;
                    fy += Math.Abs(gammaNet) * 55 * (par - 0.40) * depthMask * sink;
                    double g0 = (x - ColumnX[0]) / 26.0;
                    double g1 = (x - ColumnX[1]) / 26.0;
                    double bump = Math.Exp(-g0 * g0) + Math.Exp(-g1 * g1);
                    fy -= Math.Abs(gammaNet) * 150 * bump * depthMask;
                }
            }

            //  壁面粘滞（no-slip）：贴壁处水平流速趋近于零。缺这一条时环流会把圈
            //  一路顶到侧壁、沿壁堆成一列，看起来像贴在玻璃上而不是漂在水里
            double wallFade = Math.Min((x - 18) / 54.0, (BoardW - 18 - x) / 54.0);
            fx *= Math.Max(0.10, Math.Min(1, wallFade));

            //  底部常驻上涌：真实水机泵动时是整箱水被顶起来，不是只有柱口附近上涌。
            //  这条与"按哪边泵"无关、强度随总环量与搅动增强，保证底角永远不是死水区
            double depth = (FloatBottom + 4 - y) / (FloatBottom + 4 - TipY);
            if (depth > 0)
            {
                double up = 30 + 120 * Math.Min(1, Activity() + 0.7 * _stirBoost);
                fy -= up * Math.Min(1, depth) * Math.Min(1, depth);
            }

            // 限幅：数值稳定性（避免中心区速度发散）
            fx = Math.Max(-FlowClampX, Math.Min(FlowClampX, fx));
            fy = Math.Max(-FlowClampY, Math.Min(FlowClampY, fy));
        }

        // ---------------------------------------------------------------
        // 搭建画面
        // ---------------------------------------------------------------

        private void BuildBoard()
        {
            BoardCanvas.Children.Clear();
            _rings.Clear();
            _bubbles.Clear();
            _ripples.Clear();
            _caustics.Clear();
            _rays.Clear();

            BuildCaustics();
            BuildLightRays();

            _shadowCanvas = new Canvas { Width = BoardW, Height = BoardH };
            BoardCanvas.Children.Add(_shadowCanvas);

            BuildColumn(0, _isWork ? "1小时" : "30分");
            BuildColumn(1, _isWork ? "10分" : "5分");

            _fxCanvas = new Canvas { Width = BoardW, Height = BoardH };
            BoardCanvas.Children.Add(_fxCanvas);

            // 12 个彩圈随机分布在底部
            for (int i = 0; i < 12; i++)
            {
                CreateRing(RingColors[i % RingColors.Length]);
            }
        }

        private void BuildCaustics()
        {
            var brush = new RadialGradientBrush
            {
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF), 0),
                    new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1)
                }
            };
            brush.Freeze();
            double[] xs = { 40, 180, 300, 420 };
            double[] ws = { 108, 92, 92, 108 };
            for (int i = 0; i < 4; i++)
            {
                var el = new Ellipse
                {
                    Width = ws[i],
                    Height = ws[i] * 0.32,
                    Fill = brush,
                    IsHitTestVisible = false
                };
                Canvas.SetTop(el, 8 + (i % 2) * 8);
                BoardCanvas.Children.Add(el);
                _caustics.Add(new Caustic { Element = el, BaseX = xs[i], Phase = i * 1.83 });
            }
        }

        private void BuildLightRays()
        {
            double[] xs = { 110, 300 };
            double[] dirs = { 1, -1 };
            for (int i = 0; i < 2; i++)
            {
                double x = xs[i];
                double d = dirs[i];
                var fill = new LinearGradientBrush(
                    Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF),
                    Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF),
                    new Point(0, 0), new Point(0, 1));
                fill.Freeze();
                var poly = new Polygon
                {
                    Fill = fill,
                    IsHitTestVisible = false,
                    Points = new PointCollection
                    {
                        new Point(x - 12, 0),
                        new Point(x + 7, 0),
                        new Point(x + d * 76, BoardH),
                        new Point(x - d * 24, BoardH)
                    }
                };
                var rot = new RotateTransform(0, x, 0);
                poly.RenderTransform = rot;
                BoardCanvas.Children.Add(poly);
                _rays.Add(new LightRay { Element = poly, Rot = rot, BaseAngle = d * 6, Phase = i * 2.1 });
            }
        }

        private void BuildColumn(int column, string unitLabel)
        {
            double x = ColumnX[column];

            var shadow = new Ellipse
            {
                Width = 54,
                Height = 11,
                Fill = ShadowFill,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(shadow, x - 27);
            Canvas.SetTop(shadow, 376);
            _shadowCanvas.Children.Add(shadow);

            // 柱身（烤漆黄金属）
            var shaftFill = new LinearGradientBrush(
                Color.FromRgb(0xFF, 0xEE, 0x9A), Color.FromRgb(0xE3, 0xAE, 0x18),
                new Point(0, 0), new Point(1, 0));
            shaftFill.Freeze();
            var shaft = new Rectangle
            {
                Width = 7,
                Height = ShaftBottomY - NeedleBaseY,
                RadiusX = 2.5,
                RadiusY = 2.5,
                Fill = shaftFill,
                Stroke = new SolidColorBrush(Color.FromArgb(0x78, 0x8A, 0x66, 0x10)),
                StrokeThickness = 0.7,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(shaft, x - 3.5);
            Canvas.SetTop(shaft, NeedleBaseY);
            BoardCanvas.Children.Add(shaft);

            //  柱身高光条：让塑料柱有个受光的圆柱面，而不是一条平涂的色带
            var shaftGloss = new Rectangle
            {
                Width = 2,
                Height = ShaftBottomY - NeedleBaseY - 8,
                RadiusX = 1,
                RadiusY = 1,
                Fill = new SolidColorBrush(Color.FromArgb(0x6E, 0xFF, 0xFF, 0xFF)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(shaftGloss, x - 2.1);
            Canvas.SetTop(shaftGloss, NeedleBaseY + 4);
            BoardCanvas.Children.Add(shaftGloss);

            var spec = new Rectangle
            {
                Width = 1.2,
                Height = ShaftBottomY - NeedleBaseY - 6,
                Fill = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(spec, x - 1.8);
            Canvas.SetTop(spec, NeedleBaseY + 3);
            BoardCanvas.Children.Add(spec);

            // 针尖（银白细长锥）
            var needleFill = new LinearGradientBrush(
                Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xB6, 0xC9, 0xD8),
                new Point(0, 0), new Point(1, 0));
            needleFill.Freeze();
            var needle = new Polygon
            {
                Points = new PointCollection
                {
                    new Point(x, TipY),
                    new Point(x - 3.4, NeedleBaseY),
                    new Point(x + 3.4, NeedleBaseY)
                },
                Fill = needleFill,
                Stroke = new SolidColorBrush(Color.FromArgb(0x78, 0x8F, 0xA9, 0xC0)),
                StrokeThickness = 0.6,
                IsHitTestVisible = false
            };
            BoardCanvas.Children.Add(needle);

            //  柱顶圆珠（经典水机柱顶的小圆球）
            var bead = new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = new RadialGradientBrush
                {
                    Center = new Point(0.35, 0.3),
                    GradientOrigin = new Point(0.35, 0.3),
                    GradientStops =
                    {
                        new GradientStop(Color.FromRgb(0xFF, 0xFF, 0xFF), 0),
                        new GradientStop(Color.FromRgb(0xE0, 0xEA, 0xF2), 0.55),
                        new GradientStop(Color.FromRgb(0x8A, 0xA2, 0xB8), 1)
                    }
                },
                Stroke = new SolidColorBrush(Color.FromArgb(0x90, 0x7E, 0x96, 0xAA)),
                StrokeThickness = 0.7,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(bead, x - 3.5);
            Canvas.SetTop(bead, TipY - 7.5);
            BoardCanvas.Children.Add(bead);

            var plinthFill = new LinearGradientBrush(
                Color.FromRgb(0xEE, 0xF5, 0xFB), Color.FromRgb(0xA6, 0xBC, 0xCE),
                new Point(0, 0), new Point(0, 1));
            plinthFill.Freeze();
            var plinth = new Ellipse
            {
                Width = 38,
                Height = 16,
                Fill = plinthFill,
                Stroke = new SolidColorBrush(Color.FromArgb(0x90, 0x8C, 0xA4, 0xBB)),
                StrokeThickness = 0.8,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(plinth, x - 19);
            Canvas.SetTop(plinth, PlinthTopY);
            BoardCanvas.Children.Add(plinth);

            var plinthBase = new Rectangle
            {
                Width = 46,
                Height = 7,
                RadiusX = 3,
                RadiusY = 3,
                Fill = new SolidColorBrush(Color.FromRgb(0xC2, 0xD3, 0xE2)),
                Stroke = new SolidColorBrush(Color.FromArgb(0x90, 0x8C, 0xA4, 0xBB)),
                StrokeThickness = 0.7,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(plinthBase, x - 23);
            Canvas.SetTop(plinthBase, 372);
            BoardCanvas.Children.Add(plinthBase);

            // 柱位单位标签
            Color tagColor = column == 0
                ? Color.FromRgb(0xC2, 0x3A, 0x28)
                : Color.FromRgb(0xD9, 0x7E, 0x2F);
            var tag = new Border
            {
                Height = 17,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                Background = new SolidColorBrush(Color.FromArgb(0xB4, tagColor.R, tagColor.G, tagColor.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(0.8),
                IsHitTestVisible = false
            };
            var tagText = new TextBlock
            {
                Text = unitLabel,
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            tag.Child = tagText;
            tagText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double tagX = column == 0 ? x - 27 - tagText.DesiredSize.Width - 8 : x + 27 + 4;
            Canvas.SetLeft(tag, tagX);
            Canvas.SetTop(tag, 336);
            BoardCanvas.Children.Add(tag);
        }

        private void CreateRing(Color color)
        {
            //  糖果条纹环：沿水平方向的硬停渐变（主色与白交替），
            //  复刻经典套圈玩具的红白/蓝白条纹
            var stroke = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5),
                GradientStops =
                {
                    new GradientStop(Mix(color, Colors.White, 0.72), 0.0),
                    new GradientStop(color, 0.25),
                    new GradientStop(Mix(color, Colors.Black, 0.15), 0.5),
                    new GradientStop(Mix(color, Colors.White, 0.72), 0.75),
                    new GradientStop(color, 1.0)
                }
            };
            stroke.Freeze();

            var body = new Ellipse
            {
                StrokeThickness = 7,
                Stroke = stroke
            };
            //  内缘压暗：给环一个截面厚度感（纯描边看起来像铁丝）
            var innerRim = new Ellipse
            {
                StrokeThickness = 1.6,
                Stroke = new SolidColorBrush(Color.FromArgb(0x38, 0x00, 0x1A, 0x2B)),
                Margin = new Thickness(-2.6)
            };
            var gloss = new Ellipse
            {
                StrokeThickness = 1.8,
                Stroke = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
                Margin = new Thickness(3.0)
            };
            var grid = new Grid
            {
                Width = RingR * 2,
                Height = RingR * 2,
                RenderTransformOrigin = new Point(0.5, 0.5),
                IsHitTestVisible = false
            };
            var squash = new ScaleTransform(1, 0.36);
            var tilt = new RotateTransform(0);
            var group = new TransformGroup();
            group.Children.Add(squash);
            group.Children.Add(tilt);
            grid.RenderTransform = group;
            grid.Children.Add(body);
            grid.Children.Add(innerRim);
            grid.Children.Add(gloss);

            var shadow = new Ellipse
            {
                Width = 28,
                Height = 8,
                Fill = ShadowFill,
                IsHitTestVisible = false,
                Opacity = 0
            };
            _shadowCanvas.Children.Add(shadow);

            var ring = new Ring
            {
                Element = grid,
                Squash = squash,
                Tilt = tilt,
                Shadow = shadow,
                State = StateFloat,
                X = 40 + (BoardW - 80) * _random.NextDouble(),
                BaseY = FloatTop + (FloatBottom - FloatTop) * _random.NextDouble(),
                BobFreq = 0.8 + _random.NextDouble() * 0.7,
                BobPhase = _random.NextDouble() * Math.PI * 2
            };
            ring.Y = ring.BaseY;
            ring.PrevY = ring.Y;
            ring.Vx = (_random.Next(2) == 0 ? -1 : 1) * (5 + _random.NextDouble() * 8);
            ring.Vy = 0;
            _rings.Add(ring);
            _fxCanvas.Children.Add(grid);
        }

        private static Color Mix(Color a, Color b, double k)
        {
            return Color.FromRgb(
                (byte)(a.R + (b.R - a.R) * k),
                (byte)(a.G + (b.G - a.G) * k),
                (byte)(a.B + (b.B - a.B) * k));
        }

        /// <summary>三频随机噪声（非整数频率比，轨迹无周期感）。</summary>
        private static double Turb3(double t, double p1, double p2, double p3)
        {
            return Math.Sin(5.1 * t + p1) + 0.55 * Math.Sin(8.9 * t + p2) + 0.32 * Math.Sin(14.3 * t + p3);
        }

        /// <summary>
        /// 常驻离壁/离底斥力（Float 态专用，不依赖环量）：
        /// 距左/右壁 56px、距底 56px 的区域内按深度平方推离，
        /// 底边推力最大——保证死水期角落的圈也会被缓慢推回涡强区。
        /// </summary>
        private static void ApplyWallRepulsion(Ring ring, double dt)
        {
            const double band = 56.0;
            const double push = 260.0;

            double dxWall = ring.X - 26;                      // 距左壁
            if (dxWall < band)
            {
                ring.Vx += push * (1 - dxWall / band) * (1 - dxWall / band) * dt;
            }
            else
            {
                double dxRight = (BoardW - 26) - ring.X;      // 距右壁
                if (dxRight < band)
                {
                    ring.Vx -= push * (1 - dxRight / band) * (1 - dxRight / band) * dt;
                }
            }

            double dyFloor = (FloatBottom + 4) - ring.Y;      // 距底
            if (dyFloor < band && dyFloor > -20)
            {
                double k = Math.Max(0, 1 - Math.Max(0, dyFloor) / band);
                ring.Vy -= push * 1.35 * k * k * dt;
            }
        }

        private static double StackY(int index)
        {
            return StackY0 + index * StackStep;
        }

        private int StackedCount(int column)
        {
            int count = 0;
            foreach (Ring ring in _rings)
            {
                if (ring.State == StateStacked && ring.Column == column)
                {
                    count++;
                }
            }
            return count;
        }

        private int PendingCount(int column)
        {
            int count = 0;
            foreach (Ring ring in _rings)
            {
                if (ring.Column == column
                    && (ring.State == StateStacked || ring.State == StateCarried
                        || ring.State == StateThreaded))
                {
                    count++;
                }
            }
            return count;
        }

        private void SeedInitialRings(int initialMinutes)
        {
            //  贪心分配（纯逻辑在 RingAllocation，可单测）保证 round-trip 恒等：
            //  旧的"先整除再对左柱单位取模"在满柱时会把余量整段丢掉
            var allocation = Core.Librarys.RingAllocation.Allocate(
                initialMinutes, _leftUnitMinutes, _rightUnitMinutes, MaxRingsPerColumn);
            int left = allocation.Item1;
            int right = allocation.Item2;

            int li = 0, ri = 0;
            foreach (Ring ring in _rings)
            {
                if (li < left)
                {
                    Attach(ring, 0, li++);
                }
                else if (ri < right)
                {
                    Attach(ring, 1, ri++);
                }
                else
                {
                    break;
                }
            }
        }

        private void Attach(Ring ring, int column, int index)
        {
            ring.State = StateStacked;
            ring.Column = column;
            ring.StackIndex = index;
            ring.X = ColumnX[column];
            ring.Y = StackY(index);
            ring.Tilt.Angle = 0;
            ring.Squash.ScaleY = 0.32;
            PlaceRing(ring);
            Canvas.SetZIndex(ring.Element, 10 + index);
        }

        private void PlaceRing(Ring ring)
        {
            Canvas.SetLeft(ring.Element, ring.X - RingR);
            Canvas.SetTop(ring.Element, ring.Y - RingR);
        }

        /// <summary>全局硬约束：位置与速度限制在屏幕内，任何状态不可越界。</summary>
        private static void Contain(Ring ring)
        {
            if (ring.X < 26) { ring.X = 26; if (ring.Vx < 0) ring.Vx = -ring.Vx * 0.4; }
            if (ring.X > BoardW - 26) { ring.X = BoardW - 26; if (ring.Vx > 0) ring.Vx = -ring.Vx * 0.4; }
            if (ring.Y < 14) { ring.Y = 14; if (ring.Vy < 0) ring.Vy = -ring.Vy * 0.15; }
            if (ring.Y > FloatBottom + 4) { ring.Y = FloatBottom + 4; if (ring.Vy > 0) ring.Vy = -ring.Vy * 0.35; }
            if (ring.Vx > 380) ring.Vx = 380;
            if (ring.Vx < -380) ring.Vx = -380;
            if (ring.Vy > 480) ring.Vy = 480;
            if (ring.Vy < -480) ring.Vy = -480;
        }

        /// <summary>
        /// 圈间软碰撞：速度级斥力（重叠越深斥力越强，圈自然滑开，无硬投影抖动）。
        /// - 只修改速度、不直接改位置——下一帧由积分自然分离，重叠→推开→远离平滑过渡，
        ///   不会出现"重叠-弹开-再重叠"的振荡卡顿；
        /// - 只有重叠很深（接近穿模）才做一次位置投影兜底，防止互相嵌死；
        /// - 被卷携圈（固定模式目标）质量感更大（3 倍），受斥力小，引导不被挤散；
        /// - 柱上已套入（Stacked/Threaded）的圈不参与——它们本来就要叠在一起。
        /// </summary>
        private void ResolveRingCollisions(double dt)
        {
            const double softDist = RingR * 2 - 2;   // 软碰撞半径（略小于外径）
            const double hardOverlap = 4;           // 超过该重叠深度才做位置投影
            for (int i = 0; i < _rings.Count; i++)
            {
                Ring a = _rings[i];
                if (!CanCollide(a))
                {
                    continue;
                }
                for (int j = i + 1; j < _rings.Count; j++)
                {
                    Ring b = _rings[j];
                    if (!CanCollide(b))
                    {
                        continue;
                    }
                    double dx = b.X - a.X;
                    double dy = b.Y - a.Y;
                    double dist2 = dx * dx + dy * dy;
                    if (dist2 >= softDist * softDist || dist2 < 1e-6)
                    {
                        continue;
                    }
                    double dist = Math.Sqrt(dist2);
                    double nx = dx / dist;
                    double ny = dy / dist;
                    double overlap = softDist - dist;
                    double k = overlap / softDist;            // 0..1 重叠深度比

                    double massA = a.State == StateCarried ? 3.0 : 1.0;
                    double massB = b.State == StateCarried ? 3.0 : 1.0;

                    // 软斥力加速度：重叠越深越强（二次），按质量分配
                    double accel = 900 * k * k;
                    a.Vx -= nx * accel * dt / massA;
                    a.Vy -= ny * accel * dt / massA;
                    b.Vx += nx * accel * dt / massB;
                    b.Vy += ny * accel * dt / massB;

                    // 深穿透兜底：只投影超出硬阈值的部分，避免穿模嵌死
                    if (overlap > hardOverlap)
                    {
                        double push = (overlap - hardOverlap) * 0.5;
                        a.X -= nx * push;
                        a.Y -= ny * push;
                        b.X += nx * push;
                        b.Y += ny * push;

                        //  投影后立刻夹回水箱：否则圈会被推进墙体，
                        //  下一帧被 Contain 拉回时速度方向仍是"朝墙"，在那儿反复顶死
                        a.X = Math.Max(26, Math.Min(BoardW - 26, a.X));
                        a.Y = Math.Max(14, Math.Min(FloatBottom + 4, a.Y));
                        b.X = Math.Max(26, Math.Min(BoardW - 26, b.X));
                        b.Y = Math.Max(14, Math.Min(FloatBottom + 4, b.Y));
                    }
                }
            }
        }

        /// <summary>参与碰撞的水中圈状态（柱上堆叠的圈不参与）。</summary>
        private static bool CanCollide(Ring r)
        {
            return r.State == StateFloat || r.State == StateCarried || r.State == StateBallistic;
        }

        // ---------------------------------------------------------------
        // 帧循环
        // ---------------------------------------------------------------

        private void FrameTimer_Tick(object sender, EventArgs e)
        {
            //  窗口隐藏/最小化时跳过全部物理与渲染帧工作（弹窗失焦时用户看不到它）
            if (!IsVisible)
            {
                return;
            }

            double now = _clock.Elapsed.TotalSeconds;
            double dt = Math.Min(0.05, Math.Max(0.001, now - _lastSec));
            _lastSec = now;

            // 环量演化：
            // 注入：目标值按一阶滞后逼近（按压机械过程，持续 ~0.45s）
            // 衰减：粘性扩散 Γ(t)=Γ0·e^{−t/τ_ν}（消退持续数秒）
            double inject = Math.Min(1, dt / InjectTau);
            _gammaCW += (_gammaCWTarget - _gammaCW) * inject;
            _gammaCCW += (_gammaCCWTarget - _gammaCCW) * inject;
            double decay = Math.Exp(-dt / ViscTau);
            _gammaCWTarget *= decay;
            _gammaCCWTarget *= decay;
            if (_gammaCWTarget < 0.003) _gammaCWTarget = 0;
            if (_gammaCCWTarget < 0.003) _gammaCCWTarget = 0;
            _gammaCW = Math.Max(0, Math.Min(_gammaCWTarget, _gammaCW));
            _gammaCCW = Math.Max(0, Math.Min(_gammaCCWTarget, _gammaCCW));

            // 重置搅动增强衰减（约 1.6s 内持续把圈吸离底角）
            _stirBoost *= Math.Exp(-dt / 1.6);
            if (_stirBoost < 0.01) _stirBoost = 0;

            for (int i = 0; i < _rings.Count; i++)
            {
                UpdateRing(_rings[i], now, dt);
                UpdateRingShadow(_rings[i]);
            }

            ResolveRingCollisions(dt);

            UpdateAmbientBubbles(now);
            UpdateBubbles(now, dt);
            UpdateRipples(now);
            UpdateWaterLight(now);

            if (_ledFlashOn)
            {
                //  仅在明暗符号翻转时切换（每秒 ~14 次，而非每帧 60 次重设画刷）
                bool bright = Math.Sin(now * 14) > 0;
                if (bright != _ledLastBright)
                {
                    _ledLastBright = bright;
                    TotalText.Foreground = bright ? LedFlash : LedNormal;
                }
            }

            if (_unitRestoreAt > 0 && now > _unitRestoreAt)
            {
                UpdateUnitText();
                _unitRestoreAt = 0;
            }
        }

        private void UpdateRing(Ring ring, double now, double dt)
        {
            double shear = ShearTurbulence();
            switch (ring.State)
            {
                case StateFloat:
                    {
                        // 一阶阻力追随局部流场 + 中性浮力弹簧 + 剪切湍流扰动
                        FlowAt(ring.X, ring.Y, out double fx, out double fy);
                        double act = Activity();
                        //  水流在场时漂浮弹簧软化（圈更容易被流场带起，不会钉在深处）
                        double springK = 1.1 * (1 - 0.4 * Math.Min(1, act));
                        double relax = Math.Min(1, (1.5 + 6.5 * act) * dt);
                        ring.Vx += (fx - ring.Vx) * relax;
                        ring.Vy += (fy - ring.Vy) * relax;

                        //  弹簧目标随水流上浮：静水时回到各自的 BaseY（底部静息带），
                        //  水一搅起来整片圈被抬向涡强区。旧写法永远拉回 BaseY，
                        //  等于把圈焊在底角——这正是"搅几次就再也浮不起来"的主因
                        double liftSpan = (FloatBottom - TipY) * 0.62 * Math.Min(1, act + 0.8 * _stirBoost);
                        double targetY = Math.Max(TipY + 26, ring.BaseY - liftSpan);
                        ring.Vy += (targetY - ring.Y) * springK * dt;
                        ring.Vy -= ring.Vy * 1.3 * dt;
                        ring.Vx += Turb3(now * 0.5, ring.P1, ring.P2, ring.P3) * (7 + 34 * shear) * dt;
                        ring.Vy += Math.Sin(now * ring.BobFreq + ring.BobPhase) * 13 * dt;

                        //  速度升力：水平漂移的圈获得向上分量（流体力学的环流升力效应），
                        //  让圈自然趋向水面上部——涡强区
                        ring.Vy -= Math.Abs(ring.Vx) * 0.6 * dt * (1 + act);

                        //  常驻离壁/离底斥力位场（不依赖环量）：死水期角落不再是准驻留点。
                        //  距边 <56px 时按深度二次推离，底边推力最大、侧壁次之
                        ApplyWallRepulsion(ring, dt);

                        //  确定性脱困：卡在底角、或者速度已经很小的圈，给一点朝向柱子的推力。
                        //  没有这条的话，环量衰减到底流之后圈会永久停在角落，
                        //  用户怎么泵都带不起来（每次泵只加环量，圈本身没有脱离手段）
                        bool cornerPin = ring.Y > FloatBottom - 34 && (ring.X < 74 || ring.X > BoardW - 74);
                        bool sleepy = Math.Abs(ring.Vx) + Math.Abs(ring.Vy) < 26;
                        ring.Idle = (cornerPin || sleepy) ? ring.Idle + dt : 0;
                        if (ring.Idle > 0.45)
                        {
                            double towardX = ColumnX[ring.X < VortexCx ? 0 : 1];
                            ring.Vx += Math.Sign(towardX - ring.X) * 200 * dt;
                            ring.Vy -= 230 * dt;
                            if (ring.Idle > 4) ring.Idle = 0;   // 避免长时间单方向积分
                        }

                        ring.X += ring.Vx * dt;
                        ring.Y += ring.Vy * dt;
                        Contain(ring);

                        ring.Tilt.Angle = Math.Sin(now * 0.9 + ring.BobPhase) * 7
                            + Math.Max(-16, Math.Min(16, ring.Vx * 0.05));
                        ring.Squash.ScaleY = 0.36 + 0.035 * Math.Sin(now * 1.1 + ring.BobPhase);
                        PlaceRing(ring);

                        // 被上涌流抬过针柱区 → 转入飞行态（可套入）
                        if (ring.Y < TipY - 6)
                        {
                            ring.State = StateBallistic;
                            ring.Guided = false;
                            ring.Idle = 0;
                            ring.AngVel = (_random.Next(2) == 0 ? 1 : -1) * (120 + _random.NextDouble() * 180);
                            ring.PrevY = ring.Y;
                        }
                    }
                    break;

                case StateCarried:
                    {
                        // 固定模式目标圈：被吸向本柱上涌核心
                        double age = now - ring.JetStart;
                        double targetX = ColumnX[ring.Column];

                        // 超时保护：若长时间没升上去，改为脱离流场的直接上浮
                        bool directAscent = age > 2.2 && ring.Y > TipY + 8;
                        if (directAscent)
                        {
                            ring.Vx += (targetX - ring.X) * 60 * dt;
                            ring.Vx -= ring.Vx * 3.0 * dt;
                            ring.Vy -= 640 * dt;
                            ring.Vy += GWater * dt;
                            if (ring.Vy < -MaxRise) ring.Vy = -MaxRise;
                        }
                        else
                        {
                            FlowAt(ring.X, ring.Y, out double fx, out double fy);
                            double relax = Math.Min(1, 5.0 * dt);
                            ring.Vx += (fx - ring.Vx) * relax;
                            ring.Vy += (fy - ring.Vy) * relax;

                            ring.Vx += (targetX - ring.X) * 26 * dt;
                            ring.Vx -= ring.Vx * 2.0 * dt;
                            ring.Vx += Turb3(age, ring.P1, ring.P2, ring.P3) * ring.TurbAmp * dt;
                            if (Math.Abs(ring.X - targetX) < 46)
                            {
                                ring.Vy -= 500 * dt;
                            }
                            ring.Vy += GWater * dt;
                            if (ring.Vy < -MaxRise) ring.Vy = -MaxRise;
                        }

                        ring.X += ring.Vx * dt;
                        ring.Y += ring.Vy * dt;
                        Contain(ring);

                        ring.Tilt.Angle = Math.Sin(age * 8.2 + ring.P1) * 13;
                        ring.Squash.ScaleY = 0.40 + 0.09 * Math.Abs(Math.Sin(age * 6.7 + ring.P2));

                        if (ring.Y < TipY - 12)
                        {
                            ring.State = StateBallistic;
                            ring.Idle = 0;
                            ring.Vx += (_random.NextDouble() - 0.5) * 60;
                            ring.AngVel = (_random.Next(2) == 0 ? 1 : -1) * (140 + _random.NextDouble() * 200);
                            ring.PrevY = ring.Y;
                        }
                        PlaceRing(ring);
                    }
                    break;

                case StateBallistic:
                    {
                        ring.Vy += GFree * dt;
                        ring.Vx *= Math.Exp(-1.15 * dt);
                        ring.Vy *= Math.Exp(-1.3 * dt);

                        FlowAt(ring.X, ring.Y, out double fx, out double fy);
                        ring.Vx += fx * 0.4 * dt;
                        ring.Vy += fy * 0.22 * dt;

                        if (ring.Guided && ring.Vy > -60)
                        {
                            // 固定模式：回落引导向目标柱
                            ring.Vx += (ColumnX[ring.Column] - ring.X) * 26 * dt;
                            if (ring.Vx > 260) ring.Vx = 260;
                            if (ring.Vx < -260) ring.Vx = -260;
                        }
                        else if (!ring.Guided && ring.Vy > 0)
                        {
                            // 随机模式：近针弱吸引（提高套中率，仍以水流为主）
                            for (int c = 0; c < 2; c++)
                            {
                                double dx = ColumnX[c] - ring.X;
                                if (Math.Abs(dx) < WeakGuideBand && Math.Abs(ring.Y - (TipY + 10)) < 70)
                                {
                                    ring.Vx += dx * 3.2 * dt;
                                }
                            }
                        }

                        ring.X += ring.Vx * dt;
                        ring.Y += ring.Vy * dt;

                        //  落地判定必须抢在 Contain 之前：Contain 会把 Y 夹到 FloatBottom+4，
                        //  之后再判 "Y > FloatBottom+4" 永远为假，落地分支永不执行 ——
                        //  圈就永久停在 Ballistic 态、被夹在水底，怎么搅都起不来
                        bool hitFloor = ring.Y >= FloatBottom + 4;
                        Contain(ring);
                        ring.Tilt.Angle += ring.AngVel * dt;
                        ring.AngVel *= Math.Exp(-1.8 * dt);
                        ring.Squash.ScaleY = 0.36 + 0.12 * Math.Abs(Math.Sin(now * 6 + ring.P2));

                        // 回落穿过针尖高度：对准则套入（随机模式两柱皆可）
                        if (ring.PrevY <= TipY + 2 && ring.Y > TipY + 2 && ring.Vy > 0)
                        {
                            int hit = -1;
                            if (ring.Guided)
                            {
                                hit = ring.Column;
                                double dx = ring.X - ColumnX[hit];
                                if (Math.Abs(dx) > ThreadBand * 1.4)
                                {
                                    ring.X = ColumnX[hit] + ThreadBand * 1.2 * Math.Sign(dx);
                                }
                            }
                            else
                            {
                                if (Math.Abs(ring.X - ColumnX[0]) < ThreadBand) hit = 0;
                                else if (Math.Abs(ring.X - ColumnX[1]) < ThreadBand) hit = 1;
                            }

                            if (hit >= 0 && PendingCount(hit) < MaxRingsPerColumn)
                            {
                                ring.Column = hit;
                                ring.StackIndex = PendingCount(hit);
                                ring.State = StateThreaded;
                                ring.Vx *= 0.3;
                                ring.AngVel *= 0.2;
                                ring.Bounces = 0;
                                SpawnMicroBubbles(ColumnX[hit], TipY + 6, 3, 90);
                            }
                            else if (hit >= 0)
                            {
                                ring.Vx = (ring.X < ColumnX[hit] ? -1 : 1) * (110 + _random.NextDouble() * 70);
                                ring.Vy *= 0.4;
                            }
                        }
                        ring.PrevY = ring.Y;

                        //  超时兜底：圈可能压在别的圈上、永远触不到箱底，
                        //  此态只吃 0.22 倍流场力 —— 不兜底就是永久卡死（多泵几轮必现）
                        ring.Idle += dt;
                        bool restingOnPile = ring.Idle > 1.2 && Math.Abs(ring.Vx) + Math.Abs(ring.Vy) < 70;
                        if (restingOnPile || ring.Idle > 9)
                        {
                            ring.State = StateFloat;
                            ring.Guided = false;
                            ring.Idle = 0;
                            ring.BaseY = FloatTop + (FloatBottom - FloatTop) * (0.05 + 0.9 * _random.NextDouble());
                        }

                        //  兜底：贴底且几乎不动时也回漂浮态。落地分支依赖精确触底，
                        //  一旦因为数值抖动错过，圈会永久停在 Ballistic（贴底不动）
                        if (ring.State == StateBallistic && ring.Y > FloatBottom - 6 && Math.Abs(ring.Vy) < 45)
                        {
                            hitFloor = true;
                        }

                        // 落回水底（hitFloor 在 Contain 之前采样）
                        if (hitFloor)
                        {
                            if (ring.Vy > 110)
                            {
                                ring.Y = FloatBottom + 4;
                                ring.Vy = -ring.Vy * 0.26;
                                SpawnRipple(ring.X, 372);
                                SpawnMicroBubbles(ring.X, 366, 3, 60);
                            }
                            else if (ring.Guided)
                            {
                                // 固定模式兜底：没套中就再次被卷起；
                                // 柱已被泵满时转为漂浮，否则会陷入"卷起-弹开-落下"的无限循环
                                if (PendingCount(ring.Column) >= MaxRingsPerColumn)
                                {
                                    ring.State = StateFloat;
                                    ring.Guided = false;
                                    ring.BaseY = FloatTop + (FloatBottom - FloatTop) * (0.05 + 0.9 * _random.NextDouble());
                                }
                                else
                                {
                                    Carry(ring, ring.Column, now);
                                    PumpCirculation(ring.Column == 0, false);
                                    SpawnRipple(ring.X, 372);
                                }
                            }
                            else
                            {
                                ring.State = StateFloat;
                                ring.Guided = false;
                                //  落底不回最深 BaseY：随机模式的圈落回浅水带，
                                //  生活区上移到涡强区，避免被重新钉死在底角
                                ring.BaseY = FloatTop + (FloatBottom - FloatTop) * (0.05 + 0.9 * _random.NextDouble());
                                ring.Tilt.Angle = 0;
                                SpawnRipple(ring.X, 372);
                            }
                        }
                        PlaceRing(ring);
                    }
                    break;

                case StateThreaded:
                    {
                        double colX = ColumnX[ring.Column];
                        double targetY = StackY(ring.StackIndex);
                        ring.Vx += (colX - ring.X) * 150 * dt;
                        ring.Vx *= Math.Exp(-8 * dt);
                        ring.Vy += 520 * dt;
                        ring.X += ring.Vx * dt;
                        ring.Y += ring.Vy * dt;
                        ring.Tilt.Angle *= Math.Exp(-6 * dt);
                        ring.Squash.ScaleY += (0.32 - ring.Squash.ScaleY) * Math.Min(1, 8 * dt);

                        if (ring.Y >= targetY)
                        {
                            ring.Y = targetY;
                            if (Math.Abs(ring.Vy) > 60 && ring.Bounces < 3)
                            {
                                ring.Vy = -ring.Vy * 0.30;
                                ring.Bounces++;
                                ring.Vx += (_random.NextDouble() - 0.5) * 44;
                                SpawnMicroBubbles(ring.X, ring.Y - 6, 2, 70);
                            }
                            else
                            {
                                ring.State = StateStacked;
                                ring.Guided = false;
                                ring.X = colX;
                                ring.Tilt.Angle = 0;
                                ring.Squash.ScaleY = 0.32;
                                PlaceRing(ring);
                                Canvas.SetZIndex(ring.Element, 10 + ring.StackIndex);
                                SpawnRipple(colX, ring.Y + 5);
                                SpawnMicroBubbles(colX, ring.Y - 8, 5, 150);
                                UpdateTotal();
                                _ledFlashOn = true;
                                _unitRestoreAt = Math.Max(_unitRestoreAt, _clock.Elapsed.TotalSeconds + 0.55);
                            }
                        }
                        PlaceRing(ring);
                    }
                    break;

                case StateStacked:
                    break;
            }
        }

        private void Carry(Ring ring, int column, double now)
        {
            ring.State = StateCarried;
            ring.Guided = true;
            ring.Column = column;
            ring.JetStart = now;
            ring.TurbAmp = 210 + _random.NextDouble() * 150;
            ring.P1 = _random.NextDouble() * Math.PI * 2;
            ring.P2 = _random.NextDouble() * Math.PI * 2;
            ring.P3 = _random.NextDouble() * Math.PI * 2;
            ring.Bounces = 0;
            ring.Vy = Math.Min(ring.Vy, -20);
            Canvas.SetZIndex(ring.Element, 30);
        }

        private void UpdateRingShadow(Ring ring)
        {
            bool floating = ring.State == StateFloat || ring.State == StateCarried || ring.State == StateBallistic;
            if (!floating)
            {
                if (ring.Shadow.Opacity != 0)
                {
                    ring.Shadow.Opacity = 0;
                }
                return;
            }
            double k = Math.Min(1, Math.Max(0, (ring.Y - 170) / (FloorY - 170)));
            ring.Shadow.Opacity = 0.05 + 0.13 * k;
            Canvas.SetLeft(ring.Shadow, ring.X - 14);
            Canvas.SetTop(ring.Shadow, FloorY - 2);
        }

        private void UpdateWaterLight(double now)
        {
            double act = Activity();
            foreach (Caustic c in _caustics)
            {
                Canvas.SetLeft(c.Element, c.BaseX + Math.Sin(now * 0.31 + c.Phase) * 24);
                c.Element.Opacity = (0.4 + 0.6 * act) * (0.55 + 0.45 * Math.Sin(now * 0.47 + c.Phase * 1.7));
            }
            foreach (LightRay r in _rays)
            {
                r.Rot.Angle = r.BaseAngle + Math.Sin(now * 0.24 + r.Phase) * 2.6;
            }
        }

        private void UpdateAmbientBubbles(double now)
        {
            // 气泡生成率与环流活动度成正比：产生与消退随环量注入/衰减持续（非瞬时）
            double act = Activity();
            if (act <= 0.02)
            {
                if (_bubbles.Count > 0 && now > _nextBubbleAt)
                {
                    // 静止后偶发一个环境气泡
                    _nextBubbleAt = now + 2.5 + _random.NextDouble() * 2;
                    AddBubble(30 + _random.NextDouble() * (BoardW - 60), FloorY - _random.NextDouble() * 8,
                        20 + _random.NextDouble() * 24, 2.5 + _random.NextDouble() * 2.8,
                        12 + _random.NextDouble() * 8, now);
                }
                return;
            }
            if (now < _nextBubbleAt || _bubbles.Count > 50)
            {
                return;
            }
            _nextBubbleAt = now + (0.55 - 0.35 * act) * (0.7 + _random.NextDouble() * 0.8);
            AddBubble(30 + _random.NextDouble() * (BoardW - 60), FloorY - _random.NextDouble() * 10,
                24 + _random.NextDouble() * 30 * act, 2.2 + _random.NextDouble() * 3,
                12 + _random.NextDouble() * 8, now);
        }

        private void UpdateBubbles(double now, double dt)
        {
            double act = Activity();
            double shear = ShearTurbulence();
            for (int i = _bubbles.Count - 1; i >= 0; i--)
            {
                Bubble b = _bubbles[i];
                if (now < b.StartAt)
                {
                    continue;
                }
                if (b.Birth < 0)
                {
                    b.Birth = now;
                }
                // 气泡受局部流场影响（随流弯曲）+ 剪切湍流抖动
                FlowAt(b.X, b.Y, out double fx, out double fy);
                double turbX = shear > 0.05
                    ? Turb3(now * 1.4, b.SwayPhase, b.SwayPhase * 0.7, b.SwayPhase * 1.9) * 46 * shear
                    : 0;
                b.Y -= (b.Speed - fy * 0.4) * dt;
                b.X += (Math.Sin(now * 2.2 + b.SwayPhase) * b.SwayAmp + fx * 0.6 + turbX) * dt;
                Canvas.SetLeft(b.Element, b.X - b.Element.Width / 2);
                Canvas.SetTop(b.Element, b.Y - b.Element.Height / 2);

                double opacity = 0.4 + 0.6 * Math.Min(1, act + 0.2);
                if (b.Y < 60)
                {
                    opacity *= Math.Max(0, (b.Y - 20) / 40);
                }
                if (b.Life > 0)
                {
                    double remain = b.Life - (now - b.Birth);
                    if (remain < 0.25)
                    {
                        opacity *= Math.Max(0, remain / 0.25);
                    }
                }
                bool gone = b.Y < 20 || (b.Life > 0 && now - b.Birth > b.Life);
                if (gone)
                {
                    _fxCanvas.Children.Remove(b.Element);
                    _bubbles.RemoveAt(i);
                }
                else
                {
                    b.Element.Opacity = 0.75 * opacity;
                }
            }
        }

        private void UpdateRipples(double now)
        {
            for (int i = _ripples.Count - 1; i >= 0; i--)
            {
                Ripple r = _ripples[i];
                double t = (now - r.Birth) / 0.45;
                if (t >= 1)
                {
                    _fxCanvas.Children.Remove(r.Element);
                    _ripples.RemoveAt(i);
                    continue;
                }
                double radius = 7 + 26 * t;
                r.Element.Width = radius * 2;
                r.Element.Height = radius * 0.9;
                r.Element.Opacity = 0.6 * (1 - t);
                Canvas.SetLeft(r.Element, r.CenterX - radius);
                Canvas.SetTop(r.Element, r.CenterY - radius * 0.45);
            }
        }

        // ---------------------------------------------------------------
        // 特效生成（仅气泡与落地波纹）
        // ---------------------------------------------------------------

        private void AddBubble(double x, double y, double speed, double size, double life, double now)
        {
            if (_bubbles.Count > 60)
            {
                return;
            }
            var el = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = BubbleBrush,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(el, x - size / 2);
            Canvas.SetTop(el, y - size / 2);
            _fxCanvas.Children.Add(el);
            _bubbles.Add(new Bubble
            {
                Element = el,
                X = x,
                Y = y,
                Speed = speed,
                SwayPhase = _random.NextDouble() * Math.PI * 2,
                SwayAmp = 8 + _random.NextDouble() * 14,
                Birth = -1,
                StartAt = now,
                Life = life
            });
        }

        private void SpawnMicroBubbles(double x, double y, int count, double speed)
        {
            double now = _clock.IsRunning ? _clock.Elapsed.TotalSeconds : 0;
            for (int i = 0; i < count; i++)
            {
                AddBubble(x + (_random.NextDouble() - 0.5) * 14, y + (_random.NextDouble() - 0.5) * 8,
                    speed + _random.NextDouble() * speed, 1.8 + _random.NextDouble() * 2.2,
                    0.38 + _random.NextDouble() * 0.24, now);
            }
        }

        private void SpawnRipple(double x, double y)
        {
            var el = new Ellipse
            {
                Width = 14,
                Height = 7,
                Stroke = RippleStroke,
                StrokeThickness = 1.6,
                IsHitTestVisible = false
            };
            _fxCanvas.Children.Add(el);
            _ripples.Add(new Ripple
            {
                Element = el,
                CenterX = x,
                CenterY = y,
                Birth = _clock.IsRunning ? _clock.Elapsed.TotalSeconds : 0
            });
        }

        // ---------------------------------------------------------------
        // 交互
        // ---------------------------------------------------------------

        private void SetupJetButton(System.Windows.Controls.Button button, int targetColumn)
        {
            string columnName = targetColumn == 0 ? "左柱" : "右柱";
            string unit = targetColumn == 0
                ? (_isWork ? "1小时" : "30分")
                : (_isWork ? "10分" : "5分");
            string dir = targetColumn == 0 ? "顺时针" : "逆时针";
            Brush tint = new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xEA));

            //  右钮箭头水平镜像：与左钮构成一顺一逆的环流方向指示
            var arrow = new UI.Controls.Base.Icon
            {
                IconType = UI.Controls.Base.IconTypes.Refresh,
                FontSize = 14,
                Foreground = tint,
                HorizontalAlignment = HorizontalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(targetColumn == 0 ? 1 : -1, 1)
            };
            var panel = new StackPanel();
            panel.Children.Add(arrow);
            panel.Children.Add(new TextBlock
            {
                Text = columnName,
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = tint,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, -2, 0, 0)
            });
            panel.Children.Add(new TextBlock
            {
                Text = unit,
                FontSize = 8,
                Foreground = tint,
                Opacity = 0.85,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            panel.Children.Add(new TextBlock
            {
                Text = "PUSH",
                FontSize = 7,
                FontWeight = FontWeights.Bold,
                Foreground = tint,
                Opacity = 0.7,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            button.Content = panel;
        }

        /// <summary>
        /// 注入环量：clockwise=true 向顺时针（左钮）叠加一档，false 向逆时针（右钮）。
        /// 多按一次就再叠加一档（上限 Γ_max）；反向环量并存时依物理相互抵消。
        /// </summary>
        private void PumpCirculation(bool clockwise, bool spawnBurst)
        {
            if (clockwise)
            {
                _gammaCWTarget = Math.Min(GammaMax, _gammaCWTarget + GammaPerPump);
            }
            else
            {
                _gammaCCWTarget = Math.Min(GammaMax, _gammaCCWTarget + GammaPerPump);
            }
            if (spawnBurst)
            {
                double now = _clock.IsRunning ? _clock.Elapsed.TotalSeconds : 0;
                for (int i = 0; i < 5; i++)
                {
                    double ang = _random.NextDouble() * Math.PI * 2;
                    double rr = 60 + _random.NextDouble() * 130;
                    AddBubble(VortexCx + Math.Cos(ang) * rr, VortexCy + Math.Sin(ang) * rr * 0.8,
                        40 + _random.NextDouble() * 60, 2 + _random.NextDouble() * 2.6,
                        1.4 + _random.NextDouble() * 0.8, now + i * 0.05);
                }
            }
        }

        /// <summary>
        /// 泵动：向对应方向注入环量（左钮顺时针、右钮逆时针，可同时/连按叠加）；
        /// 固定模式下同时指定一个圈被对应柱上涌流卷携，保证套上该柱。
        /// </summary>
        private void Pump(int column)
        {
            PumpCirculation(column == 0, true);
            //  泵动也注入短时搅动增强（此前只有 Reset 有）：
            //  帮助底角/边缘的圈脱离死水区被卷入环流
            _stirBoost = Math.Max(_stirBoost, 0.6);

            if (_fixedMode)
            {
                if (PendingCount(column) >= MaxRingsPerColumn)
                {
                    UnitText.Text = column == 0 ? "左柱已套满 6 圈，先重置或泵右侧" : "右柱已套满 6 圈，先重置或泵左侧";
                    _unitRestoreAt = _clock.Elapsed.TotalSeconds + 1.8;
                    return;
                }
                UpdateUnitText();

                // 挑离目标柱最近的漂浮圈
                Ring chosen = null;
                double best = double.MaxValue;
                foreach (Ring r in _rings)
                {
                    if (r.State != StateFloat)
                    {
                        continue;
                    }
                    double score = Math.Abs(r.X - ColumnX[column]) + _random.NextDouble() * 90;
                    if (score < best)
                    {
                        best = score;
                        chosen = r;
                    }
                }
                if (chosen == null)
                {
                    return;
                }
                Carry(chosen, column, _clock.Elapsed.TotalSeconds);
            }
            else
            {
                UpdateUnitText();
            }
        }

        private void ResetAll()
        {
            double now = _clock.Elapsed.TotalSeconds;
            bool any = false;
            foreach (Ring ring in _rings)
            {
                if (ring.State == StateStacked || ring.State == StateThreaded || ring.State == StateCarried)
                {
                    any = true;
                    ring.State = StateBallistic;
                    ring.Guided = false;
                    ring.Vx = (_random.NextDouble() - 0.5) * 320;
                    ring.Vy = -(240 + _random.NextDouble() * 200);
                    ring.AngVel = (_random.Next(2) == 0 ? 1 : -1) * (300 + _random.NextDouble() * 400);
                    ring.Bounces = 0;
                    ring.PrevY = ring.Y;
                    Canvas.SetZIndex(ring.Element, 5);
                }
                else if (ring.State == StateFloat)
                {
                    // 全箱翻腾：水底（含角落）的圈也被抬离底部，
                    // 脱离死水区进入环流中上部，避免"重置后仍卡在角落"
                    any = true;
                    ring.Vy = -(160 + _random.NextDouble() * 140);
                    ring.Vx = (_random.Next(2) == 0 ? -1 : 1) * (60 + _random.NextDouble() * 80);
                    ring.BaseY = FloatTop + (FloatBottom - FloatTop) * _random.NextDouble() * 0.35;
                    ring.AngVel = (_random.Next(2) == 0 ? 1 : -1) * (200 + _random.NextDouble() * 280);
                    ring.PrevY = ring.Y;
                }
            }
            if (!any)
            {
                return;
            }
            // 柱底同时爆发气泡，把圈吹飞；注入单一方向净环流（顺时针 1.5 档）搅散——
            // 之前双向各注一档会恰好抵消（净 Γ=0），水不转、圈落回水面就飞不起来
            SpawnMicroBubbles(ColumnX[0], PlinthTopY, 8, 190);
            SpawnMicroBubbles(ColumnX[1], PlinthTopY, 8, 190);
            SpawnRipple(ColumnX[0], PlinthTopY + 4);
            SpawnRipple(ColumnX[1], PlinthTopY + 4);
            _gammaCWTarget = Math.Min(GammaMax, 1.5);
            _gammaCCWTarget = 0;
            _stirBoost = 1.0;
            UpdateUnitText();
            UpdateTotal();
            _ledFlashOn = true;
            _unitRestoreAt = Math.Max(_unitRestoreAt, now + 0.5);
        }

        //  模式钮四态画刷缓存（避免每次点击 new）
        private static readonly SolidColorBrush ModeActiveBrush = new SolidColorBrush(Color.FromRgb(0x2F, 0xA3, 0xD8));
        private static readonly SolidColorBrush ModeIdleBrush = new SolidColorBrush(Color.FromRgb(0x0E, 0x33, 0x55));
        private static readonly SolidColorBrush ModeActiveText = Brushes.White;
        private static readonly SolidColorBrush ModeIdleText = new SolidColorBrush(Color.FromRgb(0x9F, 0xC7, 0xE6));

        private void SetMode(bool fixedMode)
        {
            _fixedMode = fixedMode;
            FixedModeButton.Background = _fixedMode ? ModeActiveBrush : ModeIdleBrush;
            FixedModeButton.Foreground = _fixedMode ? ModeActiveText : ModeIdleText;
            RandomModeButton.Background = _fixedMode ? ModeIdleBrush : ModeActiveBrush;
            RandomModeButton.Foreground = _fixedMode ? ModeIdleText : ModeActiveText;
            UpdateUnitText();
        }

        private void UpdateUnitText()
        {
            string units = _isWork
                ? "左柱 1 圈 = 1 小时 · 右柱 1 圈 = 10 分钟"
                : "左柱 1 圈 = 30 分钟 · 右柱 1 圈 = 5 分钟";
            string hint = _fixedMode
                ? "固定模式：每泵一次必有一圈套上对应柱"
                : "随机模式：圈随全屏环流飘飞，反向叠加抵消";
            _ledFlashOn = false;
            TotalText.Foreground = LedNormal;
            UnitText.Text = units + "\n" + hint;
        }

        private void UpdateTotal()
        {
            int total = StackedCount(0) * _leftUnitMinutes + StackedCount(1) * _rightUnitMinutes;
            TotalText.Text = string.Format("{0:00}:{1:00}", total / 60, total % 60);
        }

        // ---------------------------------------------------------------
        // 按键
        // ---------------------------------------------------------------

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Left:
                    Pump(0);   // 左钮：顺时针环流
                    e.Handled = true;
                    break;
                case Key.Right:
                    Pump(1);   // 右钮：逆时针环流
                    e.Handled = true;
                    break;
                case Key.R:
                    ResetAll();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    Confirm();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    DialogResult = false;
                    e.Handled = true;
                    break;
            }
        }

        private void LeftJetButton_Click(object sender, RoutedEventArgs e)
        {
            Pump(0);
            Focus();
        }

        private void RightJetButton_Click(object sender, RoutedEventArgs e)
        {
            Pump(1);
            Focus();
        }

        private void RandomModeButton_Click(object sender, RoutedEventArgs e)
        {
            SetMode(false);
            Focus();
        }

        private void FixedModeButton_Click(object sender, RoutedEventArgs e)
        {
            SetMode(true);
            Focus();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            ResetAll();
            Focus();
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            Confirm();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void Confirm()
        {
            //  按"必然归属该柱"的圈结算（Stacked+Carried+Threaded）：
            //  飞行/悬挂中的圈在固定模式有必中保证，确认时直接计入——
            //  旧实现只数已落柱的圈，泵完立刻确认会静默丢掉在空的圈
            int total = PendingCount(0) * _leftUnitMinutes + PendingCount(1) * _rightUnitMinutes;
            _applyResult?.Invoke(_isWork, total);
            DialogResult = true;
        }
    }
}
