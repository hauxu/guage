using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;

namespace UI.Base
{
    /// <summary>
    /// 窗口毛玻璃材质。
    ///
    /// 这里用的是 SetWindowCompositionAttribute 而不是官方的 DwmSetWindowAttribute 系统材质。
    /// 后者虽然有正式文档，但其内建着色无法调整，实测背景透过率最高只有约四成，
    /// 达不到"接近全透"的效果；前者允许自行指定着色与不透明度，着色取全透明时就是纯模糊。
    /// 另外它不需要把窗口边框延伸进客户区，因此不会出现 DWM 自绘标题栏按钮与自定义按钮重叠的问题。
    /// </summary>
    public static class WindowBackdrop
    {
        //  窗口合成属性
        private const int WCA_ACCENT_POLICY = 19;

        //  合成状态
        private const int ACCENT_DISABLED = 0;
        private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
        //  绘制全部边框，否则窗口四周会缺失
        private const int ACCENT_FLAG_DRAW_ALL_BORDERS = 0x20 | 0x40 | 0x80 | 0x100;

        //  DwmSetWindowAttribute 属性编号，仅用于同步标题栏深浅色
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        //  Windows 11 窗口圆角偏好
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_DONOTROUND = 1;
        private const int DWMWCP_ROUND = 2;
        private const int DWMWCP_ROUNDSMALL = 3;

        //  亚克力自 Windows 10 1803 起可用，这里只面向 Windows 11
        private const int MinimumBuild = 22000;

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            /// <summary>
            /// 着色，字节序为 AABBGGRR
            /// </summary>
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("ntdll.dll")]
        private static extern int RtlGetVersion(ref OsVersionInfo versionInfo);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OsVersionInfo
        {
            public int Size;
            public int MajorVersion;
            public int MinorVersion;
            public int BuildNumber;
            public int PlatformId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string CsdVersion;
        }

        private static bool? isSupported;

        /// <summary>
        /// 当前系统是否支持毛玻璃。
        /// </summary>
        public static bool IsSupported
        {
            get
            {
                if (isSupported == null)
                {
                    isSupported = GetRealBuildNumber() >= MinimumBuild;
                }
                return isSupported.Value;
            }
        }

        /// <summary>
        /// 取真实的系统版本号。程序清单没有声明 supportedOS 时
        /// Environment.OSVersion 会谎报成 Windows 8，只有 RtlGetVersion 不受影响。
        /// </summary>
        private static int GetRealBuildNumber()
        {
            try
            {
                var info = new OsVersionInfo();
                info.Size = Marshal.SizeOf(typeof(OsVersionInfo));
                if (RtlGetVersion(ref info) != 0 || info.MajorVersion < 10)
                {
                    return 0;
                }
                return info.BuildNumber;
            }
            catch
            {
                //  取不到版本号时按不支持处理，回退到纯色背景
                return 0;
            }
        }

        /// <summary>
        /// 着色的最低不透明度。着色取全透明时系统会把窗口整块变成透明且不再做模糊，
        /// 因此必须保留一点着色，模糊才会真正渲染出来。
        /// 48/255≈19%：最透明档仍保有足够底色，背后桌面的深色内容不会以"纯模糊残影"的形式透出。
        /// </summary>
        public const byte MinimumTintAlpha = 48;

        //  同值守卫 + 提交限流：SetWindowCompositionAttribute 每次调用都会让 DWM
        //  拆建模糊区（中间帧发黑），拖动滑块时必须合并提交
        private static bool _lastEnabled;
        private static byte _lastAlpha;
        private static System.Windows.Media.Color _lastTint;
        private static IntPtr _lastHwnd;
        private static bool _hasSubmitted;
        private static System.Diagnostics.Stopwatch _submitWatch;
        private static bool _pendingEnabled;
        private static System.Windows.Media.Color _pendingTint;
        private static byte _pendingAlpha;
        private static IntPtr _pendingHwnd;
        private static System.Windows.Threading.DispatcherTimer _pendingTimer;

        /// <summary>
        /// 为窗口开启或关闭毛玻璃，返回材质当前是否处于开启状态。
        /// 相同参数不重复提交；变化时按 66ms 限流（拖动中合并提交，消除闪烁黑条）。
        /// </summary>
        /// <param name="tint">着色颜色，通常取当前主题的窗口底色</param>
        /// <param name="tintAlpha">着色不透明度，会被抬到不低于 <see cref="MinimumTintAlpha"/></param>
        public static bool Apply(System.Windows.Window window, bool isEnabled, System.Windows.Media.Color tint, byte tintAlpha)
        {
            if (window == null || !IsSupported)
            {
                return false;
            }

            //  窗口句柄要等到 SourceInitialized 之后才存在
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            byte alpha = Math.Max(MinimumTintAlpha, tintAlpha);

            //  同值守卫：参数未变不重提交（这是拖动闪烁的主因）
            if (_hasSubmitted && isEnabled == _lastEnabled && alpha == _lastAlpha
                && tint == _lastTint && hwnd == _lastHwnd)
            {
                return _lastEnabled;
            }

            if (_submitWatch == null)
            {
                _submitWatch = System.Diagnostics.Stopwatch.StartNew();
            }

            //  开/关翻转必须立即提交：限流挂起路径曾只携带 tint/alpha，
            //  Tick 用旧的 _lastEnabled 提交会把"启用/关闭"静默吞掉
            //  （表现为启动后玻璃永远不开，或玻璃关不掉）
            bool enabledChanged = isEnabled != _lastEnabled;
            bool submitNow = !_hasSubmitted || enabledChanged
                || (_submitWatch.ElapsedMilliseconds - LastSubmitMs) >= 66;
            if (!submitNow)
            {
                //  限流窗口内：挂起最新值，定时器到期后只提交最后一次
                _pendingEnabled = isEnabled;
                _pendingTint = tint;
                _pendingAlpha = alpha;
                _pendingHwnd = hwnd;
                if (_pendingTimer == null)
                {
                    _pendingTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(66)
                    };
                    _pendingTimer.Tick += (s, e) =>
                    {
                        _pendingTimer.Stop();
                        if (_hasSubmitted)
                        {
                            if (SubmitAccent(_pendingHwnd, _pendingEnabled, _pendingTint, _pendingAlpha))
                            {
                                //  回写最近提交参数：否则下次同参数会被误判为"有变化"造成多余重提交
                                _lastEnabled = _pendingEnabled;
                                _lastTint = _pendingTint;
                                _lastAlpha = _pendingAlpha;
                                _lastHwnd = _pendingHwnd;
                            }
                            LastSubmitMs = _submitWatch.ElapsedMilliseconds;
                        }
                    };
                }
                _pendingTimer.Stop();
                _pendingTimer.Start();
                return _lastEnabled;
            }

