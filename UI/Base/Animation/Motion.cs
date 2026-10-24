using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace UI.Base.Animation
{
    /// <summary>
    /// 全局动效参数。控件动画的时长与缓动曲线统一从这里取，避免各处硬编码导致整体节奏不一致。
    /// </summary>
    public static class Motion
    {
        //  时长档位（秒）— 参考 iOS 动效节奏：短促响应 + 自然减速收尾
        public const double MicroSeconds = 0.12;
        public const double FastSeconds = 0.2;
        public const double NormalSeconds = 0.32;
        public const double SlowSeconds = 0.45;
        public const double CrossFadeSeconds = 0.24;
        public const double CrossFadeOffsetY = 8;

        /// <summary>
        /// 极短反馈，用于按下、选中态切换。
        /// </summary>
        public static Duration Micro { get { return Seconds(MicroSeconds); } }

        /// <summary>
        /// 微交互时长，用于悬停高亮、按下反馈这类需要即时响应的变化。
        /// </summary>
        public static Duration Fast { get { return Seconds(FastSeconds); } }

        /// <summary>
        /// 常规时长，用于透明度、颜色过渡。
        /// </summary>
        public static Duration Normal { get { return Seconds(NormalSeconds); } }

        /// <summary>
        /// 强调时长，用于位移、形变一类幅度较大的变化。
        /// </summary>
        public static Duration Slow { get { return Seconds(SlowSeconds); } }

        /// <summary>
        /// 同级页面切换（如主导航 Tab）使用的交叉淡入时长。
        /// </summary>
        public static Duration CrossFade { get { return Seconds(CrossFadeSeconds); } }

        public static Duration Seconds(double seconds)
        {
            return new Duration(TimeSpan.FromSeconds(seconds));
        }

        /// <summary>
        /// 通用减速曲线。快速起步后平缓收尾，是界面元素进场与位移的默认选择。
        /// </summary>
        public static readonly IEasingFunction Decelerate = Freeze(new QuinticEase { EasingMode = EasingMode.EaseOut });

        /// <summary>
        /// 更接近 iOS 的减速曲线：前段灵敏、后段柔和吸附。
        /// </summary>
        public static readonly IEasingFunction AppleDecelerate = Freeze(new PowerEase
        {
            EasingMode = EasingMode.EaseOut,
            Power = 3
        });

        /// <summary>
        /// 温和的减速曲线，用于透明度、颜色一类不需要强烈动势的属性。
        /// </summary>
        public static readonly IEasingFunction Standard = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });

        /// <summary>
        /// 加速曲线。用于元素退场，让其"被带走"而不是慢慢消失。
        /// </summary>
        public static readonly IEasingFunction Accelerate = Freeze(new CubicEase { EasingMode = EasingMode.EaseIn });

        /// <summary>
        /// 弹性曲线，约 4.6% 过冲。用于选中标记一类需要物理质感的位移。
        /// </summary>
        public static readonly IEasingFunction Spring = Freeze(new SpringEase
        {
            Bounce = 0.22,
            EasingMode = EasingMode.EaseOut
        });

        /// <summary>
        /// 几乎不过冲的弹性曲线，用于形变恢复一类不应该左右摇摆的属性。
        /// </summary>
        public static readonly IEasingFunction SoftSpring = Freeze(new SpringEase
        {
            Bounce = 0.08,
            EasingMode = EasingMode.EaseOut
        });

        /// <summary>
        /// 明显过冲的弹力曲线（约 8% 过冲）：进场位移越过终点再弹回，带来活力感。
        /// </summary>
        public static readonly IEasingFunction Bouncy = Freeze(new SpringEase
        {
            Bounce = 0.42,
            EasingMode = EasingMode.EaseOut
        });

        /// <summary>
        /// 更强的弹出曲线（约 11% 过冲）：缩放弹出、图标/卡片生长。
        /// </summary>
        public static readonly IEasingFunction Pop = Freeze(new SpringEase
        {
            Bounce = 0.5,
            EasingMode = EasingMode.EaseOut
        });

        /// <summary>
        /// 重力下坠：加速下落 + 两次衰减反弹（easeOutBounce 闭式解）。
        /// 用于通知从顶部掉落、元素坠落入场等"受重力"的运动。
        /// </summary>
        public static readonly IEasingFunction Drop = Freeze(new GravityBounceEase());

        //  位移时长的计算参数
        private const double DistanceMinSeconds = 0.28;
        private const double DistanceMaxSeconds = 0.5;
        private const double SecondsPerPixel = 0.001;

        /// <summary>
        /// 根据位移距离推算动画时长。距离越远耗时越长，但设有上下限，
        /// 避免短距离拖沓、长距离又快得失去重量感。
        /// </summary>
        public static Duration ForDistance(double distance)
        {
            double seconds = DistanceMinSeconds + Math.Abs(distance) * SecondsPerPixel;
            return Seconds(Math.Min(DistanceMaxSeconds, seconds));
        }

        //  挤压拉伸的计算参数
        private const double StretchReferenceDistance = 180.0;
        private const double MaxStretch = 0.22;

        /// <summary>
        /// 根据位移距离推算挤压拉伸幅度。软体在加速时沿运动方向拉长，
        /// 位移越大速度越高、形变越明显。返回值为超出原尺寸的比例。
        /// </summary>
        public static double StretchForDistance(double distance)
        {
            double ratio = Math.Abs(distance) / StretchReferenceDistance;
            return Math.Min(MaxStretch, ratio * MaxStretch);
        }

        private static IEasingFunction Freeze(EasingFunctionBase easing)
        {
            easing.Freeze();
            return easing;
        }
    }
}
