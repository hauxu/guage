using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using UI.Base.Animation;
using UI.Controls.Button;
using UI.Controls.Input;

namespace UI.Controls.Window
{
    public class DefaultWindow : System.Windows.Window
    {
        #region 1.依赖属性
        #region Dialog
        public static readonly DependencyProperty DialogTitleProperty = DependencyProperty.Register("DialogTitle", typeof(string), typeof(DefaultWindow));
        /// <summary>
        /// Dialog title
        /// </summary>
        public string DialogTitle
        {
            get { return (string)GetValue(DialogTitleProperty); }
            set { SetValue(DialogTitleProperty, value); }
        }

        public static readonly DependencyProperty DialogMessageProperty = DependencyProperty.Register("DialogMessage", typeof(string), typeof(DefaultWindow));
        /// <summary>
        /// Dialog message
        /// </summary>
        public string DialogMessage
        {
            get { return (string)GetValue(DialogMessageProperty); }
            set { SetValue(DialogMessageProperty, value); }
        }
        #endregion
        #region InputModal
        public static readonly DependencyProperty InputModalValueProperty = DependencyProperty.Register("InputModalValue", typeof(string), typeof(DefaultWindow));
        /// <summary>
        /// Dialog title
        /// </summary>
        public string InputModalValue
        {
            get { return (string)GetValue(InputModalValueProperty); }
            set { SetValue(InputModalValueProperty, value); }
        }
        #endregion
        public static readonly DependencyProperty IsShowToastDProperty = DependencyProperty.Register("IsShowToast", typeof(bool), typeof(DefaultWindow), new PropertyMetadata(false, new PropertyChangedCallback(OnIsShowToastChanged)));
        public bool IsShowToast { get { return (bool)GetValue(IsShowToastDProperty); } set { SetValue(IsShowToastDProperty, value); } }
        private static void OnIsShowToastChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var that = d as DefaultWindow;
            if (that != null)
            {
                if (that.IsShowToast)
                {
                    that.ShowToast();
                }
                else
                {
                    that.HideToast();
                }
            }
        }

        public static readonly DependencyProperty ToastIconProperty = DependencyProperty.Register("ToastIcon", typeof(Base.IconTypes), typeof(DefaultWindow));
        public Base.IconTypes ToastIcon
        {
            get
            {
                return (Base.IconTypes)GetValue(ToastIconProperty);
            }
            set { SetValue(ToastIconProperty, value); }
        }
        public static readonly DependencyProperty ToastContentProperty = DependencyProperty.Register("ToastContent", typeof(string), typeof(DefaultWindow));
        /// <summary>
        /// toast content
        /// </summary>
        public string ToastContent
        {
            get { return (string)GetValue(ToastContentProperty); }
            set { SetValue(ToastContentProperty, value); }
        }
        public static readonly DependencyProperty ToastTypeProperty = DependencyProperty.Register("ToastType", typeof(ToastType), typeof(DefaultWindow), new PropertyMetadata(ToastType.Info));
        /// <summary>
        /// toast type
        /// </summary>
        public ToastType ToastType
        {
            get { return (ToastType)GetValue(ToastTypeProperty); }
            set { SetValue(ToastTypeProperty, value); }
        }
        public static readonly DependencyProperty PageContainerProperty = DependencyProperty.Register("PageContainer", typeof(PageContainer), typeof(DefaultWindow), new PropertyMetadata(null, new PropertyChangedCallback(OnPageContainerChanged)));
        public PageContainer PageContainer { get { return (PageContainer)GetValue(PageContainerProperty); } set { SetValue(PageContainerProperty, value); } }
        #region 最小化按钮显示状态

        public static readonly DependencyProperty MinimizeVisibilityProperty = DependencyProperty.Register("MinimizeVisibility", typeof(Visibility), typeof(DefaultWindow));

        //  PageContainer 页面加载回调（缓存委托，换容器时先退订旧实例）
        private EventHandler _onLoadPagedHandler;
        private PageContainer _subscribedPageContainer;

