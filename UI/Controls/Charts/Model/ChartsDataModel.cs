using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UI.Models;

namespace UI.Controls.Charts.Model
{
    public class ChartsDataModel : UINotifyPropertyChanged
    {
        private string Name_;
        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get { return Name_; } set { Name_ = value; OnPropertyChanged(); } }

        /// <summary>
        /// 值
        /// </summary>
        private double Value_;
        public double Value { get { return Value_; } set { if (Value_ != value) { Value_ = value; OnPropertyChanged(); } } }
        /// <summary>
        /// 列名
        /// </summary>
        private string Tag_;
        public string Tag { get { return Tag_; } set { if (Tag_ != value) { Tag_ = value; OnPropertyChanged(); } } }
        /// <summary>
        /// 悬浮文本
        /// </summary>
        public string PopupText { get; set; }
        /// <summary>
        /// 是否选中
        /// </summary>
        public bool IsSelected { get; set; }
        /// <summary>
        /// 图标文件
        /// </summary>
        public string Icon { get; set; }

        /// <summary>
        /// 时间（仅在类型为Month样式时有效）
        /// </summary>
        public DateTime DateTime { get; set; }

        /// <summary>
        /// 数据模型
        /// </summary>
        public object Data { get; set; }

        /// <summary>
        /// 值
        /// </summary>
        public double[] Values { get; set; }
        /// <summary>
        /// 列名
        /// </summary>
        public string[] ColumnNames { get; set; }
        /// <summary>
        /// 颜色
        /// </summary>
        public string Color { get; set; }

        private List<ChartBadgeModel> BadgeList_;
        /// <summary>
        /// 徽章
        /// </summary>
        public List<ChartBadgeModel> BadgeList { get { return BadgeList_; } set { BadgeList_ = value; OnPropertyChanged(); } }

        /// <summary>
        /// 最大值
        /// </summary>
        public double MaxValue { get; set; }

        /// <summary>
        /// 新项入场动画标记：仅该项首次出现的重建播放一次，播毕即清（防 5 秒刷新重播）
        /// </summary>
        public bool IsNewAnimation { get; set; }
    }
}
