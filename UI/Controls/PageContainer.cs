using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using System.Windows.Threading;
using UI.Base.Animation;
using UI.Controls.Models;
using UI.Models;

namespace UI.Controls
{
    public class PageContainer : Control
    {
        #region 依赖属性
        public List<string> IndexUriList
        {
            get { return (List<string>)GetValue(IndexUriListProperty); }
            set { SetValue(IndexUriListProperty, value); }
        }
        public static readonly DependencyProperty IndexUriListProperty =
            DependencyProperty.Register("IndexUriList", typeof(List<string>), typeof(PageContainer));

        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title",
                typeof(string),
                typeof(PageContainer));

        public IServiceProvider ServiceProvider
        {
            get { return (IServiceProvider)GetValue(ServiceProviderProperty); }
            set { SetValue(ServiceProviderProperty, value); }
        }
        public static readonly DependencyProperty ServiceProviderProperty =
            DependencyProperty.Register("ServiceProvider", typeof(IServiceProvider), typeof(PageContainer));

        public object Content
        {
            get { return (object)GetValue(ContentProperty); }
            set { SetValue(ContentProperty, value); }
        }
        public static readonly DependencyProperty ContentProperty =
            DependencyProperty.Register("Content", typeof(object), typeof(PageContainer), new PropertyMetadata("Content undefined!"));

        public string Uri
        {
            get { return (string)GetValue(UriProperty); }
            set { SetValue(UriProperty, value); }
        }
        public static readonly DependencyProperty UriProperty =
            DependencyProperty.Register("Uri", typeof(string), typeof(PageContainer), new PropertyMetadata(string.Empty, new PropertyChangedCallback(OnUriChanged)));

        private static void OnUriChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = d as PageContainer;
            if (control == null || e.NewValue == e.OldValue)
            {
                return;
            }

            string oldUri = e.OldValue as string ?? string.Empty;
            if (!string.IsNullOrEmpty(oldUri)
                && control.PageCache.ContainsKey(oldUri)
                && control.ScrollViewer != null)
            {
                PageModel page = control.PageCache[oldUri];
                page.ScrollValue = control.ScrollViewer.VerticalOffset;
            }

