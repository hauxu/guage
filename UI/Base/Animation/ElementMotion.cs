using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace UI.Base.Animation
{
    /// <summary>
    /// 动画期间的位图缓存与阴影控制，降低 WPF 逐帧重绘开销。
    /// </summary>
    public static class AnimationRuntime
    {
        public static void EnableTransitionCache(UIElement element)
        {
            if (element == null)
            {
                return;
            }

            RenderOptions.SetBitmapScalingMode(element, BitmapScalingMode.LowQuality);
            RenderOptions.SetCachingHint(element, CachingHint.Cache);
            element.CacheMode = new BitmapCache(1.0)
            {
                EnableClearType = false,
                SnapsToDevicePixels = true
            };
        }

        public static void DisableTransitionCache(UIElement element)
        {
            if (element == null)
            {
                return;
            }

            element.CacheMode = null;
            RenderOptions.SetCachingHint(element, CachingHint.Unspecified);
            RenderOptions.SetBitmapScalingMode(element, BitmapScalingMode.Unspecified);
        }

        /// <summary>
        /// 动画期间临时移除 DropShadowEffect，结束后恢复。
        /// </summary>
        public static void RestoreShadow(FrameworkElement element, Effect effect)
        {
            if (element != null && element.Effect == null)
            {
                element.Effect = effect;
            }
        }

        public static void ResetTransform(TranslateTransform translate, ScaleTransform scale = null)
        {
            if (translate != null)
            {
                translate.BeginAnimation(TranslateTransform.XProperty, null);
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                translate.X = 0;
                translate.Y = 0;
            }

            if (scale != null)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleX = 1;
                scale.ScaleY = 1;
            }
        }
    }

    /// <summary>
    /// 页面级元素的进出场动画，复用 Motion 的节奏参数。
    /// </summary>
    public static class ElementMotion
    {
        private const int StaggerMs = 40;

        public static void PlayEntrance(FrameworkElement target, int index = 0, double offsetY = 10)
        {
            if (target == null || !target.IsLoaded)
            {
                return;
            }

            if (AnimationBudget.IsBackground)
            {
                target.Opacity = 1;
                target.RenderTransform = null;
                return;
            }

            if (!AnimationBudget.AllowRichTransitions)
            {
                target.Opacity = 0;
                target.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Motion.Micro)
                {
                    EasingFunction = Motion.Standard
                });
                return;
            }

            target.BeginAnimation(UIElement.OpacityProperty, null);
            if (target.RenderTransform is TranslateTransform oldTranslate)
            {
                oldTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            }

            target.Opacity = 0;
            var translate = new TranslateTransform(0, offsetY);
            target.RenderTransform = translate;
            target.RenderTransformOrigin = new Point(0.5, 0);

            AnimationRuntime.EnableTransitionCache(target);

            var begin = TimeSpan.FromMilliseconds(Math.Max(0, index) * StaggerMs);
            //  位移用弹力曲线（越过终点再弹回）+ 轻微缩放，元素"蹦"进来而非滑进来
            var move = new DoubleAnimation(offsetY, 0, Motion.Normal)
            {
                BeginTime = begin,
                EasingFunction = Motion.Bouncy
            };
            move.Completed += (s, e) => AnimationRuntime.DisableTransitionCache(target);

            var popScale = new ScaleTransform(0.97, 0.97);
            var scaleGroup = new TransformGroup();
            scaleGroup.Children.Add(popScale);
            scaleGroup.Children.Add(translate);
            target.RenderTransform = scaleGroup;

            popScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.97, 1, Motion.Normal) { BeginTime = begin, EasingFunction = Motion.SoftSpring });
            popScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.97, 1, Motion.Normal) { BeginTime = begin, EasingFunction = Motion.SoftSpring });
            translate.BeginAnimation(TranslateTransform.YProperty, move);

            var fade = new DoubleAnimation(0, 1, Motion.Fast)
            {
                BeginTime = begin,
                EasingFunction = Motion.Standard
            };

            target.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        /// <summary>
        /// 下坠入场：元素从上方 offsetY 处受重力加速落下，触地反弹两次后落定
        /// （GravityBounceEase 物理下坠曲线），适合通知条、徽章一类"从上面掉下来"的元素。
        /// </summary>
        public static void PlayDropIn(FrameworkElement target, int index = 0, double offsetY = 26)
        {
            if (target == null || !target.IsLoaded)
            {
                return;
            }

            if (AnimationBudget.IsBackground)
            {
                target.Opacity = 1;
                target.RenderTransform = null;
                return;
            }

            if (!AnimationBudget.AllowRichTransitions)
            {
                target.Opacity = 0;
                target.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Motion.Micro)
                {
                    EasingFunction = Motion.Standard
                });
                return;
            }

            target.BeginAnimation(UIElement.OpacityProperty, null);
            if (target.RenderTransform is TranslateTransform oldTranslate)
            {
                oldTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            }

            target.Opacity = 0;
            var translate = new TranslateTransform(-offsetY, 0);
            target.RenderTransform = translate;
            target.RenderTransformOrigin = new Point(0.5, 0);

            AnimationRuntime.EnableTransitionCache(target);

            var begin = TimeSpan.FromMilliseconds(Math.Max(0, index) * StaggerMs);
            //  下坠时间略长于常规位移（重力下落需要路程），落地反弹由曲线承担
            var drop = new DoubleAnimation(-offsetY, 0, Motion.Slow)
            {
                BeginTime = begin,
                EasingFunction = Motion.Drop
            };
            drop.Completed += (s, e) => AnimationRuntime.DisableTransitionCache(target);

            translate.BeginAnimation(TranslateTransform.YProperty, drop);

            var fade = new DoubleAnimation(0, 1, Motion.Fast)
            {
                BeginTime = begin,
                EasingFunction = Motion.Standard
            };
            target.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        /// <summary>
        /// 图表元素生长入场：缩放 0.4→1 + 淡入（Pop 弹簧），delayMs 用于逐项错峰。
        /// </summary>
        public static void PlayGrowIn(FrameworkElement target, double delayMs = 0)
        {
            if (target == null || !target.IsLoaded)
            {
                return;
            }

            if (AnimationBudget.IsBackground)
            {
                target.Opacity = 1;
                target.RenderTransform = null;
                return;
            }

            if (!AnimationBudget.AllowRichTransitions)
            {
                target.Opacity = 0;
                target.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, 1, Motion.Micro) { EasingFunction = Motion.Standard });
                return;
            }

            target.BeginAnimation(UIElement.OpacityProperty, null);

            var scale = new ScaleTransform(0.4, 0.4);
            target.RenderTransform = scale;
            target.RenderTransformOrigin = new Point(0.5, 0.5);

            var begin = TimeSpan.FromMilliseconds(Math.Max(0, delayMs));

            var spring = new SpringEase { Bounce = 0.5, EasingMode = EasingMode.EaseOut };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.4, 1, Motion.Normal) { BeginTime = begin, EasingFunction = spring });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.4, 1, Motion.Normal) { BeginTime = begin, EasingFunction = spring });

            target.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, Motion.Fast) { BeginTime = begin, EasingFunction = Motion.Standard });
        }

        public static void PlayFadeScaleIn(FrameworkElement target, Action onCompleted = null)
        {
            if (target == null)
            {
                return;
            }

            if (AnimationBudget.IsBackground)
            {
                target.Visibility = Visibility.Visible;
                target.IsHitTestVisible = true;
                target.Opacity = 1;
                target.RenderTransform = null;
                onCompleted?.Invoke();
                return;
            }

            if (!AnimationBudget.AllowRichTransitions)
            {
                target.Visibility = Visibility.Visible;
                target.IsHitTestVisible = true;
                target.Opacity = 0;
                var quickFadeAnim = new DoubleAnimation(0, 1, Motion.Micro) { EasingFunction = Motion.Standard };
                quickFadeAnim.Completed += (s, e) => onCompleted?.Invoke();
                target.BeginAnimation(UIElement.OpacityProperty, quickFadeAnim);
                return;
            }

            target.Visibility = Visibility.Visible;
            target.IsHitTestVisible = true;
            target.Opacity = 0;

            var scale = new ScaleTransform(0.94, 0.94);
            target.RenderTransform = scale;
            target.RenderTransformOrigin = new Point(0.5, 0.5);

            Effect savedEffect = target.Effect;
            target.Effect = null;
            AnimationRuntime.EnableTransitionCache(target);

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94, 1, Motion.Normal) { EasingFunction = Motion.Pop });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, 1, Motion.Normal) { EasingFunction = Motion.Pop });

            var opacityAnim = new DoubleAnimation(0, 1, Motion.Fast) { EasingFunction = Motion.Standard };
            opacityAnim.Completed += (s, e) =>
            {
                AnimationRuntime.DisableTransitionCache(target);
                AnimationRuntime.RestoreShadow(target, savedEffect);
                onCompleted?.Invoke();
            };
            target.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
        }

        public static void PlayFadeScaleOut(FrameworkElement target, Action onCompleted = null)
        {
            if (target == null)
            {
                onCompleted?.Invoke();
                return;
            }

            target.IsHitTestVisible = false;
            Effect savedEffect = target.Effect;
            target.Effect = null;
            AnimationRuntime.EnableTransitionCache(target);

            var scale = target.RenderTransform as ScaleTransform ?? new ScaleTransform(1, 1);
            target.RenderTransform = scale;
            target.RenderTransformOrigin = new Point(0.5, 0.5);

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.98, Motion.Fast) { EasingFunction = Motion.Accelerate });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.98, Motion.Fast) { EasingFunction = Motion.Accelerate });

            var opacityAnim = new DoubleAnimation(1, 0, Motion.Fast) { EasingFunction = Motion.Accelerate };
            opacityAnim.Completed += (s, e) =>
            {
                target.Visibility = Visibility.Collapsed;
                AnimationRuntime.DisableTransitionCache(target);
                AnimationRuntime.RestoreShadow(target, savedEffect);
                onCompleted?.Invoke();
            };
            target.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
        }

        public static void PlayBackdropIn(UIElement target)
        {
            if (target == null)
            {
                return;
            }

            target.Opacity = 0;
            target.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 0.55, Motion.Fast) { EasingFunction = Motion.Standard });
        }

        public static void PlayBackdropOut(UIElement target, Action onCompleted = null)
        {
            if (target == null)
            {
                onCompleted?.Invoke();
                return;
            }

            var anim = new DoubleAnimation(target.Opacity, 0, Motion.Fast) { EasingFunction = Motion.Accelerate };
            anim.Completed += (s, e) => onCompleted?.Invoke();
            target.BeginAnimation(UIElement.OpacityProperty, anim);
        }
    }
}
