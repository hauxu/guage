using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using UI.Base.Animation;
using UI.Controls.Navigation.Models;

namespace UI.Controls.Navigation
{
    public class Navigation : Control
    {
        public ContextMenu ItemContextMenu
        {
            get { return (ContextMenu)GetValue(ItemContextMenuProperty); }
            set { SetValue(ItemContextMenuProperty, value); }
        }
        public static readonly DependencyProperty ItemContextMenuProperty =
            DependencyProperty.Register("ItemContextMenu", typeof(ContextMenu), typeof(Navigation));

        public NavigationItemModel SelectedItem
        {
            get { return (NavigationItemModel)GetValue(SelectedItemProperty); }
            set { SetValue(SelectedItemProperty, value); }
        }
        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register("SelectedItem", typeof(NavigationItemModel), typeof(Navigation), new PropertyMetadata(new PropertyChangedCallback(OnSelectedItemChanged)));

        private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = d as Navigation;
            if (e.NewValue != e.OldValue)
            {
                var newItem = e.NewValue as NavigationItemModel;
                var oldItem = e.OldValue as NavigationItemModel;
                if (newItem != null && control.ItemsDictionary.ContainsKey(newItem.ID))
                {
                    control.ItemsDictionary[newItem.ID].IsSelected = true;
                }
                if (oldItem != null && control.ItemsDictionary.ContainsKey(oldItem.ID))
                {
                    control.ItemsDictionary[oldItem.ID].IsSelected = false;
                }
                control.ScrollToActive();
            }
        }

        public bool IsShowNavigation
        {
            get { return (bool)GetValue(IsShowNavigationProperty); }
            set { SetValue(IsShowNavigationProperty, value); }
        }
        public static readonly DependencyProperty IsShowNavigationProperty =
            DependencyProperty.Register("IsShowNavigation", typeof(bool), typeof(Navigation), new PropertyMetadata(true, new PropertyChangedCallback(OnIsShowNavigationChanged)));

