using System;
using System.Windows;
using System.Windows.Media;

namespace UI.Base.Animation
{
    /// <summary>
    /// 根据窗口可见性与帧耗时，在完整动效、降级动效、后台静默之间切换。
    /// </summary>
    public static class AnimationBudget
    {
        private const int SampleWindow = 36;
        private const int SlowFrameThresholdMs = 33;
        private const int SlowFrameLimit = 10;
        private const int FastFrameLimit = 4;
        private const double MaxSampleGapMs = 120;
        private const int TierHysteresis = 2;

        private static bool _isMonitoring;
        private static int _frameCount;
        private static int _slowFrameCount;
        private static int _goodSampleStreak;
        private static int _badSampleStreak;
        private static DateTime _lastFrameTime = DateTime.UtcNow;
        private static bool _warmupComplete;
        private static Window _attachedWindow;

        public static AnimationTier Tier { get; private set; } = AnimationTier.Full;

        public static event EventHandler TierChanged;

        /// <summary>
        /// 是否处于后台（窗口不可见或最小化）。
        /// </summary>
        public static bool IsBackground => Tier == AnimationTier.Background;

        /// <summary>
        /// 是否应简化页面过渡（高负载或刚从后台恢复时的保守档位）。
        /// </summary>
        public static bool ReduceMotion => Tier != AnimationTier.Full;

        /// <summary>
        /// 是否允许完整的页面交叉淡入、位移等动效。
        /// </summary>
        public static bool AllowRichTransitions => Tier == AnimationTier.Full;

        /// <summary>
        /// 是否允许首页实时刷新（时长每秒更新等）。
        /// </summary>
        public static bool AllowLiveUiUpdates => Tier != AnimationTier.Background;

        /// <summary>
        /// 是否允许跑马灯等非必要循环动画。
        /// </summary>
        public static bool AllowMarquee => Tier == AnimationTier.Full;

        public static void AttachWindow(Window window)
        {
            if (window == null)
            {
                return;
            }

            DetachWindow();

            _attachedWindow = window;
            window.Activated += Window_Activated;
            window.Deactivated += Window_Deactivated;
            window.StateChanged += Window_StateChanged;
            window.IsVisibleChanged += Window_IsVisibleChanged;
            window.Closed += Window_Closed;

            UpdateWindowAvailability();
        }

        public static void DetachWindow()
        {
            if (_attachedWindow == null)
            {
                return;
            }

            _attachedWindow.Activated -= Window_Activated;
            _attachedWindow.Deactivated -= Window_Deactivated;
            _attachedWindow.StateChanged -= Window_StateChanged;
            _attachedWindow.IsVisibleChanged -= Window_IsVisibleChanged;
            _attachedWindow.Closed -= Window_Closed;
            _attachedWindow = null;

            EnterBackgroundMode();
        }

        public static void StartMonitoring()
        {
            if (_isMonitoring)
            {
                return;
            }

            _isMonitoring = true;
            ResetSampling();
            CompositionTarget.Rendering += OnRendering;
        }

        public static void StopMonitoring()
        {
            if (!_isMonitoring)
            {
                return;
            }

            _isMonitoring = false;
            CompositionTarget.Rendering -= OnRendering;
        }

        private static void Window_Activated(object sender, EventArgs e)
        {
            UpdateWindowAvailability();
        }

        private static void Window_Deactivated(object sender, EventArgs e)
        {
            UpdateWindowAvailability();
        }

        private static void Window_StateChanged(object sender, EventArgs e)
        {
            UpdateWindowAvailability();
        }

        private static void Window_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            UpdateWindowAvailability();
        }

        private static void Window_Closed(object sender, EventArgs e)
        {
            DetachWindow();
        }

        private static void UpdateWindowAvailability()
        {
            if (_attachedWindow == null)
            {
                EnterBackgroundMode();
                return;
            }

            if (!_attachedWindow.IsVisible || _attachedWindow.WindowState == WindowState.Minimized)
            {
                EnterBackgroundMode();
                return;
            }

            EnterForegroundMode();
        }

        private static void EnterBackgroundMode()
        {
            StopMonitoring();
            ResetSampling();
            SetTier(AnimationTier.Background);
        }

        private static void EnterForegroundMode()
        {
            if (Tier == AnimationTier.Background)
            {
                //  从后台恢复时先用保守档位，采样稳定后再升到 Full。
                SetTier(AnimationTier.Reduced);
            }

            StartMonitoring();
        }

        private static void ResetSampling()
        {
            _frameCount = 0;
            _slowFrameCount = 0;
            _goodSampleStreak = 0;
            _badSampleStreak = 0;
            _warmupComplete = false;
            _lastFrameTime = DateTime.UtcNow;
        }

        private static void OnRendering(object sender, EventArgs e)
        {
            if (Tier == AnimationTier.Background)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var elapsedMs = (now - _lastFrameTime).TotalMilliseconds;
            _lastFrameTime = now;

            if (elapsedMs > 0.5 && elapsedMs < MaxSampleGapMs)
            {
                _frameCount++;
                if (elapsedMs > SlowFrameThresholdMs)
                {
                    _slowFrameCount++;
                }
            }

            if (!_warmupComplete)
            {
                if (_frameCount >= SampleWindow)
                {
                    _warmupComplete = true;
                    EvaluateSample();
                    _frameCount = 0;
                    _slowFrameCount = 0;
                }

                return;
            }

            if (_frameCount < SampleWindow)
            {
                return;
            }

            EvaluateSample();
            _frameCount = 0;
            _slowFrameCount = 0;
        }

        private static void EvaluateSample()
        {
            if (Tier == AnimationTier.Background)
            {
                return;
            }

            if (_slowFrameCount >= SlowFrameLimit)
            {
                _badSampleStreak++;
                _goodSampleStreak = 0;
                if (_badSampleStreak >= TierHysteresis && Tier == AnimationTier.Full)
                {
                    SetTier(AnimationTier.Reduced);
                }

                return;
            }

            if (_slowFrameCount <= FastFrameLimit)
            {
                _goodSampleStreak++;
                _badSampleStreak = 0;
                if (_goodSampleStreak >= TierHysteresis && Tier == AnimationTier.Reduced)
                {
                    SetTier(AnimationTier.Full);
                }

                return;
            }

            _goodSampleStreak = 0;
            _badSampleStreak = 0;
        }

        private static void SetTier(AnimationTier tier)
        {
            if (Tier == tier)
            {
                return;
            }

            Tier = tier;
            TierChanged?.Invoke(null, EventArgs.Empty);
        }
    }
}
