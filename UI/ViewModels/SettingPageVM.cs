using Core.Librarys;
using Core.Librarys.Browser;
using Core.Librarys.Security;
using Core.Models.Config;
using Core.Models.Config.Link;
using Core.Servicers.Instances;
using Core.Servicers.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using UI.Controls;
using UI.Models;
using UI.Servicers;

namespace UI.ViewModels
{
    public class SettingPageVM : SettingPageModel, IDisposable
    {
        private bool _disposed;
        private ConfigModel config;
        private readonly IAppConfig appConfig;
        private readonly MainViewModel mainVM;
        private readonly IData data;
        private readonly IWebData _webData;
        private readonly IUIServicer _uiServicer;
        private readonly IWebServer _webServer;
        private readonly IThemeServicer _themeServicer;
        //  配置写盘防抖：拖动滑块时每个 mouse-move 都会走到 Save（全量 JSON 写盘），
        //  防抖后只在停止变更 500ms 后落盘（内存值即时更新，预览不受影响）
        private System.Windows.Threading.DispatcherTimer _saveDebounceTimer;
        public Command OpenURL { get; set; }
        public Command CheckUpdate { get; set; }
        public Command DelDataCommand { get; set; }
        public Command ExportDataCommand { get; set; }
        public Command OpenBrowserExtensionFolderCommand { get; set; }

        public SettingPageVM(IAppConfig appConfig, MainViewModel mainVM, IData data, IWebData webData, IUIServicer uiServicer_, IWebServer webServer, IThemeServicer themeServicer)
        {
            this.appConfig = appConfig;
            this.mainVM = mainVM;
            this.data = data;
            _webData = webData;
            _uiServicer = uiServicer_;
            _webServer = webServer;
            _themeServicer = themeServicer;

            OpenURL = new Command(new Action<object>(OnOpenURL));
            CheckUpdate = new Command(new Action<object>(OnCheckUpdate));
            DelDataCommand = new Command(new Action<object>(OnDelData));
            ExportDataCommand = new Command(new Action<object>(OnExportData));
            OpenBrowserExtensionFolderCommand = new Command(new Action<object>(OnOpenBrowserExtensionFolder));

            Init();
        }

        /// <summary>
        /// 防抖保存：连续变更只落盘最后一次
        /// </summary>
        private void SaveDebounced()
        {
            if (_saveDebounceTimer == null)
            {
                _saveDebounceTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                _saveDebounceTimer.Tick += (s, e) =>
                {
                    _saveDebounceTimer.Stop();
                    appConfig.Save();
                };
            }
            _saveDebounceTimer.Stop();
            _saveDebounceTimer.Start();
        }

        /// <summary>
        /// 设置面板编辑回调：committed=false（拖动中）只做轻量笔刷预览；
        /// committed=true（松手提交）才执行 accent 重提交等重操作
        /// </summary>
        public void NotifySettingEdited(bool committed = true)
        {
            _themeServicer?.ApplyAppearancePreview(committed);
            SaveDebounced();
        }



        private void OnCheckUpdate(object obj)
        {
            try
            {
                string updaterExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "Updater.exe");
                string updaterCacheExePath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Update",
                    "Updater.exe");
                string updateDirPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Update");
                if (!Directory.Exists(updateDirPath))
                {
                    Directory.CreateDirectory(updateDirPath);
                }

                if (!File.Exists(updaterExePath))
                {
                    mainVM.Toast("升级程序不存在，请到发布页手动下载", Controls.Window.ToastType.Error, Controls.Base.IconTypes.None);
                    return;
                }
                File.Copy(updaterExePath, updaterCacheExePath, true);

