using Core.Enums;
using Core.Event;
using Core.Librarys;
using Core.Librarys.Security;
using Core.Librarys.Browser;
using Core.Librarys.Browser.Favicon;
using Core.Librarys.SQLite;
using Core.Models;
using Core.Models.Config;
using Core.Models.Config.Link;
using Core.Servicers.Interfaces;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Threading;

namespace Core.Servicers.Instances
{
    public class Main : IMain
    {
        private readonly IAppObserver appObserver;
        private readonly IData data;
        private readonly ISleepdiscover sleepdiscover;
        private readonly IAppConfig appConfig;
        private readonly IAppData appData;
        private readonly ICategorys categories;
        private readonly IWebFilter _webFilter;
        private readonly IAppTimerServicer _appTimer;
        private readonly IWebServer _webServer;
        private readonly IWebData _webData;
        private readonly ITimeLimitServicer _timeLimitServicer;
        //  忽略的进程
        private readonly string[] DefaultIgnoreProcess = new string[] {
            "guage",
            "SearchHost",
            "Taskmgr",
            "ApplicationFrameHost",
            "StartMenuExperienceHost",
            "ShellExperienceHost",
            "OpenWith",
            "Updater",
            "LockApp",
            "dwm",
            "SystemSettingsAdminFlows"
        };

        /// <summary>
        /// 睡眠状态
        /// </summary>
        private SleepStatus sleepStatus;

        /// <summary>
        /// app config
        /// </summary>
        private ConfigModel config;

        public event EventHandler OnUpdateTime;
        public event EventHandler OnStarted;
        public event EventHandler<WebBrowseUpdatedEventArgs> OnWebBrowseUpdated;

        /// <summary>
        /// 忽略进程缓存列表
        /// </summary>
        private List<string> IgnoreProcessCacheList;

        /// <summary>
        /// 配置正则忽略进程列表
        /// </summary>
        private List<string> ConfigIgnoreProcessRegxList;
        /// <summary>
        /// 配置忽略进程名称列表
        /// </summary>
        private List<string> ConfigIgnoreProcessList;
        //  更新应用日期
        private DateTime updateAppDateTime_ = DateTime.Now.Date;
        //  已经更新过的应用列表
        private List<string> updatedAppList = new List<string>();
        private List<string> _configProcessNameWhiteList, _configProcessRegexWhiteList;
        public Main(
            IAppObserver appObserver,
            IData data,
            ISleepdiscover sleepdiscover,
            IAppConfig appConfig,
            IAppData appData, ICategorys categories,
            IWebFilter webFilter_,
            IAppTimerServicer appTimer_,
            IWebServer webServer_,
            IWebData webData_,
            ITimeLimitServicer timeLimitServicer_)
        {
            this.appObserver = appObserver;
            this.data = data;
            this.sleepdiscover = sleepdiscover;
            this.appConfig = appConfig;
            this.appData = appData;
            this.categories = categories;
            _webFilter = webFilter_;
            _appTimer = appTimer_;
            _webServer = webServer_;
            _webData = webData_;
            _timeLimitServicer = timeLimitServicer_;

            IgnoreProcessCacheList = new List<string>();
            ConfigIgnoreProcessRegxList = new List<string>();
            ConfigIgnoreProcessList = new List<string>();
            _configProcessNameWhiteList = new List<string>();
            _configProcessRegexWhiteList = new List<string>();

            sleepdiscover.SleepStatusChanged += Sleepdiscover_SleepStatusChanged;
            appConfig.ConfigChanged += AppConfig_ConfigChanged;
            _appTimer.OnAppDurationUpdated += _appTimer_OnAppDurationUpdated;
            WebSocketEvent.OnWebLog += WebSocketEvent_OnWebLog;
        }

