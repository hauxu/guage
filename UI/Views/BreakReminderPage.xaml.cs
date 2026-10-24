using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using UI.Base.Animation;
using UI.Servicers;
using UI.ViewModels;

namespace UI.Views
{
    /// <summary>
    /// 休息提醒页面：倒计时视觉为"一柱香"（IncenseBurnControl，与主窗口背景共用同一控件）。
    /// 页面只负责：VM 状态 → 控件 Progress/IsBurning/IsPaused、阶段配色、
    /// 徽章/倒计时数字动效、点火/燃尽事件转调控件特效、环境辉光。
    /// </summary>
    public partial class BreakReminderPage : Page
    {
        private readonly BreakReminderPageVM _vm;
        private readonly IThemeServicer _themeServicer;
        private bool _hasPlayedEntrance;
        private SolidColorBrush _badgeBrush;
        private RadialGradientBrush _ambientGlowBrush;
        private bool _vmEventsAttached;
        private bool _themeAttached;

        public BreakReminderPage(BreakReminderPageVM vm, IThemeServicer themeServicer)
        {
            InitializeComponent();

            DataContext = vm;
            _vm = vm;
            _themeServicer = themeServicer;

            Unloaded += Page_Unloaded;
        }

        /// <summary>
        /// 订阅 VM 事件（幂等）。页面从缓存恢复时 Loaded 会重新触发，
        /// 必须在此重连——否则切走再回来后香体/数字/阶段过渡全部失效。
        /// </summary>
        private void AttachVmEvents()
        {
            if (_vmEventsAttached || _vm == null)
            {
                return;
            }
            _vmEventsAttached = true;
            _vm.PropertyChanged += Vm_PropertyChanged;
            _vm.PhaseCompletedCelebration += Vm_PhaseCompletedCelebration;
            _vm.PhaseStartedIgnition += Vm_PhaseStartedIgnition;
        }

