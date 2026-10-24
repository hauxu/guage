using Core.Models;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using UI.Base.Animation;
using UI.Controls.Input;
using UI.Controls.Select;
using UI.Controls.Toggle;
using UI.Models;
using UI.ViewModels;

namespace UI.Views
{
    public partial class TimeLimitPage : Page
    {
        private TimeLimitPageVM _vm;
        private bool _hasPlayedEntrance;
        private ScrollViewer _pageScrollViewer;
        private double _lastReportedScrollOffset = -1;
        private Panel _scrollTopOriginalParent;
        private int _scrollTopOriginalIndex = -1;
        private Thickness _scrollTopOriginalMargin;
        private HorizontalAlignment _scrollTopOriginalHorizontalAlignment;
        private VerticalAlignment _scrollTopOriginalVerticalAlignment;
        private int _scrollTopOriginalColumn;
        private int _scrollTopOriginalRowSpan;
        private int _scrollTopOriginalZIndex;

        private bool _vmEventsAttached;

        public TimeLimitPage(TimeLimitPageVM vm)
        {
            InitializeComponent();
            DataContext = vm;
            _vm = vm;

            AttachVmEvents();
            Unloaded += TimeLimitPage_Unloaded;
        }

        /// <summary>
        /// 订阅 VM 事件（幂等）。页面从缓存恢复时 Loaded 会重新触发，
        /// 必须在此重连——否则切走再回来后「添加应用」等事件无人处理。
        /// </summary>
        private void AttachVmEvents()
        {
            if (_vmEventsAttached || _vm == null)
            {
                return;
            }
            _vmEventsAttached = true;
            _vm.AppPickerOpening += Vm_AppPickerOpening;
            _vm.AppPickerClosing += Vm_AppPickerClosing;
            _vm.PickerLetterJumpRequested += Vm_PickerLetterJumpRequested;
        }

        /// <summary>
        /// 退订 VM 事件，避免 VM 与页面互相引用形成泄漏。
        /// </summary>
        private void DetachVmEvents()
        {
            if (!_vmEventsAttached || _vm == null)
            {
                return;
            }
            _vmEventsAttached = false;
            _vm.AppPickerOpening -= Vm_AppPickerOpening;
            _vm.AppPickerClosing -= Vm_AppPickerClosing;
            _vm.PickerLetterJumpRequested -= Vm_PickerLetterJumpRequested;
        }

        private void TimeLimitPage_Unloaded(object sender, RoutedEventArgs e)
        {
            DetachVmEvents();

            RestoreFloatingScrollTopButton();
            DetachPageScrollViewer();
            ForceClosePicker();
        }

        private void AttachPageScrollViewer()
        {
            DetachPageScrollViewer();
            _pageScrollViewer = FindOuterScrollViewer();
            if (_pageScrollViewer != null)
            {
                _pageScrollViewer.ScrollChanged += PageScrollViewer_ScrollChanged;
                UpdateScrollTopButtonVisibility(_pageScrollViewer.VerticalOffset);
            }
        }

        private void DetachPageScrollViewer()
        {
            if (_pageScrollViewer != null)
            {
                _pageScrollViewer.ScrollChanged -= PageScrollViewer_ScrollChanged;
                _pageScrollViewer = null;
            }
        }

        private ScrollViewer FindOuterScrollViewer()
        {
            DependencyObject current = this;
            while (current != null)
            {
                current = VisualTreeHelper.GetParent(current);
                if (current is ScrollViewer scrollViewer)
                {
                    return scrollViewer;
                }
            }

            return null;
        }

        private FrameworkElement GetContentPlacementTarget()
        {
            Window window = Window.GetWindow(this);
            if (window?.Content is Grid mainGrid)
            {
                return mainGrid.Children.OfType<Border>().FirstOrDefault(border => Grid.GetColumn(border) == 1);
            }

            return this;
        }

        private Grid GetMainWindowGrid()
        {
            return Window.GetWindow(this)?.Content as Grid;
        }

        private void AttachFloatingScrollTopButton()
        {
            if (!(ScrollTopFloat.Parent is Panel currentParent))
            {
                return;
            }

            Grid mainGrid = GetMainWindowGrid();
            Border contentBorder = GetContentPlacementTarget() as Border;
            if (mainGrid == null || contentBorder == null)
            {
                return;
            }

            if (ReferenceEquals(ScrollTopFloat.Parent, mainGrid))
            {
                UpdateScrollTopButtonVisibility(_pageScrollViewer?.VerticalOffset ?? 0);
                return;
            }

            StoreScrollTopPlacement(currentParent);
            currentParent.Children.Remove(ScrollTopFloat);

            Grid.SetColumn(ScrollTopFloat, Grid.GetColumn(contentBorder));
            Grid.SetRowSpan(ScrollTopFloat, 1);
            ScrollTopFloat.Margin = new Thickness(4, 4, 0, 0);
            ScrollTopFloat.HorizontalAlignment = HorizontalAlignment.Left;
            ScrollTopFloat.VerticalAlignment = VerticalAlignment.Top;
            Panel.SetZIndex(ScrollTopFloat, 600);

            if (!mainGrid.Children.Contains(ScrollTopFloat))
            {
                mainGrid.Children.Add(ScrollTopFloat);
            }

            UpdateScrollTopButtonVisibility(_pageScrollViewer?.VerticalOffset ?? 0);
        }

