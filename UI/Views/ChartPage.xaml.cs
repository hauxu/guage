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
    /// ChartPage.xaml 的交互逻辑
    /// </summary>
    public partial class ChartPage : Page
    {
        public ChartPage(ChartPageVM vm)
        {
            InitializeComponent();
            DataContext = vm;
            //  页面可见时启动自动刷新，离开时停止（仅当前页刷新，后台零开销）
            Loaded += (_, __) => vm.StartAutoRefresh();
            Unloaded += (_, __) => vm.StopAutoRefresh();
        }
    }
}
