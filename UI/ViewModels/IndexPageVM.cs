using Core.Event;
using Core.Librarys;
using Core.Librarys.Image;
using Core.Models;
using Core.Models.Db;
using Core.Servicers.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UI.Controls;
using UI.Controls.Charts.Model;
using UI.Controls.Select;
using UI.Models;
using UI.Servicers;
using UI.Views;
using UI.Base.Animation;

namespace UI.ViewModels
{
    public class IndexPageVM : IndexPageModel
    {
        public Command ToDetailCommand { get; set; }
        public Command RefreshCommand { get; set; }
        private readonly IData data;
        private readonly MainViewModel main;
        private readonly IMain mainServicer;        private readonly IAppContextMenuServicer appContextMenuServicer;
        private readonly IInputServicer inputServicer;
        private readonly IAppConfig appConfig;
        private readonly IWebData _webData;
        private readonly IWebSiteContextMenuServicer _webSiteContextMenu;
        private readonly IAppTimerServicer _appTimer;
        private readonly IWebServer _webServer;
        private readonly UsageDataRefreshServicer _refreshServicer;
        private readonly IAppData _appData;
        private readonly UI.Base.LoadGate _loadGate = new UI.Base.LoadGate();
        private DispatcherTimer _liveDurationTimer;
        private IDisposable _autoRefreshSubscription;
        public List<SelectItemModel> MoreTypeOptions { get; set; }
        public IndexPageVM(
            IData data,
            MainViewModel main,
            IMain mainServicer,
            IAppContextMenuServicer appContextMenuServicer,
            IInputServicer inputServicer,
            IAppConfig appConfig,
            IWebData webData_,
            IWebSiteContextMenuServicer webSiteContext_,
            IAppTimerServicer appTimer_,
            IWebServer webServer_,
            UsageDataRefreshServicer refreshServicer_,
            IAppData appData_)
        {
            this.data = data;
            this.main = main;
            this.mainServicer = mainServicer;
            this.appContextMenuServicer = appContextMenuServicer;
            this.inputServicer = inputServicer;
            this.appConfig = appConfig;
            _webData = webData_;
            _webSiteContextMenu = webSiteContext_;
            _appTimer = appTimer_;
            _webServer = webServer_;
            _refreshServicer = refreshServicer_;
            _appData = appData_;

            ToDetailCommand = new Command(new Action<object>(OnTodetailCommand));
            RefreshCommand = new Command(new Action<object>(OnRefreshCommand));

            Init();
        }

        private void OnRefreshCommand(object obj)
        {
            LoadData(showLoadingPlaceholder: true);
        }

        public override void Dispose()
        {
            AnimationBudget.TierChanged -= AnimationBudget_TierChanged;
            StopAutoRefresh();
            StopLiveUpdates();
            PropertyChanged -= IndexPageVM_PropertyChanged;
            inputServicer.OnKeyUpInput -= InputServicer_OnKeyUpInput;
            _webServer.ExtensionConnectionChanged -= WebServer_ExtensionConnectionChanged;
            appConfig.ConfigChanged -= AppConfig_ConfigChanged;
            base.Dispose();
        }

        private void Init()
        {
            TabbarData = new System.Collections.ObjectModel.ObservableCollection<string>()
            {
                "今日","本周"
            };

            TabbarSelectedIndex = 0;
            AppContextMenu = appContextMenuServicer.GetContextMenu();
            WebSiteContextMenu = _webSiteContextMenu.GetContextMenu();
            PropertyChanged += IndexPageVM_PropertyChanged;
            inputServicer.OnKeyUpInput += InputServicer_OnKeyUpInput;

            LoadData(showLoadingPlaceholder: true);

            MoreTypeOptions = new List<SelectItemModel>()
            {
                new SelectItemModel()
                {
                    Id=0,
                    Name="应用"
                },
                new SelectItemModel()
                {
                    Id=1,
                    Name="网站"
                }
            };
            MoreType = MoreTypeOptions[0];
            AnimationBudget.TierChanged += AnimationBudget_TierChanged;
            _webServer.ExtensionConnectionChanged += WebServer_ExtensionConnectionChanged;
            appConfig.ConfigChanged += AppConfig_ConfigChanged;
            UpdateBrowserExtensionStatus();
            StartLiveUpdates();
        }

