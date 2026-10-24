using Core.Librarys;
using Core.Librarys.Image;
using Core.Models;
using Core.Servicers.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using UI.Controls;
using UI.Controls.Charts.Model;
using UI.Models;
using UI.Servicers;
using UI.Views;
using static UI.Controls.SettingPanel.SettingPanel;

namespace UI.ViewModels
{
    public class DataPageVM : DataPageModel
    {
        public Command ToDetailCommand { get; set; }
        private readonly IData data;
        private readonly MainViewModel main;
        private readonly IAppContextMenuServicer appContextMenuServicer;
        private readonly IAppConfig appConfig;
        private readonly IWebData _webData;
        private readonly IWebSiteContextMenuServicer _webSiteContextMenu;
        private readonly UsageDataRefreshServicer _refreshServicer;
        private IDisposable _autoRefreshSubscription;

        public DataPageVM(IData data, MainViewModel main, IAppContextMenuServicer appContextMenuServicer, IAppConfig appConfig, IWebData webData, IWebSiteContextMenuServicer webSiteContextMenu, UsageDataRefreshServicer refreshServicer)
        {
            this.data = data;
            this.main = main;
            this.appContextMenuServicer = appContextMenuServicer;
            this.appConfig = appConfig;
            _webData = webData;
            _webSiteContextMenu = webSiteContextMenu;
            _refreshServicer = refreshServicer;

            ToDetailCommand = new Command(new Action<object>(OnTodetailCommand));

            Init();
        }

        public override void Dispose()
        {
            StopAutoRefresh();
            PropertyChanged -= DataPageVM_PropertyChanged;

            base.Dispose();
        }

        /// <summary>
        /// 页面可见时启动自动刷新（幂等，5 秒周期）。
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