            LastSubmitMs = _submitWatch.ElapsedMilliseconds;
            _pendingTimer?.Stop();
            if (SubmitAccent(hwnd, isEnabled, tint, alpha))
            {
                _lastEnabled = isEnabled;
                _lastAlpha = alpha;
                _lastTint = tint;
                _lastHwnd = hwnd;
                _hasSubmitted = true;
                return isEnabled;
            }

            //  提交失败不回写状态：下次调用仍视为"有变化"，允许重试
            Core.Librarys.Logger.Error("毛玻璃 accent 提交失败，已保留状态待重试 hwnd=" + hwnd);
            return _lastEnabled;
        }

        private static double LastSubmitMs { get; set; }

        private static bool SubmitAccent(IntPtr hwnd, bool isEnabled, System.Windows.Media.Color tint, byte alpha)
        {
            //  字节序为 AABBGGRR
            int gradientColor = (alpha << 24) | (tint.B << 16) | (tint.G << 8) | tint.R;

            var policy = new AccentPolicy
            {
                AccentState = isEnabled ? ACCENT_ENABLE_ACRYLICBLURBEHIND : ACCENT_DISABLED,
                AccentFlags = ACCENT_FLAG_DRAW_ALL_BORDERS,
                GradientColor = gradientColor,
                AnimationId = 0
            };

            int size = Marshal.SizeOf(policy);
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, buffer, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    Data = buffer,
                    SizeOfData = size
                };
                if (SetWindowCompositionAttribute(hwnd, ref data) == 0)
                {
                    return false;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            //  WPF 默认用不透明色填充 HWND 背景，不改成透明的话模糊会被整个盖住。
            //  关闭时不必还原，此时窗口背景笔刷本身就是不透明的。
            if (isEnabled)
            {
                var source = HwndSource.FromHwnd(hwnd);
                if (source != null && source.CompositionTarget != null)
                {
                    source.CompositionTarget.BackgroundColor = System.Windows.Media.Colors.Transparent;
                }
            }

            return isEnabled;
        }

        /// <summary>
        /// 同步系统绘制部分的深浅色，避免深色主题下窗口外框仍是浅色。
        /// </summary>
        /// <summary>
        /// 着色层不做可辨识的填色时用的 alpha。DWM 的 accent 是按**窗口矩形**铺的，
        /// 只要它带可见颜色，圆角处就会露出那一层的直角（"上面圆角、下面直角"）。
        /// 把 alpha 压到 1（≈0.4%）后：模糊still在，颜色改由 WPF 侧主题笔刷在圆角内绘制，
        /// 于是既有抗锯齿圆角、也保住了外发光阴影。
        /// </summary>
        private const byte BlurOnlyTintAlpha = 1;

        /// <summary>
        /// 只上模糊、不上色。颜色由 WPF 主题笔刷负责，避免 DWM 那层把圆角填平。
        /// 注意不要走 Apply()：那里的 MinimumTintAlpha 兜底会把 alpha 抬到 48。
        /// </summary>
        public static bool ApplyBlurOnly(System.Windows.Window window, System.Windows.Media.Color tint)
        {
            if (window == null || !IsSupported)
            {
                return false;
            }

            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            if (!SubmitAccent(hwnd, true, tint, BlurOnlyTintAlpha))
            {
                return false;
            }

            _hasSubmitted = true;
            _lastEnabled = true;
            _lastTint = tint;
            _lastAlpha = BlurOnlyTintAlpha;
            _lastHwnd = hwnd;
            return true;
        }

        public static void SetDarkMode(System.Windows.Window window, bool isDark)
        {
            if (window == null || !IsSupported)
            {
                return;
            }

            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            int value = isDark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        }

        /// <summary>
        /// 设置窗口是否使用系统圆角。最大化时应关闭，否则边缘会留出空隙。
        /// </summary>
        public static void SetRoundedCorners(System.Windows.Window window, int cornerRadius, bool isActive = true)
        {
            if (window == null || !IsSupported)
            {
                return;
            }

            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            int preference = GetCornerPreference(cornerRadius, isActive);
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }

        private static int GetCornerPreference(int cornerRadius, bool isActive)
        {
            if (!isActive || cornerRadius <= 0)
            {
                return DWMWCP_DONOTROUND;
            }

            if (cornerRadius <= 8)
            {
                return DWMWCP_ROUNDSMALL;
            }

            return DWMWCP_ROUND;
        }
    }
}
