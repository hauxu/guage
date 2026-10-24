using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using UI.Controls.Charts.Model;
using UI.ViewModels;

namespace UI.Views
{
    /// <summary>
    /// IndexPage.xaml 的交互逻辑
    /// </summary>
    public partial class IndexPage : Page
    {
        public IndexPage(IndexPageVM vm)
        {
            InitializeComponent();
            DataContext = vm;
            //  页面可见时启动实时刷新（活跃应用秒级更新 + 5 秒整表刷新），
            //  离开页面时停止，避免后台空转
            Loaded += (_, __) =>
            {
                vm.RefreshBrowserExtensionStatus();
                vm.StartLiveUpdates();
                vm.StartAutoRefresh();
            };
            Unloaded += (_, __) =>
            {
                vm.StopLiveUpdates();
                vm.StopAutoRefresh();
            };
        }
    }
}