        /// <summary>
        /// 按当前标签刷新对应日期范围（自动刷新入口）。
        /// </summary>
        public void RefreshCurrent()
        {
            if (TabbarSelectedIndex == 0)
            {
                _ = LoadData(DayDate == DateTime.MinValue ? DateTime.Now.Date : DayDate);
            }
            else if (TabbarSelectedIndex == 1)
            {
                _ = LoadData(MonthDate == DateTime.MinValue ? new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1) : MonthDate);
            }
            else
            {
                _ = LoadData(YearDate == DateTime.MinValue ? new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1) : YearDate);
            }
        }

        private void OnAutoRefresh()
        {
            RefreshCurrent();
        }
        private void Init()
        {
            //LoadData(DateTime.Now.Date);
            PropertyChanged += DataPageVM_PropertyChanged;

            TabbarData = new System.Collections.ObjectModel.ObservableCollection<string>()
            {
                "按天","按月","按年"
            };

            TabbarSelectedIndex = 0;

            AppContextMenu = appContextMenuServicer.GetContextMenu();
        }

        private void OnTodetailCommand(object obj)
        {
            var data = obj as ChartsDataModel;

            if (data != null)
            {
                var model = data.Data as DailyLogModel;
                if (model != null && model.AppModel != null)
                {
                    main.Data = model.AppModel;
                    main.Uri = nameof(DetailPage);
                }
                else
                {
                    main.Data = data.Data as Core.Models.Db.WebSiteModel;
                    main.Uri = nameof(WebSiteDetailPage);
                }
            }
        }

        private void DataPageVM_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {

            if (e.PropertyName == nameof(DayDate))
            {
                _ = LoadData(DayDate);
            }
            else if (e.PropertyName == nameof(MonthDate))
            {
                _ = LoadData(MonthDate);
            }
            else if (e.PropertyName == nameof(YearDate))
            {
                _ = LoadData(YearDate);
            }
            else if (e.PropertyName == nameof(TabbarSelectedIndex))
            {
                if (TabbarSelectedIndex == 0)
                {
                    if (DayDate == DateTime.MinValue)
                    {
                        DayDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day);
                    }

                }
                else if (TabbarSelectedIndex == 1)
                {
                    if (MonthDate == DateTime.MinValue)
                    {
                        MonthDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
                    }
                }
                else if (TabbarSelectedIndex == 2)
                {
                    if (YearDate == DateTime.MinValue)
                    {
                        YearDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
                    }
                }
            }
            else if (e.PropertyName == nameof(ShowType))
            {
                //  仅加载当前可见标签对应的日期范围（原来无条件三路全量查询，其中两路无用）
                if (TabbarSelectedIndex == 0)
                {
                    _ = LoadData(DayDate, 0);
                }
                else if (TabbarSelectedIndex == 1)
                {
                    _ = LoadData(MonthDate, 1);
                }
                else
                {
                    _ = LoadData(YearDate, 2);
                }

                if (ShowType.Id == 0)
                {
                    AppContextMenu = appContextMenuServicer.GetContextMenu();
                }
                else
                {
                    AppContextMenu = _webSiteContextMenu.GetContextMenu();
                }
            }
        }



        #region 读取数据




        private async Task LoadData(DateTime date, int dataType_ = -1)
        {
            try
            {
                //  先捕获当前标签类型，避免 await 期间 TabbarSelectedIndex 变化导致数据错位
                int dataType = dataType_ == -1 ? TabbarSelectedIndex : dataType_;

                DateTime dateStart = date, dateEnd = date;
                if (dataType == 1)
                {
                    dateStart = new DateTime(date.Year, date.Month, 1);
                    dateEnd = new DateTime(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));
                }
                else if (dataType == 0)
                {
                    dateStart = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0);
                    dateEnd = new DateTime(date.Year, date.Month, date.Day, 23, 59, 59);
                }
                else if (dataType == 2)
                {
                    dateStart = new DateTime(date.Year, 1, 1, 0, 0, 0);
                    dateEnd = new DateTime(date.Year, 12, DateTime.DaysInMonth(date.Year, 12), 23, 59, 59);
                }

                //  查询在后台线程，绑定属性赋值回到 UI 线程（await 恢复同步上下文）
                List<ChartsDataModel> chartData = await Task.Run(() =>
                {
                    if (ShowType.Id == 0)
                    {
                        return MapToChartsData(data.GetDateRangelogList(dateStart, dateEnd));
                    }

                    return MapToChartsWebData(_webData.GetWebSiteLogList(dateStart, dateEnd));
                });

                if (dataType == 0)
                {
                    Data = chartData;
                }
                else if (dataType == 1)
                {
                    MonthData = chartData;
                }
                else
                {
                    YearData = chartData;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("详细页加载数据失败：" + ex);
            }
        }

        #region 处理数据
        private List<ChartsDataModel> MapToChartsData(IEnumerable<Core.Models.DailyLogModel> list)
        {
            var resData = new List<ChartsDataModel>();
            try
            {
                var config = appConfig.GetConfig();

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
                    bindModel.BadgeList = new List<ChartBadgeModel>();
                    if (appModel?.Category != null)
                    {
                        bindModel.BadgeList.Add(new ChartBadgeModel()
                        {
                            Name = appModel.Category.Name,
                            Color = appModel.Category.Color,
                            Type = ChartBadgeType.Category
                        });
                    }
                    if (appModel != null && config.Behavior.IgnoreProcessList.Contains(appModel.Name))
                    {
                        bindModel.BadgeList.Add(ChartBadgeModel.IgnoreBadge);
                    }
                    resData.Add(bindModel);
                }
            }
            catch (Exception e)
            {
                Logger.Error(e.ToString());
            }

            return resData;
        }

        private List<ChartsDataModel> MapToChartsWebData(IEnumerable<Core.Models.Db.WebSiteModel> list)
        {
            var resData = new List<ChartsDataModel>();
            try
            {
                var config = appConfig.GetConfig();

                foreach (var item in list)
                {
                    var bindModel = new ChartsDataModel();
                    bindModel.Data = item;
                    bindModel.Name = !string.IsNullOrEmpty(item.Alias) ? item.Alias : item.Title;
                    bindModel.Value = item.Duration;
                    bindModel.Tag = Time.ToString(item.Duration);
                    bindModel.PopupText = item.Domain;
                    bindModel.Icon = item.IconFile;
                    bindModel.BadgeList = new List<ChartBadgeModel>();
                    if (item.Category != null)
                    {
                        bindModel.BadgeList.Add(new ChartBadgeModel()
                        {
                            Name = item.Category.Name,
                            Color = item.Category.Color,
                            Type = ChartBadgeType.Category
                        });
                    }
                    if (config.Behavior.IgnoreURLList.Contains(item.Domain))
                    {
                        bindModel.BadgeList.Add(ChartBadgeModel.IgnoreBadge);
                    }
                    resData.Add(bindModel);
                }
            }
            catch (Exception e)
            {
                Logger.Error(e.ToString());
            }
            return resData;
        }
        #endregion
        #endregion
    }
}