        private void AppConfig_ConfigChanged(ConfigModel oldConfig, ConfigModel newConfig)
        {
            //  oldConfig 是保存前的深拷贝快照，引用比较恒不等——
            //  原写法导致每次落盘（窗口尺寸/滑块防抖）都重删重建开机自启计划任务
            if (oldConfig == null || newConfig == null || oldConfig == newConfig)
            {
                return;
            }

            if (oldConfig.General.IsStartatboot != newConfig.General.IsStartatboot)
            {
                //  处理开机自启：仅在开关实际变化时重建计划任务
                if (!SystemCommon.SetStartup(newConfig.General.IsStartatboot))
                {
                    //  写计划任务需要提权，失败很常见。不回滚的话开关显示“已开启”、
                    //  实际开机不会启动；这里只回滚内存值，不在 ConfigChanged 里再 Save（会递归）
                    newConfig.General.IsStartatboot = oldConfig.General.IsStartatboot;
                    Logger.Error("设置开机自启失败，已回滚开关状态（写入计划任务需要管理员权限）");
                }
            }

            //  更新忽略规则
            UpdateConfigIgnoreProcess();

            //  更新白名单
            UpdateConfigProcessWhiteList();

            //  处理web记录功能启停
            HandleWebServiceConfig();

            if (oldConfig.General.BrowserExtensionToken != newConfig.General.BrowserExtensionToken)
            {
                WebSocketSecurity.SetToken(newConfig.General.BrowserExtensionToken);
                _webServer?.SendMsg("token_rotated");
            }
        }

