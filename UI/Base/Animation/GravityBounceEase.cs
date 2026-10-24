using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace UI.Base.Animation
{
    /// <summary>
    /// 重力下坠缓动（easeOutBounce 闭式解）：
    /// 前段按重力加速落向终点，触地后按恢复系数衰减反弹两次，
    /// 形状符合"自由落体 + 非完全弹性碰撞"的物理直觉。
    /// 与 SpringEase 一样是时长归一化的解析曲线（零帧开销），
    /// 可用于 C# BeginAnimation 与 XAML 资源。
    /// </summary>
    public class GravityBounceEase : EasingFunctionBase
    {
        /// <summary>
        /// 反弹恢复系数（0~1）：碰撞后速度保留比例。默认 0.32，
        /// 数值越大弹得越高、次数衰减越慢。
        /// </summary>
        public static readonly DependencyProperty RestitutionProperty =
            DependencyProperty.Register(
                "Restitution",
                typeof(double),
                typeof(GravityBounceEase),
                new PropertyMetadata(0.32));

        public double Restitution
        {
            get { return (double)GetValue(RestitutionProperty); }
            set { SetValue(RestitutionProperty, value); }
        }

        protected override Freezable CreateInstanceCore()
        {
            return new GravityBounceEase();
        }

        protected override double EaseInCore(double normalizedTime)
        {
            //  x=归一化时间，返回值=到终点的"剩余高度"：
            //  配合默认 EaseOut 模式（Ease = 1 - EaseInCore(1-t)）得到
            //  "加速逼近终点 + 衰减反弹"的进度曲线
            return Fall(normalizedTime);
        }

        /// <summary>
        /// 从高处下落的剩余高度：t=0 时为 1（在起点），t=1 时为 0（落定），
        /// 中途带两次衰减反弹（返回值短暂 >0 表示弹起离地）。
        /// </summary>
        private double Fall(double t)
        {
            double r = Math.Max(0.05, Math.Min(0.6, Restitution));

            //  把时间轴划分为：下落 55% + 反弹1 27% + 反弹2 18%
            if (t < 0.55)
            {
                //  自由落体：h = 1 - (t/0.55)²（匀加速，落到 0）
                double k = t / 0.55;
                return 1 - k * k;
            }
            if (t < 0.82)
            {
                //  第一次反弹：起跳高度 r，抛物线回落
                double k = (t - 0.55) / 0.27;
                return r * 4 * k * (1 - k);
            }
            //  第二次反弹：起跳高度 r²
            double k2 = (t - 0.82) / 0.18;
            return r * r * 4 * k2 * (1 - k2);
        }
    }
}
