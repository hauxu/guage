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
using UI.ViewModels;

namespace UI.Views
{
    /// <summary>
    /// WebSiteDetailPage.xaml 的交互逻辑
    /// </summary>
    public partial class WebSiteDetailPage : Page
    {
        public WebSiteDetailPage(WebSiteDetailPageVM vm_)
        {
            InitializeComponent();
            DataContext = vm_;
            //  页面可见时启动自动刷新，离开时停止
            Loaded += (_, __) => vm_.StartAutoRefresh();
            Unloaded += (_, __) => vm_.StopAutoRefresh();
        }
    }
}