                File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Newtonsoft.Json.dll"), Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Update",
                    "Newtonsoft.Json.dll"), true);

                ProcessHelper.Run(updaterCacheExePath, new string[] { Version });
                mainVM.Toast("已打开升级程序", Controls.Window.ToastType.Info, Controls.Base.IconTypes.None);
            }
            catch (Exception ex)
            {
                Logger.Error(ex.Message);
                mainVM.Toast("无法启动升级程序", Controls.Window.ToastType.Error, Controls.Base.IconTypes.None);
            }
        }

        private void OnOpenURL(object obj)
        {
            string url = obj?.ToString();
            if (!UrlSecurity.TryLaunchUrl(url))
            {
                mainVM.Toast("无法打开链接", Controls.Window.ToastType.Warning, Controls.Base.IconTypes.None);
            }
        }

        private void OnOpenBrowserExtensionFolder(object obj)
        {
            try
            {
                if (!BrowserExtensionPathHelper.TryResolveDirectory(out string path))
                {
                    mainVM.Toast("未找到本地扩展目录", Controls.Window.ToastType.Info, Controls.Base.IconTypes.None);
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
                mainVM.Toast("在扩展页开启开发者模式，「加载解压缩的扩展」指向此文件夹", Controls.Window.ToastType.Info, Controls.Base.IconTypes.None);
            }
            catch (Exception ex)
            {
                Logger.Error(ex.ToString());
                mainVM.Toast("无法打开扩展目录", Controls.Window.ToastType.Error, Controls.Base.IconTypes.IncidentTriangle);
            }
        }

        private void Init()
        {
            config = appConfig.GetConfig();

            Data = config.General;

            TabbarData = new System.Collections.ObjectModel.ObservableCollection<string>()
            {
                "常规", "关联", "行为", "数据", "关于"
            };

            PropertyChanged += SettingPageVM_PropertyChanged;

            Version = Assembly.GetExecutingAssembly().GetName().Version.ToString();

            SetExtensionInstallUrls(
                BrowserExtensionInstallInfo.ChromeWebStoreUrl,
                BrowserExtensionInstallInfo.EdgeAddOnsUrl,
                BrowserExtensionInstallInfo.InstallGuideUrl);

            DelDataStartMonthDate = DateTime.Now;
            DelDataEndMonthDate = DateTime.Now;

            ExportDataStartMonthDate = DateTime.Now;
            ExportDataEndMonthDate = DateTime.Now;

            appConfig.StoragePathChanged += AppConfig_StoragePathChanged;
            appConfig.ConfigChanged += AppConfig_ConfigChanged;
        }

        /// <summary>
        /// 释放页面订阅：退订单例 appConfig 的事件，避免旧 VM 被长期持有。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            appConfig.StoragePathChanged -= AppConfig_StoragePathChanged;
            appConfig.ConfigChanged -= AppConfig_ConfigChanged;
            PropertyChanged -= SettingPageVM_PropertyChanged;
        }

        private void AppConfig_ConfigChanged(ConfigModel oldConfig, ConfigModel newConfig)
        {
            //  Save 可能在后台线程触发：Toast 展示必须回 UI 线程
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() => AppConfig_ConfigChanged(oldConfig, newConfig)));
                return;
            }

            if (newConfig?.General?.IsWebEnabled != true || _webServer.IsRunning)
            {
                return;
            }

            string detail = string.IsNullOrWhiteSpace(_webServer.LastStartError)
                ? "网站统计服务未能启动，端口可能被占用"
                : _webServer.LastStartError;
            mainVM.Toast(detail, Controls.Window.ToastType.Warning, Controls.Base.IconTypes.IncidentTriangle);
        }

        private async void AppConfig_StoragePathChanged()
        {
            try
            {
                bool restartNow = await _uiServicer.ShowConfirmDialogAsync(
                    "存储位置已更改",
                    "存储位置已更新，需重启后完全生效。是否立即重启？");

                if (!restartNow)
                {
                    mainVM.Toast("请稍后手动重启，新位置才会生效", Controls.Window.ToastType.Info);
                    return;
                }

                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                System.Diagnostics.Process.Start(exePath);
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Logger.Error(ex.ToString());
                mainVM.Toast("无法自动重启，请手动重开软件", Controls.Window.ToastType.Error, Controls.Base.IconTypes.IncidentTriangle);
            }
        }



        private void SettingPageVM_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Data))
            {
                if (TabbarSelectedIndex == 0)
                {
                    config.General = Data as GeneralModel;
                }
                else if (TabbarSelectedIndex == 1)
                {
                    if (Data != null)
                    {
                        var newData = new List<LinkModel>();
                        foreach (var item in Data as IEnumerable<object>)
                        {
                            newData.Add(item as LinkModel);
                            Debug.WriteLine(item.ToString());
                        }
                        config.Links = newData;
                    }

                }
                else if (TabbarSelectedIndex == 2)
                {
                    config.Behavior = Data as BehaviorModel;
                }

                SaveDebounced();
            }

            if (e.PropertyName == nameof(TabbarSelectedIndex))
            {
                if (TabbarSelectedIndex == 0)
                {
                    Data = config.General;
                }
                else if (TabbarSelectedIndex == 1)
                {
                    Data = config.Links;
                }
                else if (TabbarSelectedIndex == 2)
                {
                    Data = config.Behavior;
                }
            }
        }

        private async void OnDelData(object obj)
        {
            try
            {
                if (DelDataStartMonthDate > DelDataEndMonthDate)
                {
                    mainVM.Toast("时间范围选择错误", Controls.Window.ToastType.Error, Controls.Base.IconTypes.IncidentTriangle);
                    return;
                }

                bool isConfirm = await _uiServicer.ShowConfirmDialogAsync("删除确认", "是否执行此操作？");
                if (!isConfirm)
                {
                    return;
                }

                //  删除数据在后台执行，避免阻塞 UI
                await Task.Run(() =>
                {
                    data.ClearRange(DelDataStartMonthDate, DelDataEndMonthDate);
                    _webData.Clear(DelDataStartMonthDate, DelDataEndMonthDate);
                });

                mainVM.Toast("操作已完成", Controls.Window.ToastType.Success);
            }
            catch (Exception ex)
            {
                Logger.Error(ex.ToString());
                mainVM.Toast("删除数据失败", Controls.Window.ToastType.Error, Controls.Base.IconTypes.IncidentTriangle);
            }
        }

        private async void OnExportData(object obj)
        {
            try
            {
                FolderBrowserDialog dialog = new FolderBrowserDialog();
                dialog.Description = "请选择导出位置";
                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                string selectedPath = dialog.SelectedPath;
                //  导出在后台执行，避免阻塞 UI
                await Task.Run(() =>
                {
                    data.ExportToExcel(selectedPath, ExportDataStartMonthDate, ExportDataEndMonthDate);
                    _webData.Export(selectedPath, ExportDataStartMonthDate, ExportDataEndMonthDate);
                });

                mainVM.Toast("导出数据完成", Controls.Window.ToastType.Success);
            }
            catch (Exception ec)
            {
                Logger.Error(ec.ToString());
                mainVM.Toast("导出数据失败", Controls.Window.ToastType.Error, Controls.Base.IconTypes.IncidentTriangle);
            }
        }
    }
}