        private static void OnPageContainerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var that = (DefaultWindow)d;
            if (that != null)
            {
                //  换容器时退订旧实例，避免旧容器持有窗口引用
                if (that._subscribedPageContainer != null && that._onLoadPagedHandler != null)
                {
                    that._subscribedPageContainer.OnLoadPaged -= that._onLoadPagedHandler;
                    that._subscribedPageContainer = null;
                }

                if (e.NewValue != null)
                {
                    that.IsCanBack = that.PageContainer.Index >= 1;

                    if (that._onLoadPagedHandler == null)
                    {
                        that._onLoadPagedHandler = (s, v) =>
                        {
                            var pc = s as PageContainer;
                            that.IsCanBack = pc?.Index >= 1;
                        };
                    }
                    that.PageContainer.OnLoadPaged += that._onLoadPagedHandler;
                    that._subscribedPageContainer = that.PageContainer;
                }
            }
        }

        /// <summary>
        /// 最小化按钮显示状态
        /// </summary>
        public Visibility MinimizeVisibility
        {
            get { return (Visibility)GetValue(MinimizeVisibilityProperty); }
            set { SetValue(MinimizeVisibilityProperty, value); }
        }
        #endregion   

        #region 最大化按钮显示状态

        public static readonly DependencyProperty MaximizeVisibilityProperty = DependencyProperty.Register("MaximizeVisibility", typeof(Visibility), typeof(DefaultWindow));

        /// <summary>
        /// 最大化按钮显示状态
        /// </summary>
        public Visibility MaximizeVisibility
        {
            get { return (Visibility)GetValue(MaximizeVisibilityProperty); }
            set { SetValue(MaximizeVisibilityProperty, value); }
        }
        #endregion

        #region 关闭按钮显示状态

        public static readonly DependencyProperty CloseVisibilityProperty = DependencyProperty.Register("CloseVisibility", typeof(Visibility), typeof(DefaultWindow));

        /// <summary>
        /// 关闭按钮显示状态
        /// </summary>
        public Visibility CloseVisibility
        {
            get { return (Visibility)GetValue(CloseVisibilityProperty); }
            set { SetValue(CloseVisibilityProperty, value); }
        }
        #endregion

        #region 拓展内容
        public static readonly DependencyProperty ExtElementProperty = DependencyProperty.Register("ExtElement", typeof(object), typeof(DefaultWindow));
        /// <summary>
        /// 拓展内容
        /// </summary>
        public object ExtElement
        {
            get { return GetValue(ExtElementProperty); }
            set { SetValue(ExtElementProperty, value); }
        }
        #endregion



        #region 毛玻璃材质

        private bool acrylicEnabled;
        private System.Windows.Media.Color acrylicTint = System.Windows.Media.Colors.White;
        private byte acrylicTintAlpha = 255;

        /// <summary>
        /// 设置毛玻璃材质。系统不支持时设置无效。
        /// 句柄未就绪时只记录参数，等 OnSourceInitialized 时再真正应用。
        /// </summary>
        public void SetAcrylic(bool isEnabled, System.Windows.Media.Color tint, byte tintAlpha)
        {
            acrylicEnabled = isEnabled;
            acrylicTint = tint;
            acrylicTintAlpha = tintAlpha;
            ApplyBackdrop();
        }

        private void ApplyBackdrop()
        {
            UI.Base.WindowBackdrop.Apply(this, acrylicEnabled, acrylicTint, acrylicTintAlpha);
        }

        /// <summary>
        /// 同步系统绘制部分的深浅色。
        /// </summary>
        public void SetDarkTitleBar(bool isDark)
        {
            UI.Base.WindowBackdrop.SetDarkMode(this, isDark);
        }
        #endregion

        #region 是否穿透窗口

        public static readonly DependencyProperty IsThruWindowProperty = DependencyProperty.Register("IsThruWindow", typeof(bool), typeof(DefaultWindow), new PropertyMetadata(false));

        /// <summary>
        /// 是否穿透窗口
        /// </summary>
        public bool IsThruWindow
        {
            get { return (bool)GetValue(IsThruWindowProperty); }
            set { SetValue(IsThruWindowProperty, value); }
        }
        #endregion


        public static readonly DependencyProperty IsCanBackProperty = DependencyProperty.Register("IsCanBack", typeof(bool), typeof(DefaultWindow), new PropertyMetadata(false, new PropertyChangedCallback(OnIsCanBackChanged)));

        private static void OnIsCanBackChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var that = d as DefaultWindow;
            if (that != null)
            {
                if (that.IsCanBack)
                {
                    VisualStateManager.GoToState(that, "CanBackState", true);
                }
                else
                {
                    VisualStateManager.GoToState(that, "Normal", true);
                }
            }
        }

        /// <summary>
        /// 是否可以返回
        /// </summary>
        public bool IsCanBack { get { return (bool)GetValue(IsCanBackProperty); } set { SetValue(IsCanBackProperty, value); } }
        #endregion

        private bool IsWindowClosed_ = false;
        /// <summary>
        /// 指示当前窗口是否已被关闭
        /// </summary>
        public bool IsWindowClosed { get { return IsWindowClosed_; } }

        private Border ToastBorder, Masklayer, DialogBorder, InputModalBorder, RootBorder;
        private Grid ToastGrid;
        private DispatcherTimer toastTimer;
        private bool IsDialogConfirm;
        private bool IsShowInputModal;
        private TaskCompletionSource<bool> _confirmTcs;
        private TaskCompletionSource<string> _inputTcs;
        private Button.Button CancelBtn, ConfirmBtn, InputModalCancelBtn, InputModalConfirmBtn;
        private InputBox InputModalInputBox;
        private string InputValue;
        private Func<string, bool> InputModalValidFnc;
        #region 3.初始化
        public DefaultWindow()
        {
            this.DefaultStyleKey = typeof(DefaultWindow);
            //添加当前窗体到窗体集合

            //命令绑定
            this.CommandBindings.Add(new CommandBinding(DefaultWindowCommands.MinimizeWindowCommand, OnMinimizeWindowCommand));
            this.CommandBindings.Add(new CommandBinding(DefaultWindowCommands.MaximizeWindowCommand, OnMaximizeWindowCommand));
            this.CommandBindings.Add(new CommandBinding(DefaultWindowCommands.RestoreWindowCommand, OnRestoreWindowCommand));
            this.CommandBindings.Add(new CommandBinding(DefaultWindowCommands.CloseWindowCommand, OnCloseWindowCommand));
            this.CommandBindings.Add(new CommandBinding(DefaultWindowCommands.BackCommand, OnBackCommand));

            //  获取程序图标
            if (Icon == null)
            {
                string exeFilePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                using (System.Drawing.Icon exeIcon = System.Drawing.Icon.ExtractAssociatedIcon(exeFilePath))
                {
                    Icon = ToImageSource(exeIcon);
                }
            }

            Loaded += window_Loaded;
            Unloaded += DefaultWindow_Unloaded;
            StateChanged += DefaultWindow_StateChanged;

            toastTimer = new DispatcherTimer();
            toastTimer.Tick += (s, e) =>
            {
                toastTimer.Stop();
                IsShowToast = false;
            };
            toastTimer.Interval = new TimeSpan(0, 0, 3);
        }

        private void DefaultWindow_Unloaded(object sender, RoutedEventArgs e)
        {
            Unloaded -= DefaultWindow_Unloaded;
            Loaded -= window_Loaded;
            StateChanged -= DefaultWindow_StateChanged;
        }

        //  窗口圆角（px）。layered 窗口（AllowsTransparency）下圆角由 WPF 渲染，
        //  任意半径平滑抗锯齿——不再受 DWM 直角/4px/8px 三档限制
        private int configuredCornerRadius = 12;

        /// <summary>
        /// 应用窗口圆角配置。最大化时自动切换为直角。
        /// </summary>
        public void ApplyCornerRadius(int radius)
        {
            configuredCornerRadius = Math.Max(0, Math.Min(24, radius));
            UpdateRoundedCorners();
        }

        private void DefaultWindow_StateChanged(object sender, EventArgs e)
        {
            UpdateRoundedCorners();
        }

        private void UpdateRoundedCorners()
        {
            int radius = WindowState == WindowState.Maximized ? 0 : configuredCornerRadius;

            if (RootBorder != null)
            {
                RootBorder.CornerRadius = new CornerRadius(radius);
                UpdateRootBorderClip(radius);
            }

            //  layered 窗口 DWM 圆角偏好无效；恒传 DONOTROUND，
            //  防止系统 8px 档裁切与更大的 WPF 半径打架
            UI.Base.WindowBackdrop.SetRoundedCorners(this, 0, WindowState != WindowState.Maximized);
        }

        //  上次 Clip 尺寸/半径：未变时跳过重建（缩放时每像素都触发 SizeChanged）
        private double _lastClipWidth = -1;
        private double _lastClipHeight = -1;
        private int _lastClipRadius = -1;

        private void UpdateRootBorderClip(int radius)
        {
            if (RootBorder == null || RootBorder.ActualWidth <= 0 || RootBorder.ActualHeight <= 0)
            {
                return;
            }

            if (radius <= 0)
            {
                RootBorder.Clip = null;
                _lastClipWidth = _lastClipHeight = -1;
                _lastClipRadius = 0;
                return;
            }

            //  尺寸与半径都未变才跳过重建（只改半径时必须重建，否则内容裁切滞后）
            if (Math.Abs(_lastClipWidth - RootBorder.ActualWidth) < 0.5
                && Math.Abs(_lastClipHeight - RootBorder.ActualHeight) < 0.5
                && _lastClipRadius == radius
                && RootBorder.Clip != null)
            {
                return;
            }
            _lastClipWidth = RootBorder.ActualWidth;
            _lastClipHeight = RootBorder.ActualHeight;
            _lastClipRadius = radius;

            // 冻结几何：缩放过程中每步都会重建 Clip，冻结可降低渲染与命中测试开销（缓解缩放卡顿）
            var geometry = new RectangleGeometry
            {
                RadiusX = radius,
                RadiusY = radius,
                Rect = new Rect(0, 0, RootBorder.ActualWidth, RootBorder.ActualHeight)
            };
            geometry.Freeze();
            RootBorder.Clip = geometry;
        }

        private void RootBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            //  尺寸变化时半径与裁切要一起重算：ApplyCornerRadius 可能早于首次布局执行，
            //  那时 RootBorder 实际尺寸还是 0x0，裁切会因尺寸为 0 被跳过
            UpdateRoundedCorners();
        }

        private void OnBackCommand(object sender, ExecutedRoutedEventArgs e)
        {
            if (PageContainer != null)
            {
                PageContainer.Back();
                if (PageContainer.Index == 0)
                {
                    IsCanBack = false;
                }
            }
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);

        public static ImageSource ToImageSource(Icon icon)
        {
            using (Bitmap bitmap = icon.ToBitmap())
            {
                IntPtr hBitmap = bitmap.GetHbitmap();
                try
                {
                    ImageSource wpfBitmap = Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    if (!DeleteObject(hBitmap))
                    {
                        throw new Win32Exception();
                    }

                    return wpfBitmap;
                }
                catch
                {
                    DeleteObject(hBitmap);
                    throw;
                }
            }
        }

        private void window_Loaded(object sender, RoutedEventArgs e)
        {
            AnimationBudget.AttachWindow(this);
            OnSystemButtonsVisibility();
        }




        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            ToastBorder = GetTemplateChild("ToastBorder") as Border;
            Masklayer = GetTemplateChild("Masklayer") as Border;
            ToastGrid = GetTemplateChild("ToastGrid") as Grid;
            DialogBorder = GetTemplateChild("DialogBorder") as Border;
            RootBorder = GetTemplateChild("RootBorder") as Border;
            CancelBtn = GetTemplateChild("CancelBtn") as Button.Button;
            ConfirmBtn = GetTemplateChild("ConfirmBtn") as Button.Button;
            InputModalBorder = GetTemplateChild("InputModalBorder") as Border;
            InputModalCancelBtn = GetTemplateChild("InputModalCancelBtn") as Button.Button;
            InputModalConfirmBtn = GetTemplateChild("InputModalConfirmBtn") as Button.Button;
            InputModalInputBox = GetTemplateChild("InputModalInputBox") as InputBox;

            if (PageContainer != null)
            {
                IsCanBack = PageContainer.Index >= 1;
                if (IsCanBack)
                {
                    VisualStateManager.GoToState(this, "CanBackState", true);
                }
                else
                {
                    VisualStateManager.GoToState(this, "Normal", true);
                }
            }

            //  事件处理缓存为字段并先退订再订阅：模板可能被多次应用（主题重载），
            //  匿名订阅会翻倍触发且无法退订
            if (CancelBtn != null)
            {
                CancelBtn.Click -= CancelBtn_Click;
                CancelBtn.Click += CancelBtn_Click;
            }

            if (ConfirmBtn != null)
            {
                ConfirmBtn.Click -= ConfirmBtn_Click;
                ConfirmBtn.Click += ConfirmBtn_Click;
            }

            if (InputModalCancelBtn != null)
            {
                InputModalCancelBtn.Click -= InputModalCancelBtn_Click;
                InputModalCancelBtn.Click += InputModalCancelBtn_Click;
            }

            if (InputModalConfirmBtn != null)
            {
                InputModalConfirmBtn.Click -= InputModalConfirmBtn_Click;
                InputModalConfirmBtn.Click += InputModalConfirmBtn_Click;
            }

            if (InputModalInputBox != null)
            {
                InputModalInputBox.TextChanged -= InputModalInputBox_TextChanged;
                InputModalInputBox.TextChanged += InputModalInputBox_TextChanged;
            }

            if (RootBorder != null)
            {
                RootBorder.SizeChanged -= RootBorder_SizeChanged;
                RootBorder.SizeChanged += RootBorder_SizeChanged;
                UpdateRoundedCorners();
            }
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            IsDialogConfirm = false;
            HideDialog();
        }

        private void ConfirmBtn_Click(object sender, RoutedEventArgs e)
        {
            IsDialogConfirm = true;
            HideDialog();
        }

        private void InputModalCancelBtn_Click(object sender, RoutedEventArgs e)
        {
            IsDialogConfirm = false;
            HideInputModal();
        }

        private void InputModalConfirmBtn_Click(object sender, RoutedEventArgs e)
        {
            if (InputModalValidFnc != null && !InputModalValidFnc(InputValue))
            {
                return;
            }
            IsDialogConfirm = true;
            HideInputModal();
        }

        private void InputModalInputBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            InputValue = InputModalInputBox.Text;
        }


        /// <summary>
        /// 根据窗口ResizeMode设置系统按钮的可视状态
        /// </summary>
        private void OnSystemButtonsVisibility()
        {
            switch (ResizeMode)
            {
                case ResizeMode.NoResize:
                    //仅显示关闭按钮
                    MinimizeVisibility = Visibility.Collapsed;
                    MaximizeVisibility = Visibility.Collapsed;
                    CloseVisibility = Visibility.Visible;
                    break;
                case ResizeMode.CanResize:
                    MinimizeVisibility = Visibility.Visible;
                    MaximizeVisibility = Visibility.Visible;
                    CloseVisibility = Visibility.Visible;
                    break;
                case ResizeMode.CanMinimize:
                    MinimizeVisibility = Visibility.Visible;
                    MaximizeVisibility = Visibility.Collapsed;
                    CloseVisibility = Visibility.Visible;
                    break;
            }
        }
        #endregion

        protected override void OnClosed(EventArgs e)
        {
            AnimationBudget.DetachWindow();
            base.OnClosed(e);
            IsWindowClosed_ = true;
        }
        #region 5.命令

        private void OnCloseWindowCommand(object sender, ExecutedRoutedEventArgs e)
        {
            Close();        }

        private void OnRestoreWindowCommand(object sender, ExecutedRoutedEventArgs e)
        {
            WindowState = WindowState.Normal;
        }

        private void OnMaximizeWindowCommand(object sender, ExecutedRoutedEventArgs e)
        {
            WindowState = WindowState.Maximized;
        }

        private void OnMinimizeWindowCommand(object sender, ExecutedRoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        #endregion

        #region 弹层动画构造

        /// <summary>
        /// 弹层滑入。默认弹力曲线（带过冲回弹）；Toast 传 Motion.Drop 变为重力下坠。
        /// </summary>
        private static DoubleAnimation BuildSlideIn(DependencyObject target, double from, double to, IEasingFunction easing = null, Duration? duration = null)
        {
            var animation = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = duration ?? Motion.Normal,
                EasingFunction = easing ?? Motion.Bouncy
            };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, new PropertyPath("RenderTransform.Children[0].Y"));
            return animation;
        }

        /// <summary>
        /// 弹层滑出。用加速曲线，让它显得是被带走而不是慢慢挪开。
        /// </summary>
        private static DoubleAnimation BuildSlideOut(DependencyObject target, double to)
        {
            var animation = new DoubleAnimation
            {
                To = to,
                Duration = Motion.Seconds(0.2),
                EasingFunction = Motion.Accelerate
            };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, new PropertyPath("RenderTransform.Children[0].Y"));
            return animation;
        }

        /// <summary>
        /// 遮罩层淡入淡出。
        /// </summary>
        private DoubleAnimation BuildMaskFade(double to)
        {
            var animation = new DoubleAnimation
            {
                To = to,
                Duration = to > 0 ? Motion.Seconds(0.25) : Motion.Seconds(0.18),
                EasingFunction = Motion.Standard
            };
            Storyboard.SetTarget(animation, Masklayer);
            Storyboard.SetTargetProperty(animation, new PropertyPath("Opacity"));
            return animation;
        }
        #endregion

        private void BeginOverlayStoryboard(Storyboard storyboard, Border slidingBorder, Action completed = null)
        {
            Effect savedEffect = slidingBorder?.Effect;
            if (slidingBorder != null)
            {
                slidingBorder.Effect = null;
            }

            storyboard.Completed += (s, e) =>
            {
                AnimationRuntime.RestoreShadow(slidingBorder, savedEffect);
                completed?.Invoke();
            };
            storyboard.Begin();
        }

        private void ShowToast()
        {
            ToastGrid.Visibility = Visibility.Visible;
            DialogBorder.Visibility = Visibility.Collapsed;
            if (!IsShowInputModal)
            {
                InputModalBorder.Visibility = Visibility.Collapsed;
            }
            ToastBorder.Visibility = Visibility.Visible;

            //  计时器在入口即重置（Stop+Start）：连续 Toast 变为"续时"，
            //  而不是等上一条 3s 计时到期后"消失又重弹"的闪回
            toastTimer.Stop();
            toastTimer.Start();

            Storyboard storyboard = new Storyboard();

            //  Toast 从顶部弹力滑入（轻微过冲，无二次反弹——
            //  之前的重力双反弹在 Toast 尺度上就是"缩回去又掉下来"的闪回观感）
            DoubleAnimation scrollAnimation = BuildSlideIn(ToastBorder, -150, 0, Motion.Bouncy);
            DoubleAnimation opacityAnimation = BuildMaskFade(0.6);
            storyboard.Children.Add(scrollAnimation);
            if (!IsShowInputModal)
            {
                storyboard.Children.Add(opacityAnimation);
            }

            //  点击处理器幂等挂接（先减后加，避免多次 Show 累积）
            ToastGrid.MouseLeftButtonDown -= ToastGrid_MouseLeftButtonDown;
            ToastGrid.MouseLeftButtonDown += ToastGrid_MouseLeftButtonDown;

            BeginOverlayStoryboard(storyboard, ToastBorder);
        }

        private void ToastGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            toastTimer.Stop();
            IsShowToast = false;
            ToastGrid.MouseLeftButtonDown -= ToastGrid_MouseLeftButtonDown;
        }

        private void HideToast()
        {
            Storyboard storyboard = new Storyboard();

            DoubleAnimation scrollAnimation = BuildSlideOut(ToastBorder, -150);
            DoubleAnimation opacityAnimation = BuildMaskFade(0);

            storyboard.Children.Add(scrollAnimation);
            if (!IsShowInputModal)
            {
                storyboard.Children.Add(opacityAnimation);
            }

            BeginOverlayStoryboard(storyboard, ToastBorder, () =>
            {
                if (!IsShowInputModal)
                {
                    ToastGrid.Visibility = Visibility.Collapsed;
                }
            });
        }


        #region Dialog
        public Task<bool> ShowConfirmDialogAsync(string title_, string message_)
        {
            ToastGrid.Visibility = Visibility.Visible;
            ToastBorder.Visibility = Visibility.Collapsed;
            DialogBorder.Visibility = Visibility.Visible;
            InputModalBorder.Visibility = Visibility.Collapsed;

            DialogMessage = message_;
            DialogTitle = title_;

            Storyboard storyboard = new Storyboard();

            DoubleAnimation scrollAnimation = BuildSlideIn(DialogBorder, -150, 10);
            DoubleAnimation opacityAnimation = BuildMaskFade(0.6);
            storyboard.Children.Add(scrollAnimation);
            storyboard.Children.Add(opacityAnimation);
            BeginOverlayStoryboard(storyboard, DialogBorder);

            //  用 TaskCompletionSource 替代忙等轮询：按钮点击后由动画完成回调置位
            _confirmTcs = new TaskCompletionSource<bool>();
            return _confirmTcs.Task;
        }
        private void HideDialog()
        {
            Storyboard storyboard = new Storyboard();

            DoubleAnimation scrollAnimation = BuildSlideOut(DialogBorder, -150);
            DoubleAnimation opacityAnimation = BuildMaskFade(0);

            storyboard.Children.Add(scrollAnimation);
            storyboard.Children.Add(opacityAnimation);
            BeginOverlayStoryboard(storyboard, DialogBorder, () =>
            {
                ToastGrid.Visibility = Visibility.Collapsed;
                _confirmTcs?.TrySetResult(IsDialogConfirm);
            });
        }
        #endregion

        #region InputModal
        public Task<string> ShowInputModalAsync(string title_, string message_, string value_, Func<string, bool> validFnc_)
        {
            IsShowInputModal = true;
            ToastGrid.Visibility = Visibility.Visible;
            ToastBorder.Visibility = Visibility.Collapsed;
            DialogBorder.Visibility = Visibility.Collapsed;
            InputModalBorder.Visibility = Visibility.Visible;

            DialogMessage = message_;
            DialogTitle = title_;
            InputModalValue = value_;
            InputModalValidFnc = validFnc_;

            InputModalInputBox.Text = InputModalValue;

            Storyboard storyboard = new Storyboard();

            DoubleAnimation scrollAnimation = BuildSlideIn(InputModalBorder, -150, 10);
            DoubleAnimation opacityAnimation = BuildMaskFade(0.6);
            storyboard.Children.Add(scrollAnimation);
            storyboard.Children.Add(opacityAnimation);
            BeginOverlayStoryboard(storyboard, InputModalBorder, () => InputModalInputBox.Focus());

            //  用 TaskCompletionSource 替代忙等轮询
            _inputTcs = new TaskCompletionSource<string>();
            return _inputTcs.Task;
        }
        private void HideInputModal()
        {
            Storyboard storyboard = new Storyboard();

            DoubleAnimation scrollAnimation = BuildSlideOut(InputModalBorder, -150);
            DoubleAnimation opacityAnimation = BuildMaskFade(0);

            storyboard.Children.Add(scrollAnimation);
            storyboard.Children.Add(opacityAnimation);
            BeginOverlayStoryboard(storyboard, InputModalBorder, () =>
            {
                ToastGrid.Visibility = Visibility.Collapsed;
                IsShowInputModal = false;

                if (IsDialogConfirm)
                {
                    _inputTcs?.TrySetResult(InputValue);
                }
                else
                {
                    //  取消：与旧行为一致，以异常方式通知调用方
                    _inputTcs?.TrySetException(new Exception("Input cancel"));
                }
            });
        }
        #endregion

        #region 6.事件

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            if (IsThruWindow)
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle; uint extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT);
            }

            //  句柄此时才存在，材质必须在这里应用
            ApplyBackdrop();
            UpdateRoundedCorners();
        }
        #endregion

        #region win32
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int GWL_EXSTYLE = (-20);

        [DllImport("user32", EntryPoint = "SetWindowLong")]
        private static extern uint SetWindowLong(IntPtr hwnd, int nIndex, uint dwNewLong);

        [DllImport("user32", EntryPoint = "GetWindowLong")]
        private static extern uint GetWindowLong(IntPtr hwnd, int nIndex);
        #endregion
    }
}
