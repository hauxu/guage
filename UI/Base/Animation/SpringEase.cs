using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace UI.Base.Animation
{
    /// <summary>
    /// 阻尼谐振子缓动函数，按物理方程模拟弹簧趋向平衡位置的运动。
    ///
    /// 只暴露"弹跳量"一个参数，刚度由它自动推导，使弹簧恰好在动画时长结束时收敛。
    /// 这样运动会均匀铺满整个时长，不会出现前段瞬间冲到位、后段全是不可见沉降的情况。
    /// 需配合 EasingMode.EaseOut 使用。
    /// </summary>
    public class SpringEase : EasingFunctionBase
    {
        /// <summary>
        /// 弹跳量，取值 0 到 1。0 为临界阻尼（不过冲），数值越大回弹越明显。
        /// 0.3 约对应 4.6% 过冲，是较为通用的取值。
        /// </summary>
        public double Bounce
        {
            get { return (double)GetValue(BounceProperty); }
            set { SetValue(BounceProperty, value); }
        }
        public static readonly DependencyProperty BounceProperty =
            DependencyProperty.Register("Bounce", typeof(double), typeof(SpringEase), new PropertyMetadata(0.3));

        /// <summary>
        /// 衰减包络在单位时长内需要跨越的对数衰减量。取 4.5 时振幅衰减到约 1%，
        /// 即动画结束时刚好肉眼不可见地静止。
        /// </summary>
        private const double SettlingDecay = 4.5;

        protected override double EaseInCore(double normalizedTime)
        {
            //  基类在 EaseOut 模式下计算 1 - EaseInCore(1 - t)，此处做同样的反演，
            //  使 EaseOut 得到的就是弹簧的阶跃响应本身。
            return 1.0 - NormalizedResponse(1.0 - normalizedTime);
        }

        /// <summary>
        /// 归一化后的阶跃响应，保证终点严格落在 1，避免动画结束瞬间发生跳变。
        /// </summary>
        private double NormalizedResponse(double t)
        {
            double zeta = DampingRatio;
            double omega = SettlingDecay / zeta;

            double settled = StepResponse(1.0, zeta, omega);
            if (Math.Abs(settled) < 1e-6)
            {
                return t;
            }
            return StepResponse(t, zeta, omega) / settled;
        }

        private double DampingRatio
        {
            get
            {
                //  下限避免刚度趋于无穷，上限保留临界阻尼
                double bounce = Math.Min(0.9, Math.Max(0.0, Bounce));
                return 1.0 - bounce;
            }
        }

        /// <summary>
        /// 二阶阻尼系统对单位阶跃输入的位移响应。
        /// </summary>
        private static double StepResponse(double t, double zeta, double omega)
        {
            if (zeta < 1.0)
            {
                //  欠阻尼：衰减的正弦振荡
                double omegaD = omega * Math.Sqrt(1.0 - zeta * zeta);
                return 1.0 - Math.Exp(-zeta * omega * t)
                    * (Math.Cos(omegaD * t) + zeta * omega / omegaD * Math.Sin(omegaD * t));
            }

            //  临界阻尼
            return 1.0 - Math.Exp(-omega * t) * (1.0 + omega * t);
        }

        protected override Freezable CreateInstanceCore()
        {
            return new SpringEase();
        }
    }
}
