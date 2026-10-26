using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Models.Config
{
    /// <summary>
    /// 常规
    /// </summary>
    public class GeneralModel
    {
        [Config(Name = "开机自启动", Description = "在电脑启动时自动运行 更矩", Group = "基础")]
        /// <summary>
        /// 是否启用开机自启动
        /// </summary>
        public bool IsStartatboot { get; set; }

        /// <summary>
        /// 主题模式
        /// </summary>
        [Config(Options = "浅色|深色", Name = "主题模式", Description = "设置以浅色或深色模式显示", Group = "外观")]
        public int Theme { get; set; } = 0;
        /// <summary>
        /// 主题颜色
        /// </summary>
        [Config(Name = "主题颜色", Description = "", Group = "外观")]
        public string ThemeColor { get; set; } = "#2b20d9";
        /// <summary>
        /// 毛玻璃透明度百分比。0 为不透明的纯色亚克力，100 为完全透明的亚克力
        /// </summary>
        [Config(Name = "毛玻璃透明度", Description = "0 为不透明，100 为完全透明，可以直接看到下层窗口的内容。可拖动滑块或输入数字。仅 Windows 11 支持",
            Group = "外观", IsSlider = true, Min = 0, Max = 100, Unit = "%")]
        public int GlassTransparency { get; set; } = 100;
        /// <summary>
        /// 非最大化状态下的窗口圆角档位。圆角由系统绘制，因此只有系统提供的几档。
        /// </summary>
        [Config(Options = "直角|小圆角|圆角", Name = "窗口圆角",
            Description = "非最大化状态下的窗口圆角。圆角由系统绘制，只有直角、小圆角、圆角三档。",
            Group = "外观")]
        public int WindowCornerStyle { get; set; } = 2;
        /// <summary>
        /// 界面字体大小百分比。105 为默认大小。
        /// </summary>
        [Config(Name = "字体大小", Description = "调整界面文字显示大小。105 为默认，可拖动滑块或输入数字。",
            Group = "外观", IsSlider = true, Min = 80, Max = 150, Unit = "%")]
        public int FontSizePercent { get; set; } = 105;
        /// <summary>
        /// 是否保留界面大小
        /// </summary>
        [Config(Name = "保留窗口大小", Description = "保留当前的窗口大小，而非使用系统默认尺寸", Group = "外观")]
        public bool IsSaveWindowSize { get; set; } = false;
        /// <summary>
        /// 窗口宽度
        /// </summary>
        public double WindowWidth { get; set; } = 815;
        /// <summary>
        /// 窗口高度
        /// </summary>
        public double WindowHeight { get; set; } = 720;

        /// <summary>
        /// 启动页选择
        /// </summary>
        [Config(Options = "概览|统计|详细|分类|时长限制", Name = "启动页", Description = "设置打开主界面时显示的页面", Group = "基础")]
        public int StartPage { get; set; } = 0;
        [Config(Name = "启动软件时显示主界面", Description = "在开机自启动时此选项无效", Group = "基础")]
        /// <summary>
        /// 启动软件时显示主界面
        /// </summary>
        public bool IsStartupShowMainWindow { get; set; } = true;

        /// <summary>
        /// 数据存储目录。为空时使用软件安装目录。
        /// </summary>
        [Config(Name = "数据存储位置", Description = "时长记录、配置文件、下载图标和日志等数据的保存位置。修改后需要重启软件生效。",
            Group = "存储", IsPathPicker = true, Placeholder = "默认：软件安装目录")]
        public string StoragePath { get; set; } = string.Empty;
        /// <summary>
        /// 概览页最为频繁显示条数
        /// </summary>
        [Config(Options = "1|2|3|4|5|6|7|8|9|10", Name = "最为频繁显示条数", Description = "", Group = "概览页")]
        public int IndexPageFrequentUseNum { get; set; } = 2;
        /// <summary>
        /// 概览页最为频繁显示条数
        /// </summary>
        [Config(Options = "1|2|3|4|5|6|7|8|9|10|11|12|13|14|15|16|17|18|19|20", Name = "更多显示条数", Description = "", Group = "概览页")]
        public int IndexPageMoreNum { get; set; } = 11;

        /// <summary>
        /// 是否启用网站记录功能
        /// </summary>
        [Config(Name = "网站浏览统计", Description = "统计浏览器网站访问数据。扩展安装：方式一在 Chrome/Edge 商店搜索「guage Sentry」；方式二下载 ZIP 或本地 Guage.Chrome 文件夹载入（设置 > 关于）。开启后首页显示连接状态。仅统计 http/https 页面。本机端口 8908/8909，占用时自动尝试备用端口。", Group = "功能")]
        public bool IsWebEnabled { get; set; } = false;

        /// <summary>
        /// 浏览器插件 WebSocket 连接令牌（自动生成）。
        /// </summary>
        public string BrowserExtensionToken { get; set; } = string.Empty;
    }
}