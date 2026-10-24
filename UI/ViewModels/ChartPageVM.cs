using Core.Librarys;
using Core.Models;
using Core.Models.Db;
using Core.Servicers.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Documents;
using UI.Controls;
using UI.Controls.Charts.Model;
using UI.Controls.Select;
using UI.Models;
using UI.Servicers;
using UI.Views;

namespace UI.ViewModels
{
    public class ChartPageVM : ChartPageModel
    {
        private readonly ICategorys categorys;
        private readonly IData data;
        private readonly MainViewModel mainVM;
        private readonly IAppContextMenuServicer appContextMenuServicer;
        private readonly IInputServicer inputServicer;
        private readonly IWebData _webData;
        private readonly IWebSiteContextMenuServicer _webSiteContextMenu;
        private readonly UsageDataRefreshServicer _refreshServicer;
        private IDisposable _autoRefreshSubscription;

        private double totalTime_ = 0;
        private int appCount_ = 0;
        //  异步加载版本闸门：按数据域拆分，快速切换时不互相串扰
        private readonly UI.Base.LoadGate _chartGate = new UI.Base.LoadGate();
        private readonly UI.Base.LoadGate _topGate = new UI.Base.LoadGate();
        private readonly UI.Base.LoadGate _webGate = new UI.Base.LoadGate();
        private readonly UI.Base.LoadGate _columnGate = new UI.Base.LoadGate();
        public Command ToDetailCommand { get; set; }
        public Command RefreshCommand { get; set; }
        public List<SelectItemModel> ChartDataModeOptions { get; set; }

        public ChartPageVM(IData data, ICategorys categorys, MainViewModel mainVM, IAppContextMenuServicer appContextMenuServicer, IInputServicer inputServicer, IWebData webData_, IWebSiteContextMenuServicer webSiteContextMenu_, UsageDataRefreshServicer refreshServicer_)
        {
            this.data = data;
            this.categorys = categorys;
            this.mainVM = mainVM;
            this.appContextMenuServicer = appContextMenuServicer;
            this.inputServicer = inputServicer;
            _webData = webData_;
            _webSiteContextMenu = webSiteContextMenu_;
            _refreshServicer = refreshServicer_;

            ToDetailCommand = new Command(new Action<object>(OnTodetailCommand));
            RefreshCommand = new Command(new Action<object>(OnRefreshCommand));

            TabbarData = new System.Collections.ObjectModel.ObservableCollection<string>()
            {
                "按天","按周","按月","按年"
            };

            var weekOptions = new List<SelectItemModel>();
            weekOptions.Add(new SelectItemModel()
            {
                Name = "本周"
            });
            weekOptions.Add(new SelectItemModel()
            {
                Name = "上周"
            });

            var chartDataModeOptions = new List<SelectItemModel>
            {
                new SelectItemModel()
                {
                    Id = 1,
                    Name = "默认视图",
                },
                new SelectItemModel()
                {
                    Id = 2,
                    Name = "汇总视图"
                },
                new SelectItemModel()
                {
                    Id = 3,
                    Name = "分类视图"
                }
            };

            ChartDataModeOptions = chartDataModeOptions;
            ChartDataMode = chartDataModeOptions[0];
            //ShowType = ShowTypeOptions[0];
            WeekOptions = weekOptions;
            SelectedWeek = weekOptions[0];
            MonthDate = DateTime.Now;
            TabbarSelectedIndex = 0;
            Date = DateTime.Now;
            YearDate = DateTime.Now;

            LoadDayData();

            PropertyChanged += ChartPageVM_PropertyChanged;
            inputServicer.OnKeyUpInput += InputServicer_OnKeyUpInput;
            AppContextMenu = appContextMenuServicer.GetContextMenu();
            WebSiteContextMenu = _webSiteContextMenu.GetContextMenu();
        }