        private void StoreScrollTopPlacement(Panel parent)
        {
            _scrollTopOriginalParent = parent;
            _scrollTopOriginalIndex = parent.Children.IndexOf(ScrollTopFloat);
            _scrollTopOriginalMargin = ScrollTopFloat.Margin;
            _scrollTopOriginalHorizontalAlignment = ScrollTopFloat.HorizontalAlignment;
            _scrollTopOriginalVerticalAlignment = ScrollTopFloat.VerticalAlignment;
            _scrollTopOriginalColumn = Grid.GetColumn(ScrollTopFloat);
            _scrollTopOriginalRowSpan = Grid.GetRowSpan(ScrollTopFloat);
            _scrollTopOriginalZIndex = Panel.GetZIndex(ScrollTopFloat);
        }

        private void DetachScrollTopFloatFromCurrentParent()
        {
            if (ScrollTopFloat?.Parent is Panel parent)
            {
                parent.Children.Remove(ScrollTopFloat);
            }
        }

        private void RestoreFloatingScrollTopButton()
        {
            if (_scrollTopOriginalParent == null)
            {
                DetachScrollTopFloatFromCurrentParent();
                return;
            }

            DetachScrollTopFloatFromCurrentParent();

            Grid.SetColumn(ScrollTopFloat, _scrollTopOriginalColumn);
            Grid.SetRowSpan(ScrollTopFloat, _scrollTopOriginalRowSpan);
            ScrollTopFloat.Margin = _scrollTopOriginalMargin;
            ScrollTopFloat.HorizontalAlignment = _scrollTopOriginalHorizontalAlignment;
            ScrollTopFloat.VerticalAlignment = _scrollTopOriginalVerticalAlignment;
            Panel.SetZIndex(ScrollTopFloat, _scrollTopOriginalZIndex);

            if (!_scrollTopOriginalParent.Children.Contains(ScrollTopFloat))
            {
                if (_scrollTopOriginalIndex >= 0 && _scrollTopOriginalIndex <= _scrollTopOriginalParent.Children.Count)
                {
                    _scrollTopOriginalParent.Children.Insert(_scrollTopOriginalIndex, ScrollTopFloat);
                }
                else
                {
                    _scrollTopOriginalParent.Children.Add(ScrollTopFloat);
                }
            }

            _scrollTopOriginalParent = null;
            _scrollTopOriginalIndex = -1;
        }

        private System.Windows.Threading.DispatcherTimer _scrollAnimTimer;

        private void Vm_PickerLetterJumpRequested(object sender, string letter)
        {
            if (_vm == null || string.IsNullOrWhiteSpace(letter))
            {
                return;
            }

            PickerListItemVM target = _vm.FilteredPickerItems
                .FirstOrDefault(item => item.IsSectionHeader
                    && string.Equals(item.SectionLetter, letter, StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                return;
            }

            PickerItemsControl.UpdateLayout();
            ScrollPickerItemIntoView(target);
        }

        private void ScrollPickerItemIntoView(PickerListItemVM target)
        {
            if (target == null || PickerItemsControl == null || PickerListScrollViewer == null)
            {
                return;
            }

            PickerItemsControl.UpdateLayout();
            if (PickerItemsControl.ItemContainerGenerator.ContainerFromItem(target) is FrameworkElement element)
            {
                // 目标分节头相对视口的 Y（负值表示在视口上方）→ 平滑滚动到视口上部留白处
                double relativeY = element.TranslatePoint(new Point(0, 0), PickerListScrollViewer).Y;
                AnimateScrollTo(PickerListScrollViewer.VerticalOffset + relativeY - 8);
            }
        }

        /// <summary>
        /// 平滑滚动动画（AppleDecelerate 减速曲线，360ms），保证 A-Z 跳转丝滑。
        /// </summary>
        private void AnimateScrollTo(double targetOffset)
        {
            if (PickerListScrollViewer == null)
            {
                return;
            }
            targetOffset = Math.Max(0, Math.Min(targetOffset, PickerListScrollViewer.ScrollableHeight));

            if (_scrollAnimTimer != null)
            {
                _scrollAnimTimer.Stop();
                _scrollAnimTimer = null;
            }

            double from = PickerListScrollViewer.VerticalOffset;
            if (Math.Abs(targetOffset - from) < 1)
            {
                return;
            }

            const double durationMs = 360;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            _scrollAnimTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _scrollAnimTimer.Tick += (s, e) =>
            {
                double t = watch.ElapsedMilliseconds / durationMs;
                if (t >= 1.0)
                {
                    PickerListScrollViewer.ScrollToVerticalOffset(targetOffset);
                    _scrollAnimTimer.Stop();
                    _scrollAnimTimer = null;
                    return;
                }
                double eased = Motion.AppleDecelerate.Ease(t);
                PickerListScrollViewer.ScrollToVerticalOffset(from + (targetOffset - from) * eased);
            };
            _scrollAnimTimer.Start();
        }

        private void PageScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (Math.Abs(e.VerticalOffset - _lastReportedScrollOffset) < 4)
            {
                return;
            }

            _lastReportedScrollOffset = e.VerticalOffset;
            UpdateScrollTopButtonVisibility(e.VerticalOffset);
        }

