using Core;
using Core.Librarys;
using Core.Servicers.Interfaces;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using UI.ViewModels;

namespace UI.Servicers
{
    public class StatusBarIconServicer : IStatusBarIconServicer
    {
        //  状态栏图标
        private System.Windows.Forms.NotifyIcon _statusBarIcon;
        //  状态栏图标菜单
        private ContextMenu _contextMenu;

        /// <summary>
        /// 图标类型
        /// </summary>
        public enum IconType
        {
            /// <summary>
            /// 正常
            /// </summary>
            Normal,
            /// <summary>
            /// 繁忙中
            /// </summary>
            Busy
        }

        private readonly IAppObserver _appObserver;
        private readonly IThemeServicer _themeServicer;
        private readonly MainViewModel _mainVM;
        private readonly IAppConfig _appConfig;
        private readonly IUIServicer _uIServicer;
        private MainWindow _mainWindow;
        public StatusBarIconServicer(
            IAppObserver appObserver_,
            IThemeServicer themeServicer_,
            IAppConfig appConfig_,
            MainViewModel mainVM_,
            IUIServicer uIServicer_
            )
        {
            _appObserver = appObserver_;
            _themeServicer = themeServicer_;
            _appConfig = appConfig_;
            _mainVM = mainVM_;
            _uIServicer = uIServicer_;
        }
        public void Init()
        {
            InitMenu();

            _statusBarIcon = new System.Windows.Forms.NotifyIcon();
            _statusBarIcon.Visible = true;
            _statusBarIcon.MouseClick += _statusBarIcon_MouseClick;
            _statusBarIcon.MouseDoubleClick += _statusBarIcon_MouseDoubleClick;

            SetIcon(IconType.Busy);

            WatchStateAsync();

            _appObserver.OnAppActiveChanged += _appObserver_OnAppActiveChanged; ;
            _themeServicer.OnThemeChanged += _themeServicer_OnThemeChanged;
            Application.Current.DispatcherUnhandledException += Current_DispatcherUnhandledException;

            // 内存养护：空闲时 GC 压缩 + 工作集修剪，控制进程内存低水位
            MemoryCareServicer.Start();
        }


        private void Current_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            _statusBarIcon.Visible = false;
            Logger.Save(true);
        }

        private void _themeServicer_OnThemeChanged(object sender, EventArgs e)
        {
            _contextMenu.UpdateDefaultStyle();
        }

        private void _appObserver_OnAppActiveChanged(object sender, Core.Event.AppActiveChangedEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _contextMenu.IsOpen = false;
            }));
        }

        /// <summary>
        /// 初始化状态栏菜单
        /// </summary>
        private void InitMenu()
        {
            _contextMenu = new ContextMenu();
            App.Current.Deactivated += (e, c) =>
            {
                _contextMenu.IsOpen = false;
            };
            var mainWindowMenuItem = new MenuItem();
            mainWindowMenuItem.Header = "主界面";
            mainWindowMenuItem.Click += MainWindowMenuItem_Click; ;
            var exitMenuItem = new MenuItem();
            exitMenuItem.Header = "退出";
            exitMenuItem.Click += ExitMenuItem_Click;

            _contextMenu.Items.Add(mainWindowMenuItem);
            _contextMenu.Items.Add(exitMenuItem);

        }

        /// <summary>
        /// 等待程序加载（UI 定时器轮询，避免后台线程忙等与跨线程操作 NotifyIcon）
        /// </summary>
        private void WatchStateAsync()
        {
            var watchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            watchTimer.Tick += (s, e) =>
            {
                if (AppState.IsLoading)
                {
                    _statusBarIcon.Text = $"[{AppState.ProcessValue}%] 更矩 [{AppState.ActionText}]";
                    return;
                }

                watchTimer.Stop();
                _statusBarIcon.Text = "更矩!";
                SetIcon();
            };
            watchTimer.Start();
        }

        /// <summary>
        /// 设置图标
        /// </summary>
        /// <param name="iconType_">图标样式</param>
        private void SetIcon(IconType iconType_ = IconType.Normal)
        {
            try
            {
                string iconName = "guage32";
                switch (iconType_)
                {
                    case IconType.Busy:
                        iconName = "guagebusy";
                        break;
                    default:
                        iconName = "guage32";
                        break;
                }
                Stream iconStream = Application.GetResourceStream(new Uri($"pack://application:,,,/guage;component/Resources/Icons/{iconName}.ico")).Stream;
                using (iconStream)
                {
                    var oldIcon = _statusBarIcon.Icon;
                    _statusBarIcon.Icon = new Icon(iconStream);
                    oldIcon?.Dispose();
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex.Message);
                Logger.Save(true);
            }
        }

        public void ShowMainWindow()
        {
            var config = _appConfig.GetConfig();
            if (config == null)
            {
                return;
            }

            if (_mainWindow == null || _mainWindow.IsWindowClosed)
            {
                _mainWindow = new MainWindow();
                _mainWindow.DataContext = _mainVM;
                _mainWindow.Loaded += _mainWindow_Loaded;
                _mainVM.LoadDefaultPage();
            }
            if (config.General.IsSaveWindowSize)
            {
                double restoreWidth = config.General.WindowWidth;
                double restoreHeight = config.General.WindowHeight;
                //  恢复窗口尺寸时的极端值兜底（最小尺寸限制已按需求移除，只防荒谬值）
                if (restoreWidth < 420) restoreWidth = 420;
                if (restoreHeight < 380) restoreHeight = 380;
                _mainWindow.Width = restoreWidth;
                _mainWindow.Height = restoreHeight;
            }
            _mainWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Show();
            _mainWindow.Activate();

            _uIServicer.InitWindow(_mainWindow);
        }

        public void ShowBalloonTip(string title, string message)
        {
            try
            {
                if (_statusBarIcon != null)
                {
                    _statusBarIcon.ShowBalloonTip(3000, title, message, System.Windows.Forms.ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex.ToString());
            }
        }

        private void _mainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var window = sender as MainWindow;

            _themeServicer.SetMainWindow(window);
            _themeServicer.UpdateWindowStyle();
        }

        private void ExitApp()
        {
            _statusBarIcon.Visible = false;

            Logger.Save(true);
            Application.Current.Shutdown();
        }
        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ExitApp();
        }

        private void MainWindowMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ShowMainWindow();
        }

        private void _statusBarIcon_MouseDoubleClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left && !AppState.IsLoading)
            {
                //双击托盘图标显示主窗口
                ShowMainWindow();
            }
        }

        private void _statusBarIcon_MouseClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Right && !AppState.IsLoading)
            {
                //右键单击弹出托盘菜单
                _contextMenu.IsOpen = true;
            }
        }
    }
}