        public async void Run()
        {
            //  加载应用配置（存储路径等信息必须在数据库初始化之前就绪）
            appConfig.Load();
            config = appConfig.GetConfig();
            if (config == null)
            {
                //  配置缺失（文件为空等）时重建默认配置，避免后续 NRE 崩溃
                Logger.Error("配置加载结果为空，已重建默认配置");
                appConfig.CreateDefaultConfig();
                appConfig.Save();
                config = appConfig.GetConfig();
            }
            UpdateConfigIgnoreProcess();
            UpdateConfigProcessWhiteList();

            try
            {
                await Task.Run(() =>
                 {
                     CreateDirectory();

                     //  数据库自检（损坏时尝试用备份恢复，避免启动即崩溃）
                     TryRecoverDatabase(() =>
                     {
                         using (var db = new GuageDbContext())
                         {
                             db.SelfCheck();
                         }
                     });

                     //  加载app信息
                     appData.Load();

                     // 加载分类信息
                     categories.Load();

                     //  预加载浏览器上报 MsgId 去重表（跨重启防重放）
                     _webData.PreloadMsgIdDedup();

                     AppState.IsLoading = false;
                 });
            }
            catch (Exception ex)
            {
                Logger.Error("主服务初始化异常：" + ex);
            }
            finally
            {
                //  初始化失败也不能卡在加载态（托盘与界面依赖此状态）
                AppState.IsLoading = false;
            }

            //  初始化过滤器
            _webFilter.Init();

            //  启动主服务
            Start();

            OnStarted?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 数据库自检；捕获损坏异常并用最近的备份副本替换后重试一次
        /// </summary>
        private void TryRecoverDatabase(Action selfCheck)
        {
            try
            {
                selfCheck();
                return;
            }
            catch (Exception ex)
            {
                Logger.Error("数据库自检失败，尝试用备份恢复：" + ex.Message);
            }

            try
            {
                string dbFile = FileHelper.GetDatabasePath();
                string backup = Path.Combine(
                    Path.GetDirectoryName(dbFile), "backup", "data.handle.backup");
                if (File.Exists(backup))
                {
                    File.Copy(backup, dbFile, true);
                    Logger.Info("已用备份数据库恢复：" + backup);
                    selfCheck();
                    return;
                }
                Logger.Error("无可用备份数据库，带病运行（读写可能持续报错）");
            }
            catch (Exception recoverEx)
            {
                Logger.Error("数据库备份恢复失败：" + recoverEx);
            }
        }
        public void Start()
        {
            //  appTimer必须比Observer先启动*
            _appTimer.Start();
            appObserver.Start();
            if (config.General.IsWebEnabled)
            {
                _webServer.Start();
            }
            if (config.Behavior.IsSleepWatch)
            {
                //  启动睡眠监测
                sleepdiscover.Start();
            }
            _timeLimitServicer.Start();
        }
        public void Stop()
        {
            _timeLimitServicer.Stop();
            appObserver.Stop();
            _appTimer.Stop();
            _webServer.Stop();
        }
        public void Exit()
        {
            appObserver?.Stop();
        }

        /// <summary>
        /// 创建程序目录
        /// </summary>
        private void CreateDirectory()
        {
            FileHelper.EnsureStorageDirectories();
        }

        private void UpdateConfigIgnoreProcess()
        {
            if (config == null)
            {
                return;
            }
            ConfigIgnoreProcessList.Clear();
            ConfigIgnoreProcessRegxList.Clear();
            IgnoreProcessCacheList.Clear();

            ConfigIgnoreProcessList = config.Behavior.IgnoreProcessList.Where(m => !IsRegex(m)).ToList();
            ConfigIgnoreProcessRegxList = config.Behavior.IgnoreProcessList.Where(m => IsRegex(m)).ToList();
        }

        private void UpdateConfigProcessWhiteList()
        {
            if (config == null)
            {
                return;
            }
            _configProcessNameWhiteList.Clear();
            _configProcessRegexWhiteList.Clear();

            _configProcessNameWhiteList = config.Behavior.ProcessWhiteList.Where(m => !IsRegex(m)).ToList();
            _configProcessRegexWhiteList = config.Behavior.ProcessWhiteList.Where(m => IsRegex(m)).ToList();
        }

        private bool IsRegex(string str)
        {
            return Regex.IsMatch(str, @"[\.|\*|\?|\{|\\|\[|\^|\|]");
        }

        private void Sleepdiscover_SleepStatusChanged(Enums.SleepStatus sleepStatus)
        {
            this.sleepStatus = sleepStatus;

            Logger.Info($"[{sleepStatus}]");
            if (sleepStatus == SleepStatus.Sleep)
            {
                //  进入睡眠状态
                Debug.WriteLine("进入睡眠状态");

                //  通知sokcet客户端
                _webServer?.SendMsg("sleep");
                //  停止服务
                Stop();

                //  更新时间
                UpdateAppDuration();
            }
            else
            {
                //  从睡眠状态唤醒
                Debug.WriteLine("从睡眠状态唤醒");

                _webServer?.SendMsg("wake");

                Start();
            }
        }


        /// <summary>
        /// 检查应用是否需要记录数据
        /// </summary>
        /// <param name="processName"></param>
        /// <param name="description"></param>
        /// <param name="file"></param>
        private bool IsCheckApp(string processName, string description, string file)
        {
            if (string.IsNullOrEmpty(file) || string.IsNullOrEmpty(processName) || DefaultIgnoreProcess.Contains(processName) || IgnoreProcessCacheList.Contains(processName))
            {
                return false;
            }

            //  从名称判断
            if (ConfigIgnoreProcessList.Contains(processName))
            {
                return false;
            }

            //  正则表达式
            foreach (string reg in ConfigIgnoreProcessRegxList)
            {
                if (RegexHelper.IsMatch(processName, reg) || RegexHelper.IsMatch(file, reg))
                {
                    IgnoreProcessCacheList.Add(processName);
                    return false;
                }
            }

            //  应用白名单过滤
            if (config.Behavior.IsWhiteList && config.Behavior.ProcessWhiteList.Count > 0)
            {
                bool isWhite = false;
                //  通过进程名称判断
                if (_configProcessNameWhiteList.Contains(processName))
                {
                    isWhite = true;
                }
                else
                {
                    //  进程名称中匹配不到时尝试正则表达式
                    foreach (string reg in _configProcessRegexWhiteList)
                    {
                        if (RegexHelper.IsMatch(processName, reg) || RegexHelper.IsMatch(file, reg))
                        {
                            isWhite = true;
                            break;
                        }
                    }
                }
                //  白名单中找不到时不统计
                Debug.WriteLine("白名单过滤结果：" + processName + " -> " + isWhite);
                if (!isWhite) return false;
            }

            AppModel app = appData.GetApp(processName);
            if (app == null)
            {
                //  记录应用信息

                //  提取icon
                string iconFile = Iconer.ExtractFromFile(file, processName, description);

                appData.AddApp(new AppModel()
                {
                    Name = processName,
                    Description = description,
                    File = file,
                    CategoryID = 0,
                    IconFile = iconFile,
                });
            }
            else
            {
                if (updateAppDateTime_ != DateTime.Now.Date)
                {
                    updateAppDateTime_ = DateTime.Now.Date;
                    updatedAppList.Clear();
                }

                if (!updatedAppList.Contains(processName))
                {
                    //  更新应用信息
                    app.IconFile = Iconer.ExtractFromFile(file, processName, description);

                    if (app.Description != description)
                    {
                        app.Description = description;
                    }
                    if (app.File != file)
                    {
                        app.File = file;
                    }
                    appData.UpdateApp(app);
                    updatedAppList.Add(processName);
                }
            }

            return true;
        }

        private void HandleLinks(string processName, int seconds, DateTime time)
        {
            Task.Run(() =>
            {
                try
                {
                    List<LinkModel> links = config.Links != null ? config.Links : new List<LinkModel>();
                    foreach (LinkModel link in links)
                    {
                        if (link.ProcessList != null
                            && link.ProcessList.Count >= 2
                            && link.ProcessList.Contains(processName))
                        {
                            //  属于关联进程
                            foreach (string linkProcess in link.ProcessList)
                            {
                                if (linkProcess != processName)
                                {
                                    if (IsProcessRuning(linkProcess))
                                    {
                                        //  同步更新
                                        data.UpdateAppDuration(linkProcess, seconds, time);
                                    }

                                }
                            }
                            break;
                        }
                    }

                }
                catch (Exception ex)
                {
                    Logger.Error(ex.Message + "，关联进程更新错误，Process Name: " + processName + "，Time: " + seconds);
                }
            });
        }

        #region 判断进程是否在运行中
        /// <summary>
        /// 判断进程是否在运行中
        /// </summary>
        /// <param name="processName"></param>
        /// <returns></returns>
        private bool IsProcessRuning(string processName)
        {
            Process[] process = Process.GetProcessesByName(processName);
            if (process == null || process.Length == 0)
            {
                return false;
            }

            //  Process 句柄必须释放：本方法随每次会话结算高频调用，不释放会持续累积
            foreach (var p in process)
            {
                try
                {
                    if (!p.HasExited)
                    {
                        return true;
                    }
                }
                catch
                {
                    //  进程可能刚好退出
                }
                finally
                {
                    p.Dispose();
                }
            }
            return false;
        }
        #endregion

        #region 处理网站数据记录配置项开关
        /// <summary>
        /// 处理网站数据记录配置项开关
        /// </summary>
        private void HandleWebServiceConfig()
        {
            if (config == null)
            {
                return;
            }

            if (config.General.IsWebEnabled)
            {
                _webServer.Start();
            }
            else
            {
                _webServer.Stop();
            }
        }
        #endregion

        private void _appTimer_OnAppDurationUpdated(object sender, Event.AppDurationUpdatedEventArgs e)
        {
            UpdateAppDuration(e);
        }

        // 在途写库任务（退出时等待，防止会话尾部数据丢失）
        private readonly object _pendingWritesLock = new object();
        private readonly List<Task> _pendingWrites = new List<Task>();

        private void UpdateAppDuration()
        {
            UpdateAppDuration(_appTimer.GetAppDuration());
        }
        private void UpdateAppDuration(AppDurationUpdatedEventArgs e)
        {
            if (e == null) return;

            // DB writes moved off the UI thread: a lock wait here froze the UI (stats stuck)
            var writeTask = Task.Run(() =>
            {
                try
                {
                    var app = e.App;
                    int duration = e.Duration;
                    DateTime startTime = e.ActiveTime;

                    bool isCheck = IsCheckApp(app.Process, app.Description, app.ExecutablePath);
                    if (isCheck)
                    {
                        data.UpdateAppDuration(app.Process, duration, startTime);
                        HandleLinks(app.Process, duration, startTime);
                        DispatchCateogry(app.Process, app.ExecutablePath);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex.ToString());
                }
            });

            lock (_pendingWritesLock)
            {
                _pendingWrites.RemoveAll(t => t.IsCompleted);
                _pendingWrites.Add(writeTask);
            }

            OnUpdateTime?.Invoke(this, null);
        }

        /// <summary>
        /// 退出前结算当前前台会话并等待在途写库完成（防止会话尾部时长静默丢失）
        /// </summary>
        public void FlushCurrentSession()
        {
            try
            {
                // 停表并强制提交一次（触发 OnAppDurationUpdated → UpdateAppDuration 登记）
                _appTimer.Flush();
            }
            catch (Exception ex)
            {
                Logger.Error("FlushCurrentSession 结算当前会话失败：" + ex);
            }

            // 等待在途写库落盘（限时，避免退出被慢 I/O 卡死）
            Task[] pending;
            lock (_pendingWritesLock)
            {
                pending = _pendingWrites.ToArray();
            }
            try
            {
                Task.WaitAll(pending, 5000);
            }
            catch
            {
                // 超时/聚合异常不阻塞退出
            }
        }

        #region 浏览器记录
        private void WebSocketEvent_OnWebLog(Models.WebPage.NotifyWeb args)
        {
            try
            {
                if (args == null || !UrlSecurity.TryCreateSafeHttpUri(args.Url, out _))
                {
                    return;
                }

                if (_webFilter.IsIgnore(args.Url))
                {
                    Debug.WriteLine($"URL已被过滤，{args.Url}");
                    return;
                }

                //  记录数据
                var site = new Models.WebPage.Site()
                {
                    Url = args.Url,
                    Title = args.Title
                };

                _webData.AddUrlBrowseTime(site, args.Duration, args.ActiveDateTime, args.MsgId);
                OnWebBrowseUpdated?.Invoke(this, new WebBrowseUpdatedEventArgs(UrlHelper.GetDomain(args.Url), args.Duration));

                //  处理图标：多级回退策略，尽量保证任何站点（含哔哩哔哩等国内受限站点）
                //  都能拿到图标。候选顺序：网页母图标 → DNSPod favicon 代理（国内）→
                //  iowen favicon 服务（国内）→ Google favicon（64px）→ 站点根 favicon.ico
                Task.Run(async () =>
                {
                    if (!NetworkSecurity.IsSafeFaviconUrl(args.Icon, args.Url))
                    {
                        return;
                    }

                    string saveName = UrlHelper.GetName(args.Url) + DateTime.Now.ToString("yyyyMM") + ".ico";
                    string domain = UrlHelper.GetDomain(args.Url);
                    string path = await FaviconDownloader.DownloadAsync(args.Icon, saveName, args.Url);

                    if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(domain))
                    {
                        path = await FaviconDownloader.DownloadAsync(
                            "https://statics.dnspod.cn/proxy_favicon/_/favicon?domain=" + domain,
                            saveName,
                            args.Url);
                    }

                    if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(domain))
                    {
                        path = await FaviconDownloader.DownloadAsync(
                            "https://api.iowen.cn/favicon/" + domain + ".png",
                            saveName,
                            args.Url);
                    }

                    if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(domain))
                    {
                        path = await FaviconDownloader.DownloadAsync(
                            "https://www.google.com/s2/favicons?domain=" + domain + "&sz=64",
                            saveName,
                            args.Url);
                    }

                    if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(domain))
                    {
                        path = await FaviconDownloader.DownloadAsync(
                            "https://" + domain + "/favicon.ico",
                            saveName,
                            args.Url);
                    }

                    if (!string.IsNullOrEmpty(path))
                    {
                        _webData.UpdateUrlFavicon(site, path);
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex.ToString());
            }
        }
        #endregion