            control.BeginNavigate();
        }

        public bool IsShowTilteBar
        {
            get { return (bool)GetValue(IsShowTilteBarProperty); }
            set { SetValue(IsShowTilteBarProperty, value); }
        }
        public static readonly DependencyProperty IsShowTilteBarProperty =
            DependencyProperty.Register("IsShowTilteBar",
                typeof(bool),
                typeof(PageContainer), new PropertyMetadata(false, new PropertyChangedCallback(OnIsShowTitleBarChanged)));

        private static void OnIsShowTitleBarChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = d as PageContainer;
            if (e.NewValue != e.OldValue)
            {
                control.TitleBarVisibility = control.IsShowTilteBar ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        public Visibility TitleBarVisibility
        {
            get { return (Visibility)GetValue(TitleBarVisibilityProperty); }
            set { SetValue(TitleBarVisibilityProperty, value); }
        }
        public static readonly DependencyProperty TitleBarVisibilityProperty =
            DependencyProperty.Register("TitleBarVisibility",
                typeof(Visibility),
                typeof(PageContainer), new PropertyMetadata(Visibility.Collapsed));

        public Command BackCommand
        {
            get { return (Command)GetValue(BackCommandProperty); }
            set { SetValue(BackCommandProperty, value); }
        }
        public static readonly DependencyProperty BackCommandProperty =
            DependencyProperty.Register("BackCommand",
                typeof(Command),
                typeof(PageContainer));


        public PageContainer Instance { get { return (PageContainer)GetValue(InstanceProperty); } set { SetValue(InstanceProperty, value); } }

        public static readonly DependencyProperty InstanceProperty = DependencyProperty.Register("Instance", typeof(PageContainer), typeof(PageContainer));
        #endregion

        /// <summary>
        /// 加载页面完成后发生
        /// </summary>
        public event EventHandler OnLoadPaged;

        private readonly string ProjectName;
        private List<string> Historys;
        public int Index = 0, OldIndex = 0;
        private Dictionary<string, PageModel> PageCache;
        private bool IsBack = false;
        private ScrollViewer ScrollViewer;
        private Frame Frame;
        private double _pendingScrollOffset;
        private bool _restoreScrollOnEnter;
        public PageContainer()
        {
            DefaultStyleKey = typeof(PageContainer);
            ProjectName = "UI";
            Historys = new List<string>();
            BackCommand = new Command(new Action<object>(OnBackCommand));
            NavigationCommands.BrowseBack.InputGestures.Clear();
            NavigationCommands.BrowseForward.InputGestures.Clear();
            PageCache = new Dictionary<string, PageModel>();
        }


        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            ScrollViewer = GetTemplateChild("ScrollViewer") as ScrollViewer;
            Frame = GetTemplateChild("Frame") as Frame;
            if (ScrollViewer != null)
            {
                ScrollViewer.SetValue(System.Windows.Controls.ScrollViewer.IsDeferredScrollingEnabledProperty, true);
            }
            //  幂等订阅：模板可能被多次应用，重复订阅会导致事件翻倍触发
            if (Frame != null)
            {
                Frame.NavigationService.Navigated -= NavigationService_Navigated;
                Frame.NavigationService.Navigated += NavigationService_Navigated;
            }
            Loaded -= PageContainer_Loaded;
            Loaded += PageContainer_Loaded;

            SetupTransition();
        }

        private void NavigationService_Navigated(object sender, NavigationEventArgs e)
        {
            Frame.NavigationService.RemoveBackEntry();
        }

        private void PageContainer_Loaded(object sender, RoutedEventArgs e)
        {
            Instance = this;
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.ChangedButton == MouseButton.XButton1)
            {
                Back();
            }
        }
        private void OnBackCommand(object obj)
        {
            Back();
        }

        public void Back()
        {
            if (Index - 1 >= 0)
            {
                OldIndex = Index;
                Index--;
                string uri = Historys[Index];

                int preIndex = Index + 1;

                //  从缓存中移除上一页

                var pageUri = Historys[preIndex];
                if (PageCache.ContainsKey(pageUri))
                {
                    var page = PageCache[pageUri];
                    var vm = page.Instance.DataContext as IDisposable;
                    vm?.Dispose();
                    page.Instance.Content = null;
                    page.Instance.DataContext = null;

                    PageCache.Remove(pageUri);
                }
                Historys.RemoveRange(preIndex, 1);

                IsBack = true;

                Uri = uri;

            }
        }

        public void ClearHistorys()
        {
            Historys.Clear();
        }
        private void LoadPage()
        {
            if (Uri != string.Empty)
            {
                if (IndexUriList != null && IndexUriList.Contains(Uri))
                {
                    Historys.Clear();
                    Index = 0;
                    OldIndex = 0;
                    Historys.Add(Uri);
                }
                else
                {
                    //处理历史记录
                    if (OldIndex == Index)
                    {
                        //新开
                        Historys.Add(Uri);
                        Index++;
                    }
                    OldIndex = Index;
                }
                PageModel page = GetPage();


                if (page != null)
                {
                    Content = page.Instance;

                    //  加入缓存
                    if (!PageCache.ContainsKey(Uri))
                    {
                        PageCache.Add(Uri, page);
                    }

                    //  滚动位置延后到布局完成后再恢复，避免与进场动画争抢 UI 线程
                    _restoreScrollOnEnter = true;
                    _pendingScrollOffset = IsBack ? page.ScrollValue : 0;

                    OnLoadPaged?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    Debug.WriteLine("找不到Page：" + Uri + "，请确认已被注入");
                }
            }
            IsBack = false;
        }

        private PageModel GetPage()
        {
            Page page = null;
            if (PageCache.ContainsKey(Uri))
            {
                return PageCache[Uri];
            }
            Type pageType = Type.GetType(ProjectName + ".Views." + Uri);
            if (pageType != null && ServiceProvider != null)
            {
                page = ServiceProvider.GetService(pageType) as Page;
            }
            var newPage = new PageModel()
            {
                Instance = page,
                ScrollValue = 0
            };

            return newPage;
        }

        private void ClearCache()
        {
            if (PageCache != null)
            {
                foreach (var key in PageCache.Keys)
                {
                    var page = PageCache[key];
                    var vm = page.Instance.DataContext as IDisposable;
                    vm?.Dispose();
                    page.Instance.Content = null;
                    page.Instance.DataContext = null;
                }
                PageCache.Clear();
            }
        }

        /// <summary>
        /// 释放全部缓存页面（导航容器卸载时调用）。
        /// </summary>
        public void Dispose()
        {
            ClearCache();
            Content = null;
            DataContext = null;
            Instance = null;
        }

        #region 页面过渡动画

        /// <summary>
        /// 过渡方向。进入下级页面向左推进，返回向右退回，同级页面之间只做淡入淡出。
        /// </summary>
        private enum TransitionDirection
        {
            Fade,
            Forward,
            Backward
        }

        //  横向推进与纵向浮起的位移幅度
        private const double SlideOffset = 24;
        private const double RiseOffset = 10;

        private ScaleTransform contentScale;
        private TranslateTransform contentTranslate;
        private bool isFirstNavigation = true;
        //  用于识别过期的过渡回调，快速连续切换时只有最后一次生效
        private int navigateToken;

        private void SetupTransition()
        {
            ClipToBounds = true;

            if (Frame == null)
            {
                return;
            }

            //  变换作用在 Frame 上而不是整个控件，否则滚动条会跟着一起位移
            contentScale = new ScaleTransform(1, 1);
            contentTranslate = new TranslateTransform(0, 0);

            var tfg = new TransformGroup();
            tfg.Children.Add(contentScale);
            tfg.Children.Add(contentTranslate);
            Frame.RenderTransform = tfg;
            //  以顶部为缩放锚点，内容超过一屏时才不会因为锚点在屏幕外而产生位移错觉
            Frame.RenderTransformOrigin = new Point(0.5, 0);
        }

        /// <summary>
        /// 先让旧页面退场，等它完全淡出后再换页并播放进场。
        /// 页面实例化的开销正好落在不可见的这一刻，因此不会表现为动画卡顿。
        /// </summary>
        private void BeginNavigate()
        {
            if (contentTranslate == null)
            {
                LoadPage();
                return;
            }

            TransitionDirection direction = ResolveDirection();
            int token = ++navigateToken;
            CancelActiveAnimations();

            if (isFirstNavigation || direction == TransitionDirection.Fade)
            {
                isFirstNavigation = false;
                SwapAndEnter(direction, token);
                return;
            }

            PlayExit(direction, () =>
            {
                if (token != navigateToken)
                {
                    return;
                }

                SwapAndEnter(direction, token);
            });
        }

        private void CancelActiveAnimations()
        {
            if (Frame == null)
            {
                return;
            }

            Frame.BeginAnimation(UIElement.OpacityProperty, null);
            Frame.Opacity = 1;
            AnimationRuntime.ResetTransform(contentTranslate, contentScale);
            AnimationRuntime.DisableTransitionCache(Frame);
        }

        private TransitionDirection ResolveDirection()
        {
            if (IsBack)
            {
                return TransitionDirection.Backward;
            }
            if (IndexUriList != null && IndexUriList.Contains(Uri))
            {
                return TransitionDirection.Fade;
            }
            return TransitionDirection.Forward;
        }

        private void SwapAndEnter(TransitionDirection direction, int token)
        {
            try
            {
                LoadPage();
            }
            finally
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (token != navigateToken)
                    {
                        return;
                    }

                    if (_restoreScrollOnEnter && ScrollViewer != null)
                    {
                        ScrollViewer.ScrollToVerticalOffset(_pendingScrollOffset);
                        _restoreScrollOnEnter = false;
                    }

                    if (direction == TransitionDirection.Fade)
                    {
                        if (AnimationBudget.ReduceMotion)
                        {
                            PlaySimpleFade(token);
                            return;
                        }

                        PlayCrossFade(token);
                        return;
                    }

                    PlayEnter(direction, token);
                }), DispatcherPriority.Loaded);
            }
        }

        private void PlayExit(TransitionDirection direction, Action onCompleted)
        {
            if (AnimationBudget.ReduceMotion)
            {
                AnimationRuntime.EnableTransitionCache(Frame);
                var quickFade = new DoubleAnimation
                {
                    To = 0,
                    Duration = Motion.Micro,
                    EasingFunction = Motion.Accelerate
                };
                quickFade.Completed += (s, e) => onCompleted();
                Frame.BeginAnimation(UIElement.OpacityProperty, quickFade);
                return;
            }

            double toX = 0, toY = 0;
            switch (direction)
            {
                case TransitionDirection.Forward:
                    toX = -SlideOffset;
                    break;
                case TransitionDirection.Backward:
                    toX = SlideOffset;
                    break;
                default:
                    toY = -RiseOffset * 0.5;
                    break;
            }

            AnimationRuntime.EnableTransitionCache(Frame);
            AnimationRuntime.ResetTransform(contentTranslate, contentScale);

            Duration duration = Motion.Fast;
            var fade = new DoubleAnimation
            {
                To = 0,
                Duration = duration,
                EasingFunction = Motion.Accelerate
            };
            fade.Completed += (s, e) => onCompleted();

            contentTranslate.BeginAnimation(TranslateTransform.XProperty, ToValue(toX, duration, Motion.Accelerate), HandoffBehavior.SnapshotAndReplace);
            contentTranslate.BeginAnimation(TranslateTransform.YProperty, ToValue(toY, duration, Motion.Accelerate), HandoffBehavior.SnapshotAndReplace);
            Frame.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        private void PlayEnter(TransitionDirection direction, int token)
        {
            if (token != navigateToken)
            {
                return;
            }

            if (AnimationBudget.ReduceMotion)
            {
                CancelActiveAnimations();
                return;
            }

            double fromX = 0, fromY = 0;
            switch (direction)
            {
                case TransitionDirection.Forward:
                    fromX = SlideOffset;
                    break;
                case TransitionDirection.Backward:
                    fromX = -SlideOffset;
                    break;
                default:
                    fromY = RiseOffset;
                    break;
            }

            AnimationRuntime.EnableTransitionCache(Frame);
            AnimationRuntime.ResetTransform(contentTranslate, contentScale);
            Frame.BeginAnimation(UIElement.OpacityProperty, null);
            Frame.Opacity = 0;
            contentTranslate.X = fromX;
            contentTranslate.Y = fromY;

            Duration duration = Motion.Normal;
            //  弹力曲线带轻微过冲：页面"蹦"到位而非滑到位
            var moveX = FromTo(fromX, 0, duration, Motion.Bouncy);
            var moveY = FromTo(fromY, 0, duration, Motion.Bouncy);
            var fade = FromTo(0, 1, Motion.Fast, Motion.Standard);
            fade.Completed += (s, e) =>
            {
                if (token != navigateToken)
                {
                    return;
                }

                AnimationRuntime.ResetTransform(contentTranslate, contentScale);
                Frame.BeginAnimation(UIElement.OpacityProperty, null);
                Frame.Opacity = 1;
                AnimationRuntime.DisableTransitionCache(Frame);
            };

            contentTranslate.BeginAnimation(TranslateTransform.XProperty, moveX, HandoffBehavior.SnapshotAndReplace);
            contentTranslate.BeginAnimation(TranslateTransform.YProperty, moveY, HandoffBehavior.SnapshotAndReplace);
            Frame.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        private void PlayCrossFade(int token)
        {
            if (token != navigateToken)
            {
                return;
            }

            AnimationRuntime.EnableTransitionCache(Frame);
            AnimationRuntime.ResetTransform(contentTranslate, contentScale);
            Frame.BeginAnimation(UIElement.OpacityProperty, null);
            Frame.Opacity = 0;
            contentTranslate.Y = Motion.CrossFadeOffsetY;

            var moveY = FromTo(Motion.CrossFadeOffsetY, 0, Motion.CrossFade, Motion.AppleDecelerate);
            var fade = FromTo(0, 1, Motion.CrossFade, Motion.Standard);
            fade.Completed += (s, e) =>
            {
                if (token != navigateToken)
                {
                    return;
                }

                AnimationRuntime.ResetTransform(contentTranslate, contentScale);
                Frame.BeginAnimation(UIElement.OpacityProperty, null);
                Frame.Opacity = 1;
                AnimationRuntime.DisableTransitionCache(Frame);
            };

            contentTranslate.BeginAnimation(TranslateTransform.YProperty, moveY, HandoffBehavior.SnapshotAndReplace);
            Frame.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        private void PlaySimpleFade(int token)
        {
            if (token != navigateToken)
            {
                return;
            }

            AnimationRuntime.EnableTransitionCache(Frame);
            AnimationRuntime.ResetTransform(contentTranslate, contentScale);
            Frame.BeginAnimation(UIElement.OpacityProperty, null);
            Frame.Opacity = 0;

            var fade = FromTo(0, 1, Motion.Micro, Motion.Standard);
            fade.Completed += (s, e) =>
            {
                if (token != navigateToken)
                {
                    return;
                }

                Frame.BeginAnimation(UIElement.OpacityProperty, null);
                Frame.Opacity = 1;
                AnimationRuntime.DisableTransitionCache(Frame);
            };

            Frame.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        private static DoubleAnimation ToValue(double to, Duration duration, IEasingFunction easing)
        {
            return new DoubleAnimation { To = to, Duration = duration, EasingFunction = easing };
        }

        private static DoubleAnimation FromTo(double from, double to, Duration duration, IEasingFunction easing)
        {
            return new DoubleAnimation { From = from, To = to, Duration = duration, EasingFunction = easing };
        }
        #endregion
    }
}
