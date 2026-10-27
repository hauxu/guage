using System;
using System.Windows;
using System.Windows.Controls;
using UI.ViewModels;

namespace UI.Views
{
    /// <summary>
    /// IndexPage.xaml 的交互逻辑
    /// </summary>
    public partial class IndexPage : Page
    {
        private readonly IndexPageVM vm;
        private Window hostWindow;

        public IndexPage(IndexPageVM vm)
        {
            InitializeComponent();
            DataContext = vm;
            this.vm = vm;

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            vm.RefreshBrowserExtensionStatus();
            vm.RefreshNow();

            //  窗口被托盘收起再打开时页面不会重新 Loaded，这里跟窗口可见性挂钩：
            //  一显示就立刻刷一次，不必等到下一个 tick
            hostWindow = Window.GetWindow(this);
            if (hostWindow != null)
            {
                hostWindow.IsVisibleChanged -= HostWindow_IsVisibleChanged;
                hostWindow.IsVisibleChanged += HostWindow_IsVisibleChanged;
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (hostWindow != null)
            {
                hostWindow.IsVisibleChanged -= HostWindow_IsVisibleChanged;
                hostWindow = null;
            }

            vm.StopLiveUpdates();
            vm.StopAutoRefresh();
        }

        private void HostWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is bool visible && visible)
            {
                vm.RefreshNow();
            }
        }
    }
}