        public override void Dispose()
        {
            base.Dispose();
            StopAutoRefresh();
            inputServicer.OnKeyUpInput -= InputServicer_OnKeyUpInput;
            PropertyChanged -= ChartPageVM_PropertyChanged;
            Data = null;
            TopData = null;
        }
        private void OnTodetailCommand(object obj)
        {
            var data = obj as ChartsDataModel;

            if (data != null)
            {
                if (data.Data is WebSiteModel)
                {
                    mainVM.Data = data.Data;
                    mainVM.Uri = nameof(WebSiteDetailPage);
                }
                else
                {
                    var model = data.Data as DailyLogModel;
                    var app = model != null ? model.AppModel : null;

                    if (model == null)
                    {
                        app = (data.Data as HoursLogModel).AppModel;
                    }

                    if (app != null)
                    {
                        mainVM.Data = app;
                        mainVM.Uri = nameof(DetailPage);
                    }
                }
            }
        }
        private void OnRefreshCommand(object obj)
        {
            LoadData();
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

        private void OnAutoRefresh()
        {
            LoadData();
        }

        private void InputServicer_OnKeyUpInput(object sender, System.Windows.Forms.Keys key)
        {
            if (key == System.Windows.Forms.Keys.F5)
            {
                LoadData();
            }
        }


        private void ChartPageVM_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Date))
            {
                LoadDayData();
            }
            if (e.PropertyName == nameof(TabbarSelectedIndex))
            {
                IsCanColumnSelect = true;
                LoadData();
            }
            if (e.PropertyName == nameof(SelectedWeek))
            {
                LoadWeekData();
            }
            if (e.PropertyName == nameof(MonthDate))
            {
                LoadMonthlyData();
            }
            if (e.PropertyName == nameof(YearDate))
            {
                LoadYearData();
            }
            if (e.PropertyName == nameof(ColumnSelectedIndex))
            {
                LoadSelectedColData();
            }
            if (e.PropertyName == nameof(ChartDataMode))
            {
                if (ChartDataMode.Id == 1)
                {
                    IsChartStack = true;
                }
                else
                {
                    IsChartStack = false;
                }
                LoadData();
            }
            if (e.PropertyName == nameof(WebColSelectedIndex))
            {
                LoadWebSitesColSelectedData();
            }
        }


        private void LoadData()
        {
            if (TabbarSelectedIndex == 0)
            {
                NameIndexStart = 0;

                LoadDayData();
            }
            else if (TabbarSelectedIndex == 1)
            {
                LoadWeekData();
            }
            else if (TabbarSelectedIndex == 2)
            {
                NameIndexStart = 1;

                LoadMonthlyData();
            }
            else if (TabbarSelectedIndex == 3)
            {
                LoadYearData();
            }

            LoadSelectedColData();
        }

        /// <summary>
        /// 加载天数据
        /// </summary>
        private void LoadDayData()
        {
            ColumnSelectedIndex = -1;
            WebColSelectedIndex = -1;

            DataMaximum = 3600;
            int version = _chartGate.BeginRefresh();
            _chartGate.Run(version,
                () =>
                {
                    //  应用数据（后台只查询与构建，不在后台触碰绑定属性）
                    var chartData = new List<ChartsDataModel>();
                    var nullCategory = new CategoryModel()
                    {
                        ID = 0,
                        Name = "未分类",
                        IconFile = "pack://application:,,,/guage;component/Resources/Icons/guage32.ico"
                    };
                    foreach (var item in data.GetCategoryHoursData(Date))
                    {
                        var category = categorys.GetCategory(item.CategoryID);
                        if (item.CategoryID == 0)
                        {
                            category = nullCategory;
                        }
                        if (category != null)
                        {
                            var dataItem = new ChartsDataModel()
                            {
                                Name = category.Name,
                                Icon = category.IconFile,
                                Values = item.Values,
                                Color = category.Color
                            };
                            if (category.ID == 0)
                            {
                                dataItem.Color = "#E5F7F6F2";
                            }
                            chartData.Add(dataItem);
                        }
                    }

                    //  汇总视图需要按天总量
                    var values = ChartDataMode.Id == 1 || ChartDataMode.Id == 3
                        ? null
                        : data.GetRangeTotalData(Date, Date);

                    return new { ChartData = chartData, TotalValues = values };
                },
                r =>
                {
                    List<ChartsDataModel> sumData = null;
                    if (r.TotalValues != null)
                    {
                        sumData = new List<ChartsDataModel>
                        {
                            new ChartsDataModel { Values = r.TotalValues }
                        };
                    }

                    Data = sumData ?? r.ChartData;
                    RadarData = r.ChartData;

                    double totalUse = Data.Sum(m => m.Values.Sum());
                    totalTime_ = totalUse;
                    TotalHours = Time.ToHoursString(totalUse);

                    LoadTopData();
                    //  网页数据
                    LoadWebData(Date, Date);
                });
        }

        /// <summary>
        /// 加载周数据
        /// </summary>
        private void LoadWeekData()
        {
            ColumnSelectedIndex = -1;
            WebColSelectedIndex = -1;

            DataMaximum = 0;
            int version = _chartGate.BeginRefresh();
            _chartGate.Run(version,
                () =>
                {
                    var weekDateArr = SelectedWeek.Name == "本周" ? Time.GetThisWeekDate() : Time.GetLastWeekDate();
                    string[] weekNames = { "周一", "周二", "周三", "周四", "周五", "周六", "周日", };
                    var chartData = new List<ChartsDataModel>();
                    var nullCategory = new CategoryModel()
                    {
                        ID = 0,
                        Name = "未分类",
                        IconFile = "pack://application:,,,/guage;component/Resources/Icons/guage32.ico"
                    };

                    foreach (var item in data.GetCategoryRangeData(weekDateArr[0], weekDateArr[1]))
                    {
                        var category = categorys.GetCategory(item.CategoryID);
                        if (item.CategoryID == 0)
                        {
                            category = nullCategory;
                        }
                        if (category != null)
                        {
                            var dataItem = new ChartsDataModel()
                            {
                                Name = category.Name,
                                Icon = category.IconFile,
                                Values = item.Values,
                                ColumnNames = weekNames,
                                Color = category.Color,
                            };
                            if (category.ID == 0)
                            {
                                dataItem.Color = "#E5F7F6F2";
                            }
                            chartData.Add(dataItem);
                        }
                    }

                    var totalValues = ChartDataMode.Id == 1 || ChartDataMode.Id == 3
                        ? null
                        : data.GetRangeTotalData(weekDateArr[0], weekDateArr[1]);

                    return new
                    {
                        WeekDateArr = weekDateArr,
                        WeekNames = weekNames,
                        ChartData = chartData,
                        TotalValues = totalValues
                    };
                },
                r =>
                {
                    WeekDateStr = r.WeekDateArr[0].ToString("yyyy年MM月dd日") + " 到 " + r.WeekDateArr[1].ToString("yyyy年MM月dd日");

                    List<ChartsDataModel> sumData = null;
                    if (r.TotalValues != null)
                    {
                        sumData = new List<ChartsDataModel>
                        {
                            new ChartsDataModel { Values = r.TotalValues, ColumnNames = r.WeekNames }
                        };
                    }

                    Data = sumData ?? r.ChartData;
                    RadarData = r.ChartData;

                    double totalUse = Data.Sum(m => m.Values.Sum());
                    totalTime_ = totalUse;
                    TotalHours = Time.ToHoursString(totalUse);

                    //  网页数据
                    LoadWebData(r.WeekDateArr[0], r.WeekDateArr[1]);

                    //  在 totalTime_ 赋值之后加载环比，避免读到旧值
                    LoadTopData();
                });
        }

        /// <summary>
        /// 加载月数据
        /// </summary>
        private void LoadMonthlyData()
        {
            ColumnSelectedIndex = -1;
            WebColSelectedIndex = -1;
            DataMaximum = 0;
            int version = _chartGate.BeginRefresh();
            _chartGate.Run(version,
                () =>
                {
                    var chartData = new List<ChartsDataModel>();
                    var dateArr = Time.GetMonthDate(MonthDate);
                    var nullCategory = new CategoryModel()
                    {
                        ID = 0,
                        Name = "未分类",
                        IconFile = "pack://application:,,,/guage;component/Resources/Icons/guage32.ico"
                    };

                    foreach (var item in data.GetCategoryRangeData(dateArr[0], dateArr[1]))
                    {
                        var category = categorys.GetCategory(item.CategoryID);
                        if (item.CategoryID == 0)
                        {
                            category = nullCategory;
                        }
                        if (category != null)
                        {
                            var dataItem = new ChartsDataModel()
                            {
                                Name = category.Name,
                                Icon = category.IconFile,
                                Values = item.Values,
                                Color = category.Color
                            };
                            if (category.ID == 0)
                            {
                                dataItem.Color = "#E5F7F6F2";
                            }
                            chartData.Add(dataItem);
                        }
                    }

                    var totalValues = ChartDataMode.Id == 1 || ChartDataMode.Id == 3
                        ? null
                        : data.GetRangeTotalData(dateArr[0], dateArr[1]);

                    return new { DateArr = dateArr, ChartData = chartData, TotalValues = totalValues };
                },
                r =>
                {
                    List<ChartsDataModel> sumData = null;
                    if (r.TotalValues != null)
                    {
                        sumData = new List<ChartsDataModel>
                        {
                            new ChartsDataModel { Values = r.TotalValues }
                        };
                    }

                    Data = sumData ?? r.ChartData;
                    RadarData = r.ChartData;

                    double totalUse = Data.Sum(m => m.Values.Sum());
                    totalTime_ = totalUse;
                    TotalHours = Time.ToHoursString(totalUse);

                    LoadTopData();
                    //  网页数据
                    LoadWebData(r.DateArr[0], r.DateArr[1]);
                });
        }

        /// <summary>
        /// 加载年份数据
        /// </summary>
        private void LoadYearData()
        {
            ColumnSelectedIndex = -1;
            WebColSelectedIndex = -1;

            DataMaximum = 0;
            int version = _chartGate.BeginRefresh();
            _chartGate.Run(version,
                () =>
                {
                    var chartData = new List<ChartsDataModel>();
                    string[] names = new string[12];
                    for (int i = 0; i < 12; i++)
                    {
                        names[i] = (i + 1) + "月";
                    }

                    var nullCategory = new CategoryModel()
                    {
                        ID = 0,
                        Name = "未分类",
                        IconFile = "pack://application:,,,/guage;component/Resources/Icons/guage32.ico"
                    };

                    foreach (var item in data.GetCategoryYearData(YearDate))
                    {
                        var category = categorys.GetCategory(item.CategoryID);
                        if (item.CategoryID == 0)
                        {
                            category = nullCategory;
                        }
                        if (category != null)
                        {
                            var dataItem = new ChartsDataModel()
                            {
                                Name = category.Name,
                                Icon = category.IconFile,
                                Values = item.Values,
                                ColumnNames = names,
                                Color = category.Color
                            };
                            if (category.ID == 0)
                            {
                                dataItem.Color = "#E5F7F6F2";
                            }
                            chartData.Add(dataItem);
                        }
                    }

                    var totalValues = ChartDataMode.Id == 1 || ChartDataMode.Id == 3
                        ? null
                        : data.GetMonthTotalData(YearDate);
                    var dateArr = Time.GetYearDate(YearDate);

                    return new { Names = names, ChartData = chartData, TotalValues = totalValues, DateArr = dateArr };
                },
                r =>
                {
                    List<ChartsDataModel> sumData = null;
                    if (r.TotalValues != null)
                    {
                        sumData = new List<ChartsDataModel>
                        {
                            new ChartsDataModel { Values = r.TotalValues, ColumnNames = r.Names }
                        };
                    }

                    Data = sumData ?? r.ChartData;
                    RadarData = r.ChartData;

                    double totalUse = Data.Sum(m => m.Values.Sum());
                    totalTime_ = totalUse;
                    TotalHours = Time.ToHoursString(totalUse);

                    LoadTopData();
                    //  网页数据
                    LoadWebData(r.DateArr[0], r.DateArr[1]);
                });
        }

        private void LoadTopData()
        {
            int version = _topGate.BeginRefresh();
            _topGate.Run(version,
                () =>
                {
                    var dateStart = Date.Date;
                    var dateEnd = Date.Date;
                    if (TabbarSelectedIndex == 1)
                    {
                        //  周
                        var weekDateArr = SelectedWeek.Name == "本周" ? Time.GetThisWeekDate() : Time.GetLastWeekDate();
                        dateStart = weekDateArr[0];
                        dateEnd = weekDateArr[1];
                    }
                    else if (TabbarSelectedIndex == 2)
                    {
                        //  月
                        var dateArr = Time.GetMonthDate(MonthDate);
                        dateStart = dateArr[0];
                        dateEnd = dateArr[1];
                    }
                    else if (TabbarSelectedIndex == 3)
                    {
                        //  年
                        var dateArr = Time.GetYearDate(YearDate);
                        dateStart = dateArr[0];
                        dateEnd = dateArr[1];
                    }

                    var list = data.GetDateRangelogList(dateStart, dateEnd, 5);
                    var appCount = data.GetDateRangeAppCount(dateStart, dateEnd);
                    return new { DateStart = dateStart, DateEnd = dateEnd, List = list, AppCount = appCount };
                },
                r =>
                {
                    TopData = MapToChartsData(r.List);
                    TopHours = TopData.Count > 0 ? Time.ToHoursString(TopData[0].Value) : "0";

                    appCount_ = r.AppCount;
                    AppCount = appCount_.ToString();

                    Top1App = null;
                    if (TopData.Count > 0)
                    {
                        var model = TopData[0].Data as DailyLogModel;
                        Top1App = model != null ? model.AppModel : null;
                    }

                    LoadDiffData(version, r.DateStart, r.DateEnd);
                });
        }

        private void LoadDiffData(int version, DateTime? startOverride = null, DateTime? endOverride = null)
        {
            _topGate.Run(version,
                () =>
                {
                    DateTime dateStart, dateEnd;
                    if (startOverride.HasValue)
                    {
                        dateStart = startOverride.Value;
                        dateEnd = endOverride.Value;
                    }
                    else
                    {
                        dateStart = Date.Date.AddDays(-1);
                        dateEnd = Date.Date.AddDays(-1);
                    }

                    if (TabbarSelectedIndex == 1)
                    {
                        //  周
                        var weekDateArr = Time.GetLastWeekDate();
                        dateStart = weekDateArr[0];
                        dateEnd = weekDateArr[1];
                    }
                    else if (TabbarSelectedIndex == 2)
                    {
                        //  月
                        var dateArr = Time.GetMonthDate(MonthDate.AddMonths(-1));
                        dateStart = dateArr[0];
                        dateEnd = dateArr[1];
                    }
                    else if (TabbarSelectedIndex == 3)
                    {
                        //  年
                        var dateArr = Time.GetYearDate(YearDate.AddYears(-1));
                        dateStart = dateArr[0];
                        dateEnd = dateArr[1];
                    }

                    //  应用量
                    int lastAppCount = data.GetDateRangeAppCount(dateStart, dateEnd);
                    //  使用总时长
                    var lastTotalTime = data.GetRangeTotalData(dateStart, dateEnd).Sum();
                    var lastWebTotalTime = _webData.GetBrowseDurationTotal(dateStart, dateEnd);
                    var lastWebSiteCount = _webData.GetBrowseSitesTotal(dateStart, dateEnd);
                    var lastWebPageCount = _webData.GetBrowsePagesTotal(dateStart, dateEnd);

                    return new
                    {
                        LastAppCount = lastAppCount,
                        LastTotalTime = lastTotalTime,
                        LastWebTotalTime = lastWebTotalTime,
                        LastWebSiteCount = lastWebSiteCount,
                        LastWebPageCount = lastWebPageCount
                    };
                },
                r =>
                {
                    double diffTotalTime;
                    if (r.LastTotalTime <= 0)
                    {
                        //  上期无数据：本期有使用则视为 +100%
                        diffTotalTime = totalTime_ > 0 ? 100 : 0;
                    }
                    else
                    {
                        diffTotalTime = ((totalTime_ - r.LastTotalTime) / r.LastTotalTime) * 100;
                    }

                    if (diffTotalTime > 0)
                    {
                        DiffTotalTimeType = "1";
                    }
                    else if (diffTotalTime < 0)
                    {
                        DiffTotalTimeType = "-1";
                    }
                    else
                    {
                        DiffTotalTimeType = "0";
                    }
                    DiffTotalTimeValue = DiffTotalTimeType == "0" ? string.Empty : diffTotalTime == 100 ? "100%" : Math.Abs(diffTotalTime).ToString("f2") + "%";

                    int diffAppCount = appCount_ - r.LastAppCount;
                    if (diffAppCount > 0)
                    {
                        DiffAppCountType = "1";
                    }
                    else if (diffAppCount < 0)
                    {
                        DiffAppCountType = "-1";
                    }
                    else
                    {
                        DiffAppCountType = "0";
                    }
                    DiffAppCountValue = DiffAppCountType == "0" ? string.Empty : Math.Abs(diffAppCount).ToString();

                    LastWebTotalTime = r.LastWebTotalTime;
                    LastWebSiteCount = r.LastWebSiteCount;
                    LastWebPageCount = r.LastWebPageCount;
                });
        }



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
                resData.Add(bindModel);
            }

            return resData;
        }

        private void LoadSelectedColData()
        {
            if (ColumnSelectedIndex < 0)
            {
                DayHoursSelectedTime = String.Empty;
                return;
            }

            int version = _columnGate.BeginRefresh();
            _columnGate.Run(version,
                () =>
                {
                    IEnumerable<HoursLogModel> hoursModelList = new List<HoursLogModel>();
                    IEnumerable<DailyLogModel> daysModelList = new List<DailyLogModel>();
                    string selectedTimeText = string.Empty;

                    if (TabbarSelectedIndex == 0)
                    {
                        //  天
                        var time = new DateTime(Date.Year, Date.Month, Date.Day, ColumnSelectedIndex, 0, 0);
                        selectedTimeText = time.ToString("yyyy年MM月dd日 HH点");
                        hoursModelList = data.GetTimeRangelogList(time);
                    }
                    else if (TabbarSelectedIndex == 1)
                    {
                        //  周
                        var weekDateArr = SelectedWeek.Name == "本周" ? Time.GetThisWeekDate() : Time.GetLastWeekDate();
                        var time = weekDateArr[0].AddDays(ColumnSelectedIndex);
                        selectedTimeText = time.ToString("yyyy年MM月dd日");
                        daysModelList = data.GetDateRangelogList(time, time);
                    }
                    else if (TabbarSelectedIndex == 2)
                    {
                        //  月
                        var dateArr = Time.GetMonthDate(MonthDate);
                        var time = dateArr[0].AddDays(ColumnSelectedIndex);
                        selectedTimeText = time.ToString("yyyy年MM月dd日");
                        daysModelList = data.GetDateRangelogList(time, time);
                    }
                    else if (TabbarSelectedIndex == 3)
                    {
                        //  年
                        var dateStart = new DateTime(YearDate.Year, ColumnSelectedIndex + 1, 1);
                        var dateEnd = new DateTime(dateStart.Year, dateStart.Month, DateTime.DaysInMonth(dateStart.Year, dateStart.Month), 23, 59, 59);
                        selectedTimeText = dateStart.ToString("yyyy年MM月");
                        daysModelList = data.GetDateRangelogList(dateStart, dateEnd);
                    }

                    var chartsDatas = new List<ChartsDataModel>();
                    IEnumerable<dynamic> sourceList = TabbarSelectedIndex == 0
                        ? hoursModelList.Cast<dynamic>()
                        : daysModelList.Cast<dynamic>();
                    foreach (var item in sourceList)
                    {
                        var bindModel = new ChartsDataModel();
                        bindModel.Data = item;
                        var appModel = (Core.Models.AppModel)item.AppModel;
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
                        chartsDatas.Add(bindModel);
                    }

                    return new { SelectedTimeText = selectedTimeText, ChartsDatas = chartsDatas };
                },
                r =>
                {
                    DayHoursSelectedTime = r.SelectedTimeText;
                    DayHoursData = r.ChartsDatas;
                });
        }


        #region 网页数据
        private void LoadWebData(DateTime start_, DateTime end_)
        {
            //  同一次刷新内的多个网页数据加载共享同一版本
            int version = _webGate.BeginRefresh();
            LoadCategoriesStatistics(version, start_, end_);
            LoadWebBrowseDataStatistics(version, start_, end_);
            LoadWebSitesTopData(version, start_, end_);

            //  汇总统计走版本闸门，避免后台线程直接写绑定属性
            _webGate.Run(version,
                () => new
                {
                    Total = _webData.GetBrowseDurationTotal(start_, end_),
                    SiteCount = _webData.GetBrowseSitesTotal(start_, end_),
                    PageCount = _webData.GetBrowsePagesTotal(start_, end_)
                },
                r =>
                {
                    WebTotalTime = r.Total;
                    WebSiteCount = r.SiteCount;
                    WebPageCount = r.PageCount;
                    WebTotalTimeText = Time.ToHoursString(WebTotalTime);
                });
        }

        /// <summary>
        /// 分类饼图
        /// </summary>
        /// <param name="start_"></param>
        /// <param name="end_"></param>
        private void LoadCategoriesStatistics(int version, DateTime start_, DateTime end_)
        {
            _webGate.Run(version,
                () =>
                {
                    var chartsDatas = new List<ChartsDataModel>();
                    var data = _webData.GetCategoriesStatistics(start_, end_);
                    foreach (var item in data)
                    {
                        var category = _webData.GetWebSiteCategory(item.ID);
                        var bindModel = new ChartsDataModel();
                        bindModel.Name = item.ID == 0 ? "未分类" : item.Name;
                        bindModel.Value = item.Value;
                        bindModel.Data = item;
                        bindModel.Color = item.ID == 0 ? "#ccc" : category != null ? category.Color : "#ccc";
                        bindModel.PopupText = bindModel.Name + " " + Time.ToString((int)item.Value);
                        bindModel.Icon = item.ID == 0 ? "" : category != null ? category.IconFile : "";
                        chartsDatas.Add(bindModel);
                    }

                    return chartsDatas.OrderByDescending(m => m.Value).ToList();
                },
                r => WebCategoriesPieData = r);
        }

        private void LoadWebBrowseDataStatistics(int version, DateTime start_, DateTime end_)
        {
            _webGate.Run(version,
                () =>
                {
                    var chartData = new List<ChartsDataModel>();
                    var data = _webData.GetBrowseDataByCategoryStatistics(start_, end_);
                    //  转换为图表格式数据
                    var emptyCategory = new WebSiteCategoryModel()
                    {
                        ID = 0,
                        Name = "未分类",
                        IconFile = "pack://application:,,,/guage;component/Resources/Icons/guage32.ico"
                    };

                    string[] colNames = { };

                    if (TabbarSelectedIndex == 1)
                    {
                        colNames = new string[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日", };
                    }
                    else if (TabbarSelectedIndex == 3)
                    {
                        colNames = new string[12];
                        for (int i = 0; i < 12; i++)
                        {
                            colNames[i] = (i + 1) + "月";
                        }
                    }

                    foreach (var item in data)
                    {
                        var category = _webData.GetWebSiteCategory(item.CategoryID);
                        if (item.CategoryID == 0)
                        {
                            category = emptyCategory;
                        }
                        if (category != null)
                        {
                            var dataItem = new ChartsDataModel()
                            {
                                Name = category.Name,
                                Icon = category.IconFile,
                                Values = item.Values,
                                Color = category.Color,
                                ColumnNames = colNames
                            };
                            if (category.ID == 0)
                            {
                                dataItem.Color = "#E5F7F6F2";
                            }
                            chartData.Add(dataItem);
                        }
                    }

                    return chartData;
                },
                r => WebBrowseStatisticsData = r);
        }

        private void LoadWebSitesTopData(int version, DateTime start_, DateTime end_)
        {
            _webGate.Run(version,
                () => MapToChartData(_webData.GetDateRangeWebSiteList(start_, end_, 10)),
                r => WebSitesTopData = r);
        }
        private void LoadWebSitesColSelectedData()
        {
            if (WebColSelectedIndex < 0)
            {
                WebSitesColSelectedTimeText = String.Empty;
                WebSitesColSelectedData = new List<ChartsDataModel>();
                return;
            }
            int version = _webGate.BeginRefresh();
            _webGate.Run(version,
                () =>
                {
                    bool isTime = false;
                    DateTime startTime = DateTime.Now, endTime = DateTime.Now;
                    string timeText = string.Empty;
                    if (TabbarSelectedIndex == 0)
                    {
                        //  天
                        var time = new DateTime(Date.Year, Date.Month, Date.Day, WebColSelectedIndex, 0, 0);
                        timeText = time.ToString("yyyy年MM月dd日 HH点");
                        isTime = true;
                        startTime = endTime = time;
                    }
                    else if (TabbarSelectedIndex == 1)
                    {
                        //  周
                        var weekDateArr = SelectedWeek.Name == "本周" ? Time.GetThisWeekDate() : Time.GetLastWeekDate();
                        var time = weekDateArr[0].AddDays(WebColSelectedIndex);
                        timeText = time.ToString("yyyy年MM月dd日");
                        startTime = endTime = time;
                    }
                    else if (TabbarSelectedIndex == 2)
                    {
                        //  月
                        var dateArr = Time.GetMonthDate(MonthDate);
                        var time = dateArr[0].AddDays(WebColSelectedIndex);
                        timeText = time.ToString("yyyy年MM月dd日");
                        startTime = endTime = time;
                    }
                    else if (TabbarSelectedIndex == 3)
                    {
                        //  年
                        var dateStart = new DateTime(YearDate.Year, WebColSelectedIndex + 1, 1);
                        var dateEnd = new DateTime(dateStart.Year, dateStart.Month, DateTime.DaysInMonth(dateStart.Year, dateStart.Month), 23, 59, 59);
                        timeText = dateStart.ToString("yyyy年MM月");
                        startTime = dateStart;
                        endTime = dateEnd;
                    }

                    var chartData = MapToChartData(_webData.GetDateRangeWebSiteList(startTime, endTime, 0, -1, isTime));
                    return new { TimeText = timeText, ChartData = chartData };
                },
                r =>
                {
                    WebSitesColSelectedTimeText = r.TimeText;
                    WebSitesColSelectedData = r.ChartData;
                });
        }
        private List<ChartsDataModel> MapToChartData(IEnumerable<Core.Models.Db.WebSiteModel> list)
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
    }
}