        private void DetachVmEvents()
        {
            if (!_vmEventsAttached || _vm == null)
            {
                return;
            }
            _vmEventsAttached = false;
            _vm.PropertyChanged -= Vm_PropertyChanged;
            _vm.PhaseCompletedCelebration -= Vm_PhaseCompletedCelebration;
            _vm.PhaseStartedIgnition -= Vm_PhaseStartedIgnition;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // 主题切换时刷新香烛配色（幂等：页面缓存复用时 Loaded 会重复触发）
            if (_themeServicer != null && !_themeAttached)
            {
                _themeAttached = true;
                _themeServicer.OnThemeChanged += ThemeServicer_OnThemeChanged;
            }

            // 页面从缓存恢复时重连 VM 事件（Unloaded 已退订）
            AttachVmEvents();
            _vm.StartUiTimer();

            //  动画预算档位变化时同步香烛帧循环：降档停循环（保静态香体），
            //  升回 Full 按 IsTiming 重启——否则降档期间启动计时会导致香全灭且不恢复
            AnimationBudget.TierChanged -= AnimationBudget_TierChanged;
            AnimationBudget.TierChanged += AnimationBudget_TierChanged;

            _badgeBrush = new SolidColorBrush(GetPhaseColor(_vm.PhaseKey));
            PhaseBadge.Background = _badgeBrush;
            InitAmbientGlow();
            SyncIncenseState();

            if (!_hasPlayedEntrance)
            {
                _hasPlayedEntrance = true;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ElementMotion.PlayEntrance(HeaderPanel, 0);
                    ElementMotion.PlayEntrance(DurationCards, 1);
                    ElementMotion.PlayEntrance(TimerCard, 2, 12);
                    ElementMotion.PlayEntrance(RecordsCard, 3, 12);
                }), DispatcherPriority.Loaded);
            }
        }

        private void WorkRingTossEntry_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _vm?.OpenRingTossCommand.Execute("work");
            e.Handled = true;
        }

        private void BreakRingTossEntry_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _vm?.OpenRingTossCommand.Execute("break");
            e.Handled = true;
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_themeServicer != null && _themeAttached)
            {
                _themeAttached = false;
                _themeServicer.OnThemeChanged -= ThemeServicer_OnThemeChanged;
            }
            AnimationBudget.TierChanged -= AnimationBudget_TierChanged;
            DetachVmEvents();
            // 页面切走（缓存）时停掉控件帧循环；恢复时 SyncIncenseState 会按计时状态重启
            if (IncenseBurn != null)
            {
                IncenseBurn.IsBurning = false;
            }
            _vm.StopUiTimer();
        }

        private void AnimationBudget_TierChanged(object sender, EventArgs e)
        {
            //  升回 Full 且计时中：重启控件燃烧帧循环（降档期间循环被停掉后不会自启）；
            //  DP 同值赋值不触发回调，必须先 false 再 true 强制走 Stop/Start
            if (AnimationBudget.Tier == AnimationTier.Full && _vm.IsTiming && IncenseBurn != null)
            {
                IncenseBurn.IsBurning = false;
                IncenseBurn.IsBurning = true;
            }
        }

        private void ThemeServicer_OnThemeChanged(object sender, EventArgs e)
        {
            if (_badgeBrush != null)
            {
                _badgeBrush.Color = GetPhaseColor(_vm.PhaseKey);
            }
            ApplyPhaseColors(GetPhaseColor(_vm.PhaseKey));
        }

        /// <summary>
        /// 把 VM 计时状态一次性同步进控件：
        /// 空闲（未计时未暂停）显示满香——VM 的 Progress 空闲时恒为 0（Remaining=0），
        /// 直接透传会把香画成"烧完了"，与"还没开始"的现实不符。
        /// </summary>
        private void SyncIncenseState()
        {
            if (IncenseBurn == null)
            {
                return;
            }
            bool active = _vm.IsTiming || _vm.IsPaused;
            IncenseBurn.Progress = active ? _vm.Progress : 1.0;
            IncenseBurn.IsPaused = _vm.IsPaused;
            IncenseBurn.IsBurning = _vm.IsTiming;
            IncenseBurn.ApplyPhaseColor(GetPhaseColor(_vm.PhaseKey));
        }

        private void Vm_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(BreakReminderPageVM.Progress):
                    // 燃烧比例跟随 VM（控件帧循环里另有平滑与特效）
                    if (IncenseBurn != null)
                    {
                        bool active = _vm.IsTiming || _vm.IsPaused;
                        IncenseBurn.Progress = active ? _vm.Progress : 1.0;
                    }
                    break;
                case nameof(BreakReminderPageVM.PhaseKey):
                    OnPhaseChanged();
                    break;
                case nameof(BreakReminderPageVM.RemainingText):
                    PulseRemaining();
                    break;
                case nameof(BreakReminderPageVM.IsTiming):
                    SyncIncenseState();
                    break;
                case nameof(BreakReminderPageVM.IsPaused):
                    if (IncenseBurn != null)
                    {
                        IncenseBurn.IsPaused = _vm.IsPaused;
                    }
                    break;
            }
        }

        /// <summary>
        /// 阶段切换：徽章颜色过渡 + 倒计时数字弹性缩放入场 + 香烛换色
        /// </summary>
        private void OnPhaseChanged()
        {
            Color target = GetPhaseColor(_vm.PhaseKey);

            if (_badgeBrush != null)
            {
                if (AnimationBudget.ReduceMotion)
                {
                    _badgeBrush.Color = target;
                }
                else
                {
                    _badgeBrush.BeginAnimation(SolidColorBrush.ColorProperty,
                        new ColorAnimation(_badgeBrush.Color, target, Motion.Fast) { EasingFunction = Motion.Standard });
                }
            }

            ApplyPhaseColors(target);

            // 倒计时数字弹性缩放 + 淡入
            if (AnimationBudget.ReduceMotion)
            {
                RemainingTextBlock.Opacity = 1;
                RemainingTextBlock.RenderTransform = null;
                return;
            }

            var scale = new ScaleTransform(1.06, 1.06);
            RemainingTextBlock.RenderTransform = scale;
            RemainingTextBlock.RenderTransformOrigin = new Point(0.5, 0.5);
            RemainingTextBlock.Opacity = 0;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(1.06, 1, Motion.Normal) { EasingFunction = Motion.SoftSpring });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(1.06, 1, Motion.Normal) { EasingFunction = Motion.SoftSpring });
            RemainingTextBlock.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, Motion.Fast) { EasingFunction = Motion.Standard });
        }

        /// <summary>
        /// 每秒倒计时刷新时数字轻微淡入脉冲（苹果计时器跳动感）
        /// </summary>
        private void PulseRemaining()
        {
            if (AnimationBudget.ReduceMotion)
            {
                RemainingTextBlock.Opacity = 1;
                return;
            }
            RemainingTextBlock.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0.55, 1, Motion.Fast) { EasingFunction = Motion.Standard });
        }

        // ================================================================
        // 点香 / 燃尽 动效（转调控件特效）
        // ================================================================

        /// <summary>点香：控件内火光绽放 + 白热爆闪 + 烟涌</summary>
        private void Vm_PhaseStartedIgnition(object sender, EventArgs e)
        {
            IncenseBurn?.PlayIgnition();
        }

        /// <summary>阶段燃尽：控件内火星爆闪 + 灰烬散落（区别于手动停止）</summary>
        private void Vm_PhaseCompletedCelebration(object sender, EventArgs e)
        {
            IncenseBurn?.PlayBurnout();
        }

        // ================================================================
        // 环境辉光与配色
        // ================================================================

        /// <summary>
        /// 环境辉光：香头后方的暖色径向光晕（渐变实现，不用位图模糊特效）
        /// </summary>
        private void InitAmbientGlow()
        {
            if (AmbientGlow == null)
            {
                return;
            }

            Color c = GetPhaseColor(_vm.PhaseKey);
            _ambientGlowBrush = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5),
                GradientOrigin = new Point(0.5, 0.5)
            };
            _ambientGlowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x66, c.R, c.G, c.B), 0));
            _ambientGlowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, c.R, c.G, c.B), 1));
            AmbientGlow.Fill = _ambientGlowBrush;
        }

        /// <summary>
        /// 把阶段色应用到控件燃点与环境辉光（颜色只在阶段切换/主题切换时更新）
        /// </summary>
        private void ApplyPhaseColors(Color c)
        {
            IncenseBurn?.ApplyPhaseColor(c);
            //  防御：冻结画刷只读，直接改 Color 会抛 InvalidOperationException（曾致闪退）
            if (_ambientGlowBrush != null && !_ambientGlowBrush.IsFrozen)
            {
                _ambientGlowBrush.GradientStops[0].Color = Color.FromArgb(0x66, c.R, c.G, c.B);
            }
        }

        /// <summary>
        /// 阶段配色：空闲灰 / 工作主题色 / 休息苹果绿
        /// </summary>
        private Color GetPhaseColor(string phaseKey)
        {
            switch (phaseKey)
            {
                case "Work":
                    return GetThemeColor();
                case "Break":
                    return Color.FromRgb(0x34, 0xC7, 0x59);
                default:
                    return Color.FromRgb(0xB4, 0xB4, 0xB8);
            }
        }

        private Color GetThemeColor()
        {
            var brush = TryFindResource("ThemeBrush") as SolidColorBrush;
            return brush?.Color ?? Color.FromRgb(0x2B, 0x20, 0xD9);
        }
    }
}
