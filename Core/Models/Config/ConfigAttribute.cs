using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Models.Config
{
    [AttributeUsage(AttributeTargets.Class |
 AttributeTargets.Constructor |
 AttributeTargets.Field |
 AttributeTargets.Method |
 AttributeTargets.Property)]

    public class ConfigAttribute : System.Attribute
    {
        public string Name;
        public string Description;
        public string ToggleTrueText;
        public string ToggleFalseText;
        public string Group;
        public int Index;
        public bool IsName;
        public string Placeholder;
        public bool IsCanRepeat;
        public bool IsCanImportExport;
        public string Options;
        public bool IsBeta;
        /// <summary>
        /// 整数配置项是否以滑块呈现。为 false 时按 Options 渲染为下拉选择
        /// </summary>
        public bool IsSlider;
        /// <summary>
        /// 滑块取值下限
        /// </summary>
        public int Min;
        /// <summary>
        /// 滑块取值上限
        /// </summary>
        public int Max;
        /// <summary>
        /// 数值单位，显示在输入框右侧
        /// </summary>
        public string Unit;
        /// <summary>
        /// 字符串配置项是否以文件夹选择器呈现
        /// </summary>
        public bool IsPathPicker;
        public ConfigAttribute()
        {
            ToggleTrueText = "开";
            ToggleFalseText = "关";
            IsCanRepeat = true;
            Index = 0;
            IsName = false;
            IsCanImportExport = false;
            Options = string.Empty;
            IsBeta = false;
            IsSlider = false;
            Min = 0;
            Max = 100;
            Unit = string.Empty;
            IsPathPicker = false;
        }


    }
}