        private void AppConfig_ConfigChanged(Core.Models.Config.ConfigModel oldConfig, Core.Models.Config.ConfigModel newConfig)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(UpdateBrowserExtensionStatus));
        }

        private void WebServer_ExtensionConnectionChanged(object sender, EventArgs e)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(UpdateBrowserExtensionStatus));
        }

        private void UpdateBrowserExtensionStatus()
        {
            var config = appConfig.GetConfig()?.General;
            bool webEnabled = config?.IsWebEnabled == true;
            IsBrowserExtensionIndicatorVisible = webEnabled;
            if (!webEnabled)
            {
                IsBrowserExtensionConnected = false;
                BrowserExtensionStatusText = "未连接";
                return;
            }

            if (!_webServer.IsRunning)
            {
                IsBrowserExtensionConnected = false;
                BrowserExtensionStatusText = "服务未启动";
                return;
            }

            IsBrowserExtensionConnected = _webServer.ConnectedExtensionCount > 0;
            int count = _webServer.ConnectedExtensionCount;
            if (count > 1)
            {
                BrowserExtensionStatusText = $"已连接 ({count})";
            }
            else if (count == 1)
            {
                BrowserExtensionStatusText = "已连接";
            }
            else
            {
                BrowserExtensionStatusText = "未连接";
            }
        }

        public void RefreshBrowserExtensionStatus()
        {
            UpdateBrowserExtensionStatus();
        }

        /// <summary>
        /// 页面可见时启动自动刷新（幂等，5 秒周期整表刷新）。
        /// </summary>
        public void StartAutoRefresh()
        {
            if (_autoRefreshSubscription == null)
            {
                _autoRefreshSubscription = _refreshServicer.Subscribe(OnAutoRefresh);
            }
        }

        /// <summary>
        /// 页面离开时停止自动刷新。
        /// </summary>
        public void StopAutoRefresh()
        {
            _autoRefreshSubscription?.Dispose();
            _autoRefreshSubscription = null;
        }

        private void OnAutoRefresh()
        {
            // 5 秒周期整表刷新：不带加载占位，避免每次刷新闪烁
            LoadData(showLoadingPlaceholder: false);
        }

        private void AnimationBudget_TierChanged(object sender, EventArgs e)
        {
            SyncLiveDurationUpdates();
        }

        private void SyncLiveDurationUpdates()
        {
            if (_liveDurationTimer == null)
            {
                return;
            }

            // 统计实时刷新始终运行：动画预算只控制动画丰富度，不控制统计数据刷新
            if (!_liveDurationTimer.IsEnabled)
            {
                _liveDurationTimer.Start();
            }         }

        private bool _liveUpdatesStarted;

        /// <summary>
        /// 页面可见时启动实时刷新（幂等）。
        /// </summary>
        public void StartLiveUpdates()
        {
            if (_liveUpdatesStarted)
            {
                return;
            }
            _liveUpdatesStarted = true;

            _appTimer.OnAppDurationUpdated += AppTimer_OnAppDurationUpdated;
            mainServicer.OnWebBrowseUpdated += MainServicer_OnWebBrowseUpdated;
            _liveDurationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _liveDurationTimer.Tick += LiveDurationTimer_Tick;
            _liveDurationTimer.Start();
        }

        /// <summary>
        /// 页面不可见时停止实时刷新，避免后台每秒空转（幂等）。
        /// </summary>
        public void StopLiveUpdates()
        {
            if (!_liveUpdatesStarted)
            {
                return;
            }
            _liveUpdatesStarted = false;

            if (_liveDurationTimer != null)
            {
                _liveDurationTimer.Tick -= LiveDurationTimer_Tick;
                _liveDurationTimer.Stop();
                _liveDurationTimer = null;
            }

            _appTimer.OnAppDurationUpdated -= AppTimer_OnAppDurationUpdated;
            mainServicer.OnWebBrowseUpdated -= MainServicer_OnWebBrowseUpdated;
        }

        private void LiveDurationTimer_Tick(object sender, EventArgs e)
        {
            // stats refresh must not be gated by animation budget (budget controls richness only)
            RefreshLiveAppDurations();

            //  新启动的应用即刻入列：活跃应用不在今日列表时插入临时行（掉入动画），
            //  并随秒级 tick 走字，不必等 5 秒整表刷新或会话结束
            var live = _appTimer.GetAppDuration();
            if (live != null && TabbarSelectedIndex == 0)
            {
                EnsureLiveRow(live, markNew: true);
            }

            UpdateTotalDuration();
        }

        /// <summary>
        /// 活跃应用不在今日列表时插入临时行（挂 IsNewAnimation 掉入动画）。
        /// 临时行的 AppModel 来自应用缓存，DB 落库后由 5 秒整表刷新自然转正；
        /// 刷新后若仍未落库则重新补插（幂等）。
        /// </summary>
        private void EnsureLiveRow(AppDurationUpdatedEventArgs live, bool markNew)
        {
            if (live?.App == null || string.IsNullOrEmpty(live.App.Process) || live.Duration <= 0)
            {
                return;
            }
            if (TabbarSelectedIndex != 0 || WeekData == null)
            {
                return;
            }

            bool exists = WeekData.Any(m =>
            {
                if (m?.Data is DailyLogModel log)
                {
                    //  正式行按 AppModel.Name 匹配；临时行的占位 AppModel.Name
                    //  就是进程名，同样走第一分支；异常情况下按行名兜底
                    if (log.AppModel != null)
                    {
                        return string.Equals(log.AppModel.Name, live.App.Process, StringComparison.OrdinalIgnoreCase);
                    }
                    return string.Equals(m.Name, live.App.Process, StringComparison.OrdinalIgnoreCase);
                }
                return false;
            });
            if (exists)
            {
                return;
            }

            //  GetApp 对未落库的新应用返回 null：用占位 AppModel（带进程名）
            //  让后续秒级 tick 与 5 秒整表刷新都能按名字匹配到这一行
            var appModel = _appData.GetApp(live.App.Process) ?? new Core.Models.AppModel
            {
                Name = live.App.Process
            };
            var logModel = new Core.Models.DailyLogModel
            {
                AppModel = appModel,
                Time = live.Duration
            };
            var row = new ChartsDataModel
            {
                Name = string.IsNullOrEmpty(appModel.Alias)
                    ? (string.IsNullOrEmpty(appModel.Description) ? appModel.Name : appModel.Description)
                    : appModel.Alias,
                Icon = appModel.IconFile,
                Value = live.Duration,
                Tag = Time.ToString(live.Duration),
                Data = logModel,
                IsNewAnimation = markNew
            };

            var newList = WeekData.ToList();
            newList.Add(row);
            WeekData = newList.OrderByDescending(m => m.Value).ToList();
        }

        private void AppTimer_OnAppDurationUpdated(object sender, Core.Event.AppDurationUpdatedEventArgs e)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (e?.App == null)
                {
                    return;   // 观察器给出空应用信息：无内容可结算
                }

                LiveDurationHelper.ApplyCompletedAppSession(WeekData, e);
                LiveDurationHelper.ApplyCompletedAppSession(AppMoreData, e);
                RefreshLiveAppDurations();

                //  会话里出现了列表之外的新应用（今天第一次使用）：立即整表刷新，
                //  让图标在一秒内出现，而不是等下一个 5 秒周期
                bool known = WeekData.Any(m =>
                {
                    if (m?.Data is DailyLogModel log && log.AppModel != null)
                    {
                        return string.Equals(log.AppModel.Name, e.App.Process, StringComparison.OrdinalIgnoreCase);
                    }
                    return false;
                });
                if (!known && TabbarSelectedIndex == 0)
                {
                    LoadData(showLoadingPlaceholder: false);
                    return;
                }

                UpdateTotalDuration();
            }));
        }

        // ============ 总时长卡（秒级实时） ============
        private string _totalDurationText = "00:00";
        public string TotalDurationText
        {
            get { return _totalDurationText; }
            set
            {
                if (value == _totalDurationText)
                {
                    return;
                }
                _totalDurationText = value;
                OnPropertyChanged();
            }
        }

        private string _totalDurationLabel = "今日总时长";
        public string TotalDurationLabel
        {
            get { return _totalDurationLabel; }
            set
            {
                if (value == _totalDurationLabel)
                {
                    return;
                }
                _totalDurationLabel = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 汇总当前 Tab 时段的总时长（含活跃会话未写库秒数），驱动总时长卡。
        /// 集合可能尚未加载（页面刚创建），必须判空。
        /// </summary>
        private void UpdateTotalDuration()
        {
            int total = 0;
            if (TabbarSelectedIndex == 0)
            {
                if (WeekData != null)
                {
                    total = (int)WeekData.Sum(m => m?.Value ?? 0);
                }
            }
            else
            {
                if (WeekData != null)
                {
                    total = (int)WeekData.Sum(m => m?.Value ?? 0);
                }
                if (WebFrequentUseData != null)
                {
                    total += (int)WebFrequentUseData.Sum(m => m?.Value ?? 0);
                }
            }

            //  活跃应用不在列表（可能是临时行被整表刷新吃掉）时直接累加活跃秒数；
            //  inList 匹配带 AppModel 兜底（临时行占位对象也按进程名匹配）
            var live = _appTimer.GetAppDuration();
            if (live?.App != null && !string.IsNullOrEmpty(live.App.Process))
            {
                bool inList = WeekData != null && WeekData.Any(m =>
                {
                    if (m?.Data is DailyLogModel log)
                    {
                        if (log.AppModel != null)
                        {
                            return string.Equals(log.AppModel.Name, live.App.Process, StringComparison.OrdinalIgnoreCase);
                        }
                        return string.Equals(m.Name, live.App.Process, StringComparison.OrdinalIgnoreCase);
                    }
                    return false;
                });
                if (!inList)
                {
                    total += live.Duration;
                }
            }

            TotalDurationText = Time.ToString(total);
        }
        private void MainServicer_OnWebBrowseUpdated(object sender, Core.Event.WebBrowseUpdatedEventArgs e)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                LiveDurationHelper.ApplyWebBrowseDuration(WebFrequentUseData, e.Domain, e.Duration);
                LiveDurationHelper.ApplyWebBrowseDuration(WebMoreData, e.Domain, e.Duration);
            }));
        }

        private void RefreshLiveAppDurations()
        {
            var live = _appTimer.GetAppDuration();
            LiveDurationHelper.ApplyActiveAppDuration(WeekData, live);
            LiveDurationHelper.ApplyActiveAppDuration(AppMoreData, live);
        }

        private void InputServicer_OnKeyUpInput(object sender, System.Windows.Forms.Keys key)
        {
            if (key == System.Windows.Forms.Keys.F5)
            {
                //  重新加载数据
                LoadData(showLoadingPlaceholder: true);
            }
        }

        private void OnTodetailCommand(object obj)
        {
            var data = obj as ChartsDataModel;

            if (data != null)
            {
                if (data.Data is DailyLogModel)
                {
                    var model = data.Data as DailyLogModel;
                    if (model != null && model.AppModel != null)
                    {
                        main.Data = model.AppModel;
                        main.Uri = nameof(DetailPage);
                    }
                }
                else if (data.Data is WebSiteModel)
                {
                    main.Data = data.Data;
                    main.Uri = nameof(WebSiteDetailPage);
                }

            }
        }

        private void IndexPageVM_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TabbarSelectedIndex))
            {
                TotalDurationLabel = TabbarSelectedIndex == 0 ? "今日总时长" : "本周总时长";
                LoadData(showLoadingPlaceholder: false);
            }
        }

        #region 读取数据

        private void LoadData(bool showLoadingPlaceholder = false)
        {            if (IsLoading)
            {
                return;
            }

            if (showLoadingPlaceholder && (WeekData == null || WeekData.Count == 0))
            {
                IsLoading = true;
            }

            FrequentUseNum = appConfig.GetConfig().General.IndexPageFrequentUseNum + 1;
            MoreNum = appConfig.GetConfig().General.IndexPageMoreNum + 1;

            //  同一次刷新内的多个加载共享同一版本，避免互相判定过期
            int version = _loadGate.BeginRefresh();
            if (TabbarSelectedIndex == 0)
            {
                LoadTodayData(version);
                LoadTodayMoreData(version);
            }
            else if (TabbarSelectedIndex == 1)
            {
                LoadThisWeekData(version);
                LoadThisWeekMoreData(version);
            }

        }

        #region 今日数据
        private void LoadTodayData(int version)
        {
            _loadGate.Run(version,
                () =>
                {
                    var list = data.GetDateRangelogList(DateTime.Now.Date, DateTime.Now.Date);
                    var topWebList = _webData.GetDateRangeWebSiteList(DateTime.Now, DateTime.Now, FrequentUseNum);
                    return new { List = list, WebList = topWebList };
                },
                r =>
                {
                    var newWeek = MapToChartsData(r.List);
                    MarkNewItems(WeekData, newWeek);
                    WeekData = newWeek;
                    WebFrequentUseData = MapToChartsData(r.WebList);
                    IsLoading = false;
                    //  实时补正：数据库不含当前活跃会话未写库的秒数，
                    //  整表刷新后立即补上，避免数字短暂跳回旧值
                    RefreshLiveAppDurations();
                    //  活跃应用若仍未落库（新启动的应用），重新补插临时行走字
                    EnsureLiveRow(_appTimer.GetAppDuration(), markNew: false);
                    UpdateTotalDuration();
                },
                () => IsLoading = false);
        }

        /// <summary>
        /// 标记本次刷新中新出现的条目（名称不在旧集合中）：仅它们播放入场动画，
        /// 既有条目在 5 秒整表重建时不再重复播动画
        /// </summary>
        private static void MarkNewItems(List<ChartsDataModel> oldList, List<ChartsDataModel> newList)
        {
            if (newList == null)
            {
                return;
            }
            if (oldList == null || oldList.Count == 0)
            {
                //  首次载入（或空转有数据）：全部走普通入场，不算"新出现"
                return;
            }

            var oldNames = new HashSet<string>(
                oldList.Select(m => m.Name),
                StringComparer.OrdinalIgnoreCase);
            foreach (var item in newList)
            {
                if (item != null && !oldNames.Contains(item.Name))
                {
                    item.IsNewAnimation = true;
                }
            }
        }

        private void LoadTodayMoreData(int version)
        {
            _loadGate.Run(version,
                () =>
                {
                    var appMoreData = data.GetDateRangelogList(DateTime.Now.Date, DateTime.Now.Date, MoreNum, FrequentUseNum);
                    var webMoreData = _webData.GetDateRangeWebSiteList(DateTime.Now.Date, DateTime.Now.Date, MoreNum, FrequentUseNum);
                    return new { AppMore = appMoreData, WebMore = webMoreData };
                },
                r =>
                {
                    AppMoreData = MapToChartsData(r.AppMore);
                    WebMoreData = MapToChartsData(r.WebMore);
                    IsLoading = false;
                    //  实时补正：数据库不含当前活跃会话未写库的秒数，
                    //  整表刷新后立即补上，避免数字短暂跳回旧值
                    RefreshLiveAppDurations();
                },
                () => IsLoading = false);
        }
        #endregion

        #region 本周数据
        private void LoadThisWeekData(int version)
        {
            _loadGate.Run(version,
                () =>
                {
                    var list = data.GetThisWeeklogList();
                    var week = Time.GetThisWeekDate();
                    var topWebList = _webData.GetDateRangeWebSiteList(week[0], week[1], FrequentUseNum);
                    return new { List = list, WebList = topWebList };
                },
                r =>
                {
                    WeekData = MapToChartsData(r.List);
                    WebFrequentUseData = MapToChartsData(r.WebList);
                    IsLoading = false;
                    //  实时补正：数据库不含当前活跃会话未写库的秒数，
                    //  整表刷新后立即补上，避免数字短暂跳回旧值
                    RefreshLiveAppDurations();
                    UpdateTotalDuration();
                },
                () => IsLoading = false);
        }
        private void LoadThisWeekMoreData(int version)
        {
            _loadGate.Run(version,
                () =>
                {
                    var week = Time.GetThisWeekDate();
                    var appMoreData = data.GetDateRangelogList(week[0], week[1], MoreNum, FrequentUseNum);
                    var webMoreData = _webData.GetDateRangeWebSiteList(week[0], week[1], MoreNum, FrequentUseNum);
                    return new { AppMore = appMoreData, WebMore = webMoreData };
                },
                r =>
                {
                    AppMoreData = MapToChartsData(r.AppMore);
                    WebMoreData = MapToChartsData(r.WebMore);
                    IsLoading = false;
                    //  实时补正：数据库不含当前活跃会话未写库的秒数，
                    //  整表刷新后立即补上，避免数字短暂跳回旧值
                    RefreshLiveAppDurations();
                },
                () => IsLoading = false);
        }
        #endregion


        #region 处理数据
        private List<ChartsDataModel> MapToChartsData(IEnumerable<Core.Models.DailyLogModel> list)
        {
            var resData = new List<ChartsDataModel>();

            foreach (var item in list)
            {
                var bindModel = new ChartsDataModel();
                bindModel.Data = item;
                var appModel = item.AppModel;
                if (appModel != null)
                {
                    bindModel.Name = !string.IsNullOrEmpty(appModel.Alias) ? appModel.Alias : !string.IsNullOrEmpty(appModel.Description) ? appModel.Description : appModel.Name;
                    bindModel.PopupText = appModel.File;
                    bindModel.Icon = appModel.IconFile;
                }
                else
                {
                    bindModel.Name = "未知应用";
                    bindModel.PopupText = string.Empty;
                }
                bindModel.Value = item.Time;
                bindModel.Tag = Time.ToString(item.Time);
                bindModel.DateTime = item.Date;
                resData.Add(bindModel);
            }

            return resData;
        }
        private List<ChartsDataModel> MapToChartsData(IEnumerable<Core.Models.Db.WebSiteModel> list)
        {
            var resData = new List<ChartsDataModel>();

            foreach (var item in list)
            {
                var bindModel = new ChartsDataModel();
                bindModel.Data = item;
                bindModel.Name = !string.IsNullOrEmpty(item.Alias) ? item.Alias : item.Title;
                bindModel.Value = item.Duration;
                bindModel.Tag = Time.ToString(item.Duration);
                bindModel.PopupText = item.Domain;
                bindModel.Icon = item.IconFile;
                resData.Add(bindModel);
            }

            return resData;
        }
        #endregion
        #endregion
    }
}