        #region 自动分类
        /// <summary>
        /// 自动分类
        /// </summary>
        /// <param name="processName_">进程名称</param>
        private void DispatchCateogry(string processName_, string executablePath_)
        {
            try
            {
                AppModel app = appData.GetApp(processName_);
                if (app != null)
                {
                    var categoryList = categories.GetCategories().Where(c => c.IsDirectoryMath && c.DirectoryList.Count > 0).ToList();
                    CategoryModel mathCategory = null;
                    foreach (var category in categoryList)
                    {
                        if (mathCategory != null)
                        {
                            break;
                        }
                        foreach (var item in category.DirectoryList)
                        {
                            if (string.IsNullOrWhiteSpace(item))
                            {
                                continue;
                            }

                            if (!string.IsNullOrEmpty(executablePath_)
                                && (executablePath_.Equals(item, StringComparison.OrdinalIgnoreCase)
                                    || executablePath_.StartsWith(item.TrimEnd('\\') + @"\",
                                        StringComparison.OrdinalIgnoreCase)))
                            {
                                mathCategory = category;
                                Debug.WriteLine("匹配成功：" + category.Name);
                                break;
                            }
                        }

                    }
                    if (mathCategory != null)
                    {
                        //  匹配成功
                        app.Category = mathCategory;
                        app.CategoryID = mathCategory.ID;
                        appData.UpdateApp(app);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex.ToString());
            }
        }
        #endregion
    }
}