        private static void OnIsShowNavigationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = d as Navigation;
            if (e.NewValue != e.OldValue)
            {
                if (control.IsShowNavigation && control.Visibility == Visibility.Collapsed && control.WindowWidth > 700)
                {
                    control.Visibility = Visibility.Visible;
                }
                if (!control.IsShowNavigation && control.Visibility == Visibility.Visible)
                {
                    control.Visibility = Visibility.Collapsed;
                }
            }
        }

        public object TopExtContent
        {
            get { return (object)GetValue(TopExtContentProperty); }
            set { SetValue(TopExtContentProperty, value); }
        }
        public static readonly DependencyProperty TopExtContentProperty =
            DependencyProperty.Register("TopExtContent", typeof(object), typeof(Navigation));
        public object BottomExtContent
        {
            get { return (object)GetValue(BottomExtContentProperty); }
            set { SetValue(BottomExtContentProperty, value); }
        }
        public static readonly DependencyProperty BottomExtContentProperty =
            DependencyProperty.Register("BottomExtContent", typeof(object), typeof(Navigation));
        public ObservableCollection<NavigationItemModel> Data
        {
            get
            {
                return (ObservableCollection<NavigationItemModel>)GetValue(DataProperty);
            }
            set
            {
                SetValue(DataProperty, value);
            }
        }
        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register("Data", typeof(ObservableCollection<NavigationItemModel>), typeof(Navigation), new PropertyMetadata(new PropertyChangedCallback(OnDataChanged)));

        private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = d as Navigation;
            if (e.NewValue != e.OldValue)
            {
                //  先退订旧集合：否则旧集合持有控件引用并继续收到事件（泄漏）
                if (e.OldValue is ObservableCollection<NavigationItemModel> oldCollection)
                {
                    oldCollection.CollectionChanged -= control.Data_CollectionChanged;
                }

                if (control.Data != null)
                {
                    control.Data.CollectionChanged -= control.Data_CollectionChanged;
                    control.Data.CollectionChanged += control.Data_CollectionChanged;

                    foreach (var item in control.Data)
                    {
                        control.AddItem(item);
                    }
                }
            }
        }

        public double WindowWidth
        {
            get
            {
                return (double)GetValue(WindowWidthProperty);
            }
            set
            {
                SetValue(WindowWidthProperty, value);
            }
        }
        public static readonly DependencyProperty WindowWidthProperty =
            DependencyProperty.Register("WindowWidth", typeof(double), typeof(Navigation), new PropertyMetadata(new PropertyChangedCallback(OnWindowWidthChanged)));

        private static void OnWindowWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = d as Navigation;
            if (control.IsShowNavigation)
            {
                //  滞回带：折叠阈值 700、恢复阈值 720——宽度在阈值附近徘徊时
                //  避免 Collapse/Visible 反复触发全窗重排
                if ((double)e.NewValue <= 700)
                {
                    control.Visibility = Visibility.Collapsed;
                }
                else if ((double)e.NewValue >= 720)
                {
                    control.Visibility = Visibility.Visible;
                }
            }
        }


        #region items property


        //protected static PropertyChangedCallback ItemsPropertyChangedCallback = new PropertyChangedCallback(ItemsPropertyChanged);

        //public static DependencyProperty ItemsProperty = DependencyProperty.RegisterAttached("Items", typeof(ObservableCollection<NavigationItemModel>), typeof(Navigation), new PropertyMetadata(null, ItemsPropertyChangedCallback));

        //private static void ItemsPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        //{
        //    var control = (Navigation)sender;
        //    if (control == null)
        //    {
        //        return;
        //    }
        //    control.UnregisterItems(e.OldValue as ObservableCollection<NavigationItemModel>);
        //    control.RegisterItems(e.NewValue as ObservableCollection<NavigationItemModel>);
        //}

        //public ObservableCollection<NavigationItemModel> Items
        //{
        //    get
        //    {
        //        return (ObservableCollection<NavigationItemModel>)GetValue(ItemsProperty);
        //    }
        //    set
        //    {
        //        SetValue(ItemsProperty, value);
        //    }
        //}

        //protected void UnregisterItems(ObservableCollection<NavigationItemModel> items)
        //{
        //    if (items == null)
        //    {
        //        return;
        //    }
        //    items.CollectionChanged -= ItemsChanged;
        //}

        //protected void RegisterItems(ObservableCollection<NavigationItemModel> items)
        //{
        //    if (items == null)
        //    {
        //        return;
        //    }
        //    items.CollectionChanged += ItemsChanged;
        //}

        //protected virtual void ItemsChanged(object sender, NotifyCollectionChangedEventArgs e)
        //{
        //    if (e.Action == NotifyCollectionChangedAction.Add)
        //    {
        //        foreach (var item in e.NewItems)
        //        {
        //            var data = item as NavigationItemModel;
        //            AddItem(data);
        //        }
        //    }
        //    else if (e.Action == NotifyCollectionChangedAction.Remove)
        //    {
        //        foreach (var item in e.OldItems)
        //        {
        //            var data = item as NavigationItemModel;
        //            RemoveItem(data);
        //        }
        //    }
        //}
        #endregion
        public event RoutedEventHandler OnSelected;
        public event RoutedEventHandler OnMouseRightButtonUP;

        private StackPanel ItemsPanel;
        private Dictionary<int, NavigationItem> ItemsDictionary;
        //  选中标记块
        private Border ActiveBlock;
        //  标记块所在容器，作为定位的参照系
        private Grid MenuGrid;
        //  标记块的形变与位移。缩放必须排在位移之前，否则缩放中心会连同位移量一起被放大
        private ScaleTransform activeScale;
        private TranslateTransform activeTranslate;
        //  标记块是否已完成首次定位。首次定位不播放动画
        private bool isActiveBlockPlaced;
        //  是否已有一次等待布局完成的定位排队中，避免重复投递
        private bool isPlacementPending;

        //  横向收窄相对于纵向拉伸的比例，用于近似维持体积守恒
        private const double VolumeCompensation = 0.3;
        //  拉伸达到峰值的时间点，对应弹簧位移速度最快的时刻
        private const double StretchPeakPercent = 0.18;

        public Navigation()
        {
            DefaultStyleKey = typeof(Navigation);
            ItemsDictionary = new Dictionary<int, NavigationItem>();
            Loaded += Navigation_Loaded;
            IsVisibleChanged += Navigation_IsVisibleChanged;
        }

        private void Navigation_Loaded(object sender, RoutedEventArgs e)
        {
            ScrollToActive(false);
        }

        private void Navigation_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
            {
                ScrollToActive(false);
            }
        }

        #region create animation
        private void CreateAnimations()
        {
            if (ActiveBlock == null)
            {
                return;
            }

            activeScale = new ScaleTransform(1, 1, 0, 0);
            activeTranslate = new TranslateTransform(0, 0);

            var tfg = new TransformGroup();
            tfg.Children.Add(activeScale);
            tfg.Children.Add(activeTranslate);
            ActiveBlock.RenderTransform = tfg;

            //  首次定位完成前保持隐藏，避免标记块从原点闪现
            ActiveBlock.Opacity = 0;
            isActiveBlockPlaced = false;
        }
        #endregion

        #region temp region
        private void Data_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                foreach (var item in e.NewItems)
                {
                    AddItem(item as NavigationItemModel);
                }
            }
            if (e.Action == NotifyCollectionChangedAction.Remove)
            {
                foreach (var item in e.OldItems)
                {
                    RemoveItem(item as NavigationItemModel);
                }
            }
            if (e.Action == NotifyCollectionChangedAction.Replace)
            {
                foreach (var ritem in e.NewItems)
                {
                    var item = ritem as NavigationItemModel;
                    var id = item.ID;
                    ItemsDictionary[id].Icon = item.UnSelectedIcon;
                    ItemsDictionary[id].Title = item.Title;
                    ItemsDictionary[id].SelectedIcon = item.SelectedIcon;

                }
            }
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            ItemsPanel = GetTemplateChild("ItemsPanel") as StackPanel;
            ActiveBlock = GetTemplateChild("ActiveBlock") as Border;
            MenuGrid = GetTemplateChild("MenuGrid") as Grid;

            CreateAnimations();
            Render();
        }
        private int CreateID()
        {
            if (ItemsDictionary.Count == 0)
            {
                return 1;
            }
            return ItemsDictionary.Max(m => m.Key) + 1;
        }
        private void AddItem(NavigationItemModel item)
        {
            if (ItemsPanel != null)
            {
                var navItem = new NavigationItem();
                //  ID 约定：-1 为「概览」哨兵（保留）；0 表示未显式赋值，自动生成，
                //  避免默认值 0 的项被 ItemsDictionary 静默丢弃
                int id = item.ID == 0 ? CreateID() : item.ID;
                if (ItemsDictionary.ContainsKey(id))
                {
                    return;
                }

                item.ID = id;
                navItem.ID = id;
                navItem.Title = item.Title;
                navItem.Icon = item.UnSelectedIcon;
                navItem.SelectedIcon = item.SelectedIcon;
                navItem.IconColor = item.IconColor;
                navItem.BadgeText = item.BadgeText;
                navItem.Uri = item.Uri;

                if (!string.IsNullOrEmpty(item.Title))
                {
                    navItem.MouseUp += NavItem_MouseUp;
                    navItem.Unloaded += (e, c) =>
                    {
                        navItem.MouseUp -= NavItem_MouseUp;
                    };
                }
                ItemsPanel.Children.Add(navItem);
                ItemsDictionary.Add(id, navItem);
                //if (item.IsSelected)
                //{
                //    SelectedItem = item;
                //}
            }
        }

        private void NavItem_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var navitem = sender as NavigationItem;
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                //左键选中

                SelectedItem = Data.Where(m => m.ID == navitem.ID).FirstOrDefault();
                OnSelected?.Invoke(this, null);
            }
            if (e.ChangedButton == System.Windows.Input.MouseButton.Right)
            {
                //右键
                var args = new RoutedEventArgs();
                args.RoutedEvent = e.RoutedEvent;
                args.Source = Data.Where(m => m.ID == navitem.ID).FirstOrDefault();
                OnMouseRightButtonUP?.Invoke(this, args);
            }
        }

        private void RemoveItem(NavigationItemModel item)
        {
            //  TryGetValue 防御：模板应用前 AddItem 可能未执行（ItemsPanel 为空不入字典）
            if (item != null && ItemsDictionary.TryGetValue(item.ID, out var navItem))
            {
                ItemsPanel.Children.Remove(navItem);
                ItemsDictionary.Remove(item.ID);
            }
        }
        private void Render()
        {
            ItemsPanel.Children.Clear();
            ItemsDictionary.Clear();

            if (Data != null)
            {
                foreach (var item in Data)
                {
                    AddItem(item);
                }
            }

            ScrollToActive(false);
        }
        #endregion

        private void ScrollToActive(bool animate = true)
        {
            //  获取选中项
            if (SelectedItem == null
                || ActiveBlock == null
                || MenuGrid == null
                || !ItemsDictionary.ContainsKey(SelectedItem.ID))
            {
                return;
            }

            var item = ItemsDictionary[SelectedItem.ID];
            item.IsSelected = true;

            //  导航栏在窄窗口下会被折叠，此时布局尺寸不可用，等重新可见后由 IsVisibleChanged 重新定位
            if (!IsVisible || !item.IsDescendantOf(MenuGrid))
            {
                return;
            }

            if (item.ActualHeight <= 0)
            {
                //  条目尚未完成布局，此时取到的坐标无意义，等本轮布局结束后再定位
                if (isPlacementPending)
                {
                    return;
                }
                isPlacementPending = true;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    isPlacementPending = false;
                    ScrollToActive(animate);
                }), DispatcherPriority.Loaded);
                return;
            }

            double targetY = GetActiveBlockTargetY(item);
            double distance = targetY - activeTranslate.Y;

            if (!animate || !isActiveBlockPlaced)
            {
                PlaceActiveBlock(targetY);
                return;
            }

            if (Math.Abs(distance) < 0.5)
            {
                return;
            }

            AnimateActiveBlock(targetY, distance);
        }

        /// <summary>
        /// 计算标记块应处的纵向位置，使其与目标条目垂直居中对齐。
        /// </summary>
        private double GetActiveBlockTargetY(NavigationItem item)
        {
            Point itemTop = item.TransformToAncestor(MenuGrid).Transform(new Point(0, 0));
            double blockHeight = ActiveBlock.ActualHeight > 0 ? ActiveBlock.ActualHeight : ActiveBlock.Height;
            return itemTop.Y + (item.ActualHeight - blockHeight) / 2;
        }

        /// <summary>
        /// 不经动画直接把标记块放到目标位置，用于首次渲染与布局变化。
        /// </summary>
        private void PlaceActiveBlock(double targetY)
        {
            activeTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            activeScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            activeScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);

            activeTranslate.Y = targetY;
            activeScale.ScaleY = 1;
            activeScale.ScaleX = 1;

            if (!isActiveBlockPlaced)
            {
                isActiveBlockPlaced = true;
                //  动画结束后释放属性，让不透明度回落到基础值而不是被动画长期占用
                ActiveBlock.Opacity = 1;
                ActiveBlock.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Motion.Normal)
                {
                    EasingFunction = Motion.Standard,
                    FillBehavior = FillBehavior.Stop
                });
            }
        }

        /// <summary>
        /// 播放标记块的弹性位移。位移走阻尼谐振曲线，并叠加与速度相关的挤压拉伸：
        /// 软体在加速时沿运动方向被拉长、垂直方向收窄，到位后恢复原状。
        /// </summary>
        private void AnimateActiveBlock(double targetY, double distance)
        {
            Duration duration = Motion.ForDistance(Math.Abs(distance));

            var move = new DoubleAnimation
            {
                From = activeTranslate.Y,
                To = targetY,
                Duration = duration,
                EasingFunction = Math.Abs(distance) < 48 ? Motion.SoftSpring : Motion.Spring
            };

            activeTranslate.BeginAnimation(TranslateTransform.YProperty, move, HandoffBehavior.SnapshotAndReplace);

            // 短距离位移不做挤压拉伸，减少并行动画带来的掉帧
            if (Math.Abs(distance) < 48)
            {
                activeScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                activeScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                activeScale.ScaleY = 1;
                activeScale.ScaleX = 1;
                return;
            }

            double stretch = Motion.StretchForDistance(distance);
            activeScale.CenterX = ActiveBlock.ActualWidth / 2;
            activeScale.CenterY = distance > 0 ? ActiveBlock.ActualHeight : 0;

            var stretchY = BuildDeformation(activeScale.ScaleY, 1 + stretch, duration);
            var squeezeX = BuildDeformation(activeScale.ScaleX, 1 - stretch * VolumeCompensation, duration);

            activeScale.BeginAnimation(ScaleTransform.ScaleYProperty, stretchY, HandoffBehavior.SnapshotAndReplace);
            activeScale.BeginAnimation(ScaleTransform.ScaleXProperty, squeezeX, HandoffBehavior.SnapshotAndReplace);
        }

        /// <summary>
        /// 构造一条形变曲线：从当前值出发，在速度峰值处达到 <paramref name="peak"/>，随后回弹到原尺寸。
        /// </summary>
        private DoubleAnimationUsingKeyFrames BuildDeformation(double from, double peak, Duration duration)
        {
            var animation = new DoubleAnimationUsingKeyFrames { Duration = duration };
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(from, KeyTime.FromPercent(0)));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromPercent(StretchPeakPercent))
            {
                EasingFunction = Motion.Decelerate
            });
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1))
            {
                EasingFunction = Motion.SoftSpring
            });
            return animation;
        }
    }
}
