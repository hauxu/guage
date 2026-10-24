using System;
using UI.Controls.Window;

namespace UI
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : DefaultWindow
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void OnClosed(EventArgs e)
        {
            //  每次重开主窗口都会新建 PageContainer 与整套路由页面 VM（Transient），
            //  关闭时释放缓存页面：退订旧 VM 持有的全局订阅
            //  （键盘钩子/ConfigChanged/ExtensionConnectionChanged/TierChanged），
            //  否则每次关开窗口都累积一套订阅
            PageContainer?.Dispose();
            base.OnClosed(e);
        }
    }
}

