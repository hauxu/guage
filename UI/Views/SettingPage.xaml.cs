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
    /// SettingPage.xaml 的交互逻辑
    /// </summary>
    public partial class SettingPage : Page
    {
        public SettingPage(SettingPageVM vm)
        {
            InitializeComponent();

            DataContext = vm;

            //  面板直写 config 后（同引用，DP 不再通知），由编辑事件驱动
            //  即时外观预览 + 防抖落盘；拖动提交完成时才做重操作（accent 提交）
            ConfigPanel.SettingEdited += (_, __) => vm.NotifySettingEdited(committed: false);
            ConfigPanel.SettingCommitted += (_, __) => vm.NotifySettingEdited(committed: true);
        }
    }
}
