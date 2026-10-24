using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UI.Servicers
{
    public interface IThemeServicer
    {
        void Init();
        void LoadTheme(string themeName, bool isRefresh = false);
        void SetMainWindow(MainWindow mainWindow);

        void UpdateWindowStyle();

        /// <summary>
        /// 即时应用外观预览（透明度/圆角/字体缩放），供设置页拖动滑块实时调用。
        /// committed=false 时只更新页面笔刷不重提交毛玻璃 accent（避免拖动闪烁），
        /// committed=true 时完整提交
        /// </summary>
        void ApplyAppearancePreview(bool committed);

        /// <summary>
        /// 切换主题时发生
        /// </summary>
        event EventHandler OnThemeChanged;
    }
}
