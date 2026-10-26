using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

namespace GuageBug
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void LogHyperlinkClick(object sender, RoutedEventArgs e)
        {
            string installDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string storageRoot = ReadStorageRoot(installDirectory);
            string loggerName = System.IO.Path.Combine(storageRoot,
                         "Log", DateTime.Now.ToString("yyyy-MM-dd") + ".log");
            if (File.Exists(loggerName))
            {
                System.Diagnostics.Process.Start("explorer.exe", "/select, " + loggerName);
            }
        }

        private static string ReadStorageRoot(string installDirectory)
        {
            try
            {
                string pointerPath = System.IO.Path.Combine(installDirectory, "storage.path");
                if (!File.Exists(pointerPath))
                {
                    return installDirectory;
                }

                string customPath = File.ReadAllText(pointerPath).Trim();
                if (string.IsNullOrEmpty(customPath))
                {
                    return installDirectory;
                }

                customPath = System.IO.Path.GetFullPath(customPath);
                return Directory.Exists(customPath) ? customPath : installDirectory;
            }
            catch
            {
                return installDirectory;
            }
        }
        private void IssuesHyperlinkClick(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://github.com/YOUR_GITHUB/guage/issues/new"));
        }
        private void EmailHyperlinkClick(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText("heis@thelittlepandaisbehind.com");
            MessageBox.Show("邮箱已复制");
        }
        private void Restart(object sender, RoutedEventArgs e)
        {
            string guagePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "guage.exe");
            if (File.Exists(guagePath))
            {
                Process.Start(guagePath);
            }
            else
            {
                MessageBox.Show("guage.exe 似乎已被删除", "重启失败提示", MessageBoxButton.OK, MessageBoxImage.Error);
                Process.Start(new ProcessStartInfo("https://github.com/YOUR_GITHUB/guage"));
            }
            Close();
        }
    }
}