        private void UpdateScrollTopButtonVisibility(double verticalOffset)
        {
            if (ScrollTopFloat == null)
            {
                return;
            }

            ScrollTopFloat.Visibility = verticalOffset > 80 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ScrollTopFloat_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _pageScrollViewer?.ScrollToVerticalOffset(0);
            e.Handled = true;
        }

        private void RuleCheckBox_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // 页面从缓存恢复时重连 VM 事件（Unloaded 已退订）
            AttachVmEvents();

            AttachPageScrollViewer();
            AttachFloatingScrollTopButton();

            if (_hasPlayedEntrance)
            {
                return;
            }

            _hasPlayedEntrance = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ElementMotion.PlayEntrance(HeaderPanel, 0);
                ElementMotion.PlayEntrance(ToggleCard, 1);
                ElementMotion.PlayEntrance(ToolbarPanel, 2);
                ElementMotion.PlayEntrance(RulesPanel, 3, 12);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Popup 渲染在独立 PopupRoot，不在窗口视觉树内：
        /// 打开后手动套用一次字体缩放，否则内部硬编码字号不随系统设置
        /// </summary>
        private void PickerPopup_Opened(object sender, EventArgs e)
        {
            UI.Base.UIFontScale.ApplyToRoot(PickerRoot);
        }

        private void Vm_AppPickerOpening(object sender, EventArgs e)
        {
            FrameworkElement target = GetContentPlacementTarget();
            PickerPopup.PlacementTarget = target;
            PickerPopup.Placement = PlacementMode.Center;

            if (target != null)
            {
                target.UpdateLayout();
                double width = target.ActualWidth > 0 ? target.ActualWidth : 560;
                double height = target.ActualHeight > 0 ? target.ActualHeight : 580;
                PickerOverlay.Width = width;
                PickerOverlay.Height = height;
            }

            ScrollTopFloat.Visibility = Visibility.Collapsed;
            PickerPopup.IsOpen = true;
            PickerPanel.Opacity = 0;
            PickerPanel.IsHitTestVisible = true;
            ElementMotion.PlayFadeScaleIn(PickerPanel);
        }

        private void Vm_AppPickerClosing(object sender, EventArgs e)
        {
            PickerPanel.IsHitTestVisible = false;
            ElementMotion.PlayFadeScaleOut(PickerPanel, ForceClosePicker);
        }

        private void ForceClosePicker()
        {
            PickerPopup.IsOpen = false;
            PickerPanel.Opacity = 0;
            PickerPanel.IsHitTestVisible = false;
            _vm?.HideAppPicker();
        }

        private void RuleCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (IsInteractiveChildClick(sender as DependencyObject, e.OriginalSource as DependencyObject))
            {
                return;
            }

            var border = sender as Border;
            var item = border?.DataContext as TimeLimitRuleItemVM;
            if (_vm == null || item == null)
            {
                return;
            }

            _vm.SelectRuleSingle(item);
            PlayCardSelectPulse(border);
        }

        private void AppRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            var listItem = border?.DataContext as PickerListItemVM;
            AppModel app = listItem?.App;
            if (_vm == null || app == null)
            {
                return;
            }

            _vm.SelectApp(app);
            _vm.RequestClosePicker();
            e.Handled = true;
        }

        private static bool IsInteractiveChildClick(DependencyObject card, DependencyObject source)
        {
            if (card == null || source == null)
            {
                return false;
            }

            DependencyObject current = source;
            while (current != null && !ReferenceEquals(current, card))
            {
                if (current is Toggle || current is InputBox || current is Select || current is TextBox || current is CheckBox)
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private static void PlayCardSelectPulse(Border border)
        {
            if (border == null)
            {
                return;
            }

            var scale = new ScaleTransform(1, 1);
            border.RenderTransform = scale;
            border.RenderTransformOrigin = new Point(0.5, 0.5);

            var shrink = new DoubleAnimation(1, 0.985, TimeSpan.FromSeconds(0.1))
            {
                EasingFunction = Motion.Accelerate,
                AutoReverse = true,
                FillBehavior = FillBehavior.Stop
            };
            shrink.Completed += (s, e) =>
            {
                border.RenderTransform = null;
            };

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink, HandoffBehavior.SnapshotAndReplace);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink, HandoffBehavior.SnapshotAndReplace);
        }
    }
}
