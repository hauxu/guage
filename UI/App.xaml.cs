using Core;
using Core.Librarys;
using Core.Librarys.SQLite;
using Core.Servicers.Instances;
using Core.Servicers.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Resources;
using UI.Base.Animation;
using UI.Controls.Window;
using UI.Servicers;
using UI.ViewModels;
using UI.Views;

namespace UI
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        private readonly ServiceProvider serviceProvider;
        private System.Threading.Mutex mutex;
        //  保活窗口
        private HideWindow keepaliveWindow;

        public App()
        {
            FileHelper.Initialize();

            DispatcherUnhandledException += App_DispatcherUnhandledException;

            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            serviceProvider = serviceCollection.BuildServiceProvider();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            //  退出前结算当前前台会话并等待在途写库，防止统计时长静默丢失
            try
            {
                var main = serviceProvider?.GetService<IMain>();
                main?.FlushCurrentSession();
            }
            catch (Exception flushEx)
            {
                Logger.Error("退出结算失败：" + flushEx);
            }

            //  设置页防抖保存的兜底：确保内存中的配置变更落盘。
            //  仅在配置已加载时保存——第二实例直接 Shutdown 时配置从未 Load，
            //  此时 Save 会把字面量 "null" 写进配置文件，导致下次启动全量重置
            try
            {
                var appConfig = serviceProvider?.GetService<IAppConfig>();
                if (appConfig != null && appConfig.IsLoaded)
                {
                    appConfig.Save();
                }
            }
            catch (Exception saveEx)
            {
                Logger.Error("退出保存配置失败：" + saveEx);
            }

            AnimationBudget.DetachWindow();
            base.OnExit(e);
            Logger.Save(true);
        }
        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;

            //  记录崩溃错误
            Logger.Error("[程序崩溃异常] " + e.Exception.ToString());
            Logger.Save(true);

            //  显示崩溃弹窗（文件缺失时跳过，避免崩溃后二次报错刷日志）
            string guageBugPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "GuageBug.exe");
            if (File.Exists(guageBugPath))
            {
                ProcessHelper.Run(guageBugPath, new string[] { string.Empty });
            }

            //  退出程序
            Shutdown();
        }

        #region 获取当前程序是否已运行
        /// <summary>
        /// 获取当前程序是否已运行
        /// </summary>
        private bool IsRuned()
        {
            bool ret;
            mutex = new System.Threading.Mutex(true, System.Reflection.Assembly.GetEntryAssembly().ManifestModule.Name, out ret);
            if (!ret)
            {
                //  多开一律拦截：每个实例退出时都会全量保存配置，
                //  旧实例的陈旧快照会把新实例改过的设置整体回滚
                return true;
            }
            return false;
        }
        #endregion

        private void ConfigureServices(IServiceCollection services)
        {
            //  核心服务
            services.AddSingleton<IDatabase, Database>();
            services.AddSingleton<IAppManager, AppManager>();
            services.AddSingleton<IWindowManager, WindowManager>();
            services.AddSingleton<IAppTimerServicer, AppTimerServicer>();
            services.AddSingleton<IAppObserver, AppObserver>();
            services.AddSingleton<IWebServer, WebServer>();
            services.AddSingleton<IMain, Main>();
            services.AddSingleton<IData, Data>();
            services.AddSingleton<IWebData, WebData>();
            services.AddSingleton<ISleepdiscover, Sleepdiscover>();
            services.AddSingleton<ITimeLimitNotifier, TimeLimitNotifierServicer>();
            services.AddSingleton<ITimeLimitServicer, TimeLimitServicer>();
            services.AddSingleton<IBreakReminderServicer, BreakReminderServicer>();
            services.AddSingleton<IAppConfig, AppConfig>();
            services.AddSingleton<IAppData, AppData>();
            services.AddSingleton<ICategorys, Categorys>();
            services.AddSingleton<IWebFilter, WebFilter>();

            //  UI服务
            services.AddSingleton<IUIServicer, UIServicer>();
            services.AddSingleton<IAppContextMenuServicer, AppContextMenuServicer>();
            services.AddSingleton<IThemeServicer, ThemeServicer>();
            services.AddSingleton<IMainServicer, MainServicer>();
            services.AddSingleton<IInputServicer, InputServicer>();
            services.AddSingleton<IWebSiteContextMenuServicer, WebSiteContextMenuServicer>();
            services.AddSingleton<IStatusBarIconServicer, StatusBarIconServicer>();
            services.AddSingleton<BreakReminderNotifierServicer>();
            services.AddSingleton<UsageDataRefreshServicer>();

            //  主窗口
            services.AddSingleton<MainViewModel>();

            //  首页
            services.AddTransient<IndexPage>();
            services.AddTransient<IndexPageVM>();

            //  数据页
            services.AddTransient<DataPage>();
            services.AddTransient<DataPageVM>();

            //  设置页
            services.AddTransient<SettingPage>();
            services.AddTransient<SettingPageVM>();

            //  时长限制
            services.AddTransient<TimeLimitPage>();
            services.AddTransient<TimeLimitPageVM>();

            //  详情页
            services.AddTransient<DetailPage>();
            services.AddTransient<DetailPageVM>();

            //  分类
            services.AddTransient<CategoryPage>();
            services.AddTransient<CategoryPageVM>();

            //  分类app
            services.AddTransient<CategoryAppListPage>();
            services.AddTransient<CategoryAppListPageVM>();
            //  分类站点
            services.AddTransient<CategoryWebSiteListPage>();
            services.AddTransient<CategoryWebSiteListPageVM>();
            //  图表
            services.AddTransient<ChartPage>();
            services.AddTransient<ChartPageVM>();
            //  网站详情
            services.AddTransient<WebSiteDetailPage>();
            services.AddTransient<WebSiteDetailPageVM>();

            // 休息提醒
            services.AddTransient<BreakReminderPage>();
            services.AddTransient<BreakReminderPageVM>();
        }

        private void OnStartup(object sender, StartupEventArgs e)
        {
            //  阻止多开进程
            if (IsRuned())
            {
                Shutdown();
                return;
            }

            var main = serviceProvider.GetService<IMainServicer>();

            bool isSelfStart = false;
            if (e.Args.Length != 0)
            {
                if (e.Args[0].Equals("--selfStart"))
                {
                    isSelfStart = true;
                }
            }
            main.Start(isSelfStart);

            // 激活休息提醒通知监听（订阅阶段完成事件，负责弹窗与托盘通知）
            serviceProvider.GetService<BreakReminderNotifierServicer>();

            //  创建保活窗口
            keepaliveWindow = new HideWindow();
        }
    }
}
