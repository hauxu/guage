using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using UI.Controls.Charts.Model;
using UI.Controls.Select;

namespace UI.Models
{
    public class IndexPageModel : ModelBase
    {
        private ObservableCollection<string> TabbarData_;
        /// <summary>
        /// tabbar data
        /// </summary>
        public ObservableCollection<string> TabbarData
        {
            get { return TabbarData_; }
            set { TabbarData_ = value; OnPropertyChanged(); }
        }

        private int TabbarSelectedIndex_;
        /// <summary>
        /// tabbar selected item index
        /// </summary>
        public int TabbarSelectedIndex
        {
            get { return TabbarSelectedIndex_; }
            set
            {
                TabbarSelectedIndex_ = value;
                OnPropertyChanged();
            }
        }


        private List<ChartsDataModel> WeekData_;
        /// <summary>
        /// week data
        /// </summary>
        public List<ChartsDataModel> WeekData
        {
            get { return WeekData_; }
            set { WeekData_ = value; OnPropertyChanged(); }
        }

        private bool IsLoading_;
        public bool IsLoading { get { return IsLoading_; } set { IsLoading_ = value; OnPropertyChanged(); } }

        private ContextMenu AppContextMenu_;
        public ContextMenu AppContextMenu { get { return AppContextMenu_; } set { AppContextMenu_ = value; OnPropertyChanged(); } }

        private int FrequentUseNum_;
        /// <summary>
        /// 最为频繁条数
        /// </summary>
        public int FrequentUseNum { get { return FrequentUseNum_; } set { FrequentUseNum_ = value; OnPropertyChanged(); } }
        private int MoreNum_;
        /// <summary>
        /// 更多条数
        /// </summary>
        public int MoreNum { get { return MoreNum_; } set { MoreNum_ = value; OnPropertyChanged(); } }

        private List<ChartsDataModel> WebFrequentUseData_;
        /// <summary>
        /// 网页最为频繁数据
        /// </summary>
        public List<ChartsDataModel> WebFrequentUseData
        {
            get { return WebFrequentUseData_; }
            set { WebFrequentUseData_ = value; OnPropertyChanged(); }
        }
        private SelectItemModel MoreType_;
        /// <summary>
        /// 更多展示数据类型（0=应用/1=网页）
        /// </summary>
        public SelectItemModel MoreType { get { return MoreType_; } set { MoreType_ = value; OnPropertyChanged(); } }

        private List<ChartsDataModel> AppMoreData_;
        /// <summary>
        /// 应用更多数据
        /// </summary>
        public List<ChartsDataModel> AppMoreData
        {
            get { return AppMoreData_; }
            set { AppMoreData_ = value; OnPropertyChanged(); }
        }
        private List<ChartsDataModel> WebMoreData_;
        /// <summary>
        /// 网页更多数据
        /// </summary>
        public List<ChartsDataModel> WebMoreData
        {
            get { return WebMoreData_; }
            set { WebMoreData_ = value; OnPropertyChanged(); }
        }
        private ContextMenu WebSiteContextMenu_;
        /// <summary>
        /// 网站右键菜单
        /// </summary>
        public ContextMenu WebSiteContextMenu { get { return WebSiteContextMenu_; } set { WebSiteContextMenu_ = value; OnPropertyChanged(); } }

        private bool IsBrowserExtensionIndicatorVisible_;
        /// <summary>
        /// 是否显示浏览器扩展连接指示器
        /// </summary>
        public bool IsBrowserExtensionIndicatorVisible
        {
            get { return IsBrowserExtensionIndicatorVisible_; }
            set
            {
                IsBrowserExtensionIndicatorVisible_ = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BrowserExtensionIndicatorVisibility));
            }
        }

        public Visibility BrowserExtensionIndicatorVisibility =>
            IsBrowserExtensionIndicatorVisible ? Visibility.Visible : Visibility.Collapsed;

        private bool IsBrowserExtensionConnected_;
        /// <summary>
        /// 浏览器扩展是否已连接
        /// </summary>
        public bool IsBrowserExtensionConnected
        {
            get { return IsBrowserExtensionConnected_; }
            set { IsBrowserExtensionConnected_ = value; OnPropertyChanged(); }
        }

        private string BrowserExtensionStatusText_ = "未连接";
        /// <summary>
        /// 浏览器扩展连接状态文案
        /// </summary>
        public string BrowserExtensionStatusText
        {
            get { return BrowserExtensionStatusText_; }
            set { BrowserExtensionStatusText_ = value; OnPropertyChanged(); }
        }
    }
}
