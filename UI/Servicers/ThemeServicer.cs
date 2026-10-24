using Core.Librarys;
using Core.Servicers.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using UI.Base;
using UI.Controls;
using UI.ViewModels;

namespace UI.Servicers
{
    public class ThemeServicer : IThemeServicer
    {
        /// <summary>
        /// 当前主题名称
        /// </summary>
        private string themeName;
        private MainWindow mainWindow;
        private DispatcherTimer fontScaleDebounceTimer;
        private int pendingFontSizePercent = 100;
        private Collection<ResourceDictionary> MergedDictionaries;
        private readonly string[] themeOptions = { "Light", "Dark" };
        private readonly IAppConfig appConfig;

        public event EventHandler OnThemeChanged;

        public ThemeServicer(IAppConfig appConfig)
        {
            this.appConfig = appConfig;

            appConfig.ConfigChanged += AppConfig_ConfigChanged;

            MergedDictionaries = Application.Current.Resources.MergedDictionaries;
        }

        private void AppConfig_ConfigChanged(Core.Models.Config.ConfigModel oldConfig, Core.Models.Config.ConfigModel newConfig)
        {
            //  Save 也可能在后台线程触发（时长限制的锁内保存等）：
            //  主题字典/资源/窗口属性只能回 UI 线程操作
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() => AppConfig_ConfigChanged(oldConfig, newConfig)));
                return;
            }

            if (oldConfig.General.Theme != newConfig.General.Theme)
            {
                LoadTheme(themeOptions[newConfig.General.Theme]);
                OnThemeChanged?.Invoke(this, EventArgs.Empty);
            }

            if (oldConfig.General.ThemeColor != newConfig.General.ThemeColor)
            {
                LoadTheme(themeOptions[newConfig.General.Theme], true);
                OnThemeChanged?.Invoke(this, EventArgs.Empty);
            }

            if (oldConfig.General.IsSaveWindowSize != newConfig.General.IsSaveWindowSize)
            {
                HandleWindowSizeChangedEvent();
            }

            if (oldConfig.General.GlassTransparency != newConfig.General.GlassTransparency)
            {
                //  仅背景层变化：刷新 acrylic 笔刷与窗口 accent 即可。
                //  之前每 tick 广播 OnThemeChanged（4 个订阅者全量刷新）是拖动闪烁的放大器
                UpdateBackdrop();
            }

            if (oldConfig.General.WindowCornerRadius != newConfig.General.WindowCornerRadius)
            {
                UpdateWindowCornerRadius();
            }

            if (oldConfig.General.FontSizePercent != newConfig.General.FontSizePercent)
            {
                UpdateFontScale();
            }
        }

        public void Init()
        {
            LoadTheme(themeOptions[appConfig.GetConfig().General.Theme]);
        }

        /// <summary>
        /// 即时应用外观预览（透明度/圆角/字体缩放），供设置页拖动滑块时实时调用；
        /// 持久化由调用方防抖处理。
        /// committed=false（拖动中）只刷页面笔刷不重提交 accent——accent 重提交会让
        /// DWM 拆建模糊区（中间帧发黑），拖快了必闪；松手后才一次性完整提交
        /// </summary>
public void ApplyAppearancePreview(bool committed = true)
{
    UpdateBackdrop(committed);
    //  圆角拖动中实时预览：应用只是 CornerRadius/Clip 属性赋值，不重提交 accent，无闪烁
    UpdateWindowCornerRadius();
    ApplyFontScale(false);
}
        public void LoadTheme(string themeName, bool isRefresh = false)
        {
            if (string.IsNullOrEmpty(themeName))
            {
                //  设置默认主题
                themeName = themeOptions[0];
            }

            if (themeName == this.themeName && !isRefresh)
            {
                return;
            }

            var themeDict = GetResourceDictionary($"pack://application:,,,/guage;component/Resources/Themes/{themeName}.xaml");
            var controlDict = GetResourceDictionary($"pack://application:,,,/guage;component/Themes/Generic.xaml");
            if (themeDict == null || controlDict == null)
            {
                return;
            }

            //Collection<ResourceDictionary> resourceDictionaries = Application.Current.Resources.MergedDictionaries;
            //for (int i = 0; i < resourceDictionaries.Count; i++)
            //{
            //    ResourceDictionary resourceDictionary = resourceDictionaries[i];
            //    //if (resourceDictionary.FindName(""))
            //    //{
            //    if (resourceDictionary.Contains("ThemeName"))
            //    {
            //        foreach(var key in resourceDictionary.Keys)
            //        {
            //            resourceDictionary[key] = themeDict[key];
            //        }
            //        //resourceDictionary["ThemeColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#ff0000");
            //    }
            //    //}
            //}
            //  清除旧主题：无论是否首次加载都先移除已合并的主题字典
            //  （App.xaml 静态合并的 Light.xaml 也一并移除，避免同一主题重复合并两份）
            var oldStyles = MergedDictionaries.Where(m =>
                m.Source != null
                && (m.Source.OriginalString.Contains("Light.xaml")
                    || m.Source.OriginalString.Contains("Dark.xaml")
                    || m.Source.OriginalString.Contains("Generic.xaml"))).ToList();
            foreach (var oldStyle in oldStyles)
            {
                MergedDictionaries.Remove(oldStyle);
            }

            MergedDictionaries.Add(themeDict);
            MergedDictionaries.Add(controlDict);

            //if (configDict != null)
            //{
            //    //MergedDictionaries.Add(configDict);
            //}
            //if (controlDict != null)
            //{
            //    MergedDictionaries.Add(controlDict);
            //}
            this.themeName = themeName;

            UpdateWindowStyle();
            UpdateThemeColor();
            UpdateBackdrop();

            Debug.WriteLine("已加载主题：" + themeName);
        }

        /// <summary>
        /// 毛玻璃笔刷的命名后缀。主题字典里凡是 “XxxAcrylic” 都被视为对应 “Xxx” 的着色，
        /// 只取其 RGB，透明度统一由用户设置的百分比决定。新增可透光表面无需改动这里的代码。
        /// </summary>
        private const string AcrylicSuffix = "Acrylic";

        /// <summary>
        /// 按用户设定的透明度生成各层表面的笔刷并应用窗口材质。
        /// 透明度 0 时各层完全不透明，等同于纯色亚克力；100 时完全透明，只剩模糊。
        /// 各层叠加的层数不同，因此越靠内的表面实际越实，正文区域的可读性由此保证。
        /// </summary>
        private void UpdateBackdrop(bool committed = true)
        {
            var resources = Application.Current.Resources;

            int transparency = Math.Min(100, Math.Max(0, appConfig.GetConfig().General.GlassTransparency));
            bool isSupported = UI.Base.WindowBackdrop.IsSupported;
            //  系统不支持时强制不透明，否则窗口后面没有材质可透，会露出底色
            bool isGlass = isSupported && transparency > 0;
            //  全区间线性映射：滑块 0→255（不透明）、100→16（最透明），
            //  消除旧公式"后 15% 被 floor 抬平、拖动无变化"的死角
            double minAlpha = UI.Base.WindowBackdrop.MinimumTintAlpha;
            byte alpha = (byte)Math.Round(minAlpha + (255.0 - minAlpha) * (100 - transparency) / 100.0);

            var themeDict = MergedDictionaries.FirstOrDefault(m =>
                m.Source != null
                && themeName != null
                && m.Source.OriginalString.Contains(themeName));

            if (themeDict != null)
            {
                var acrylicKeys = themeDict.Keys.OfType<string>()
                    .Where(k => k.EndsWith(AcrylicSuffix) && k.Length > AcrylicSuffix.Length)
                    .ToList();

                foreach (string key in acrylicKeys)
                {
                    string targetKey = key.Substring(0, key.Length - AcrylicSuffix.Length);
                    SolidColorBrush target = null;
                    if (isGlass)
                    {
                        target = CreateTintBrush(themeDict[key] as SolidColorBrush, alpha);
                    }

                    //  同值跳过：拖动中只刷笔刷时避免全树 DynamicResource 无谓失效
                    var existing = resources[targetKey] as SolidColorBrush;
                    if (isGlass && existing != null && existing.Color.Equals(target?.Color))
                    {
                        continue;
                    }

                    if (isGlass)
                    {
                        resources[targetKey] = target;
                    }
                    else
                    {
                        //  移除应用级覆盖，让查找落回主题字典里的不透明笔刷
                        resources.Remove(targetKey);
                    }
                }

                if (isGlass)
                {
                    //  窗口底色改由毛玻璃自身的着色承担，这里必须让位为全透明。
                    //  否则窗口层会被着色两次，滑块的响应就不再是线性的了。
                    if (!(resources["WindowBackground"] is SolidColorBrush wb) || !wb.Color.Equals(System.Windows.Media.Colors.Transparent))
                    {
                        resources["WindowBackground"] = new SolidColorBrush(System.Windows.Media.Colors.Transparent);
                    }
                }
            }
            else if (!isGlass)
            {
                //  关玻璃时必须无条件清理应用级着色覆盖（回落主题字典的不透明笔刷），
                //  不能只放在 themeDict!=null 分支里：字典缺失时清理被跳过，
                //  会组合出"玻璃已关、窗口底色仍全透明"的近乎透明窗口
                foreach (var dict in MergedDictionaries)
                {
                    foreach (string key in dict.Keys.OfType<string>()
                        .Where(k => k.EndsWith(AcrylicSuffix) && k.Length > AcrylicSuffix.Length)
                        .ToList())
                    {
                        resources.Remove(key.Substring(0, key.Length - AcrylicSuffix.Length));
                    }
                }

                resources.Remove("WindowBackground");
            }

            if (mainWindow != null && !mainWindow.IsWindowClosed)
            {
                //  accent 重提交会让 DWM 拆建模糊区（中间帧发黑）：
                //  拖动中（committed=false）只刷笔刷做预览，松手提交时才真正重设
                if (committed)
                {
                    if (isGlass && themeDict == null)
                    {
                        //  主题字典缺失时无法取着色：宁可不提交 accent（窗口走不透明底色），
                        //  也不要提交出无着色的纯模糊
                        Core.Librarys.Logger.Error("主题字典缺失，跳过毛玻璃 accent 提交 themeName=" + themeName);
                    }
                    else
                    {
                        mainWindow.SetAcrylic(isGlass, GetWindowTintColor(themeDict), alpha);
                    }
                }
                mainWindow.SetDarkTitleBar(themeName == "Dark");
            }
        }

        /// <summary>
        /// 取当前主题的窗口底色，作为毛玻璃的着色。
        /// </summary>
        private static System.Windows.Media.Color GetWindowTintColor(ResourceDictionary themeDict)
        {
            var brush = themeDict?["WindowBackgroundAcrylic"] as SolidColorBrush;
            return brush != null ? brush.Color : System.Windows.Media.Colors.White;
        }

        /// <summary>
        /// 取着色笔刷的 RGB，套上指定透明度生成新笔刷。
        /// </summary>
        private static SolidColorBrush CreateTintBrush(SolidColorBrush tint, byte alpha)
        {
            if (tint == null)
            {
                return new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, 255, 255, 255));
            }
            var color = tint.Color;
            return new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, color.R, color.G, color.B));
        }

        /// <summary>
        /// 刷新主题颜色
        /// </summary>
        private void UpdateThemeColor()
        {

            var config = appConfig.GetConfig();
            if (string.IsNullOrEmpty(config.General.ThemeColor))
            {
                //  清除应用级覆盖，让查找回落到主题字典的默认色（修复改回默认色后残留旧自定义色）
                Application.Current.Resources.Remove("ThemeColor");
                Application.Current.Resources.Remove("ThemeBrush");
                StateData.ThemeColor = ((System.Windows.Media.Color)Application.Current.Resources["ThemeColor"]).ToString();
                return;
            }

            StateData.ThemeColor = config.General.ThemeColor;
            Application.Current.Resources["ThemeColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(config.General.ThemeColor);
            Application.Current.Resources["ThemeBrush"] = UI.Base.Color.Colors.GetFromString(config.General.ThemeColor);
        }

        public void SetMainWindow(MainWindow mainWindow)
        {
            if (this.mainWindow?.PageContainer != null)
            {
                this.mainWindow.PageContainer.OnLoadPaged -= PageContainer_OnLoadPaged;
            }

            this.mainWindow = mainWindow;
            HandleWindowSizeChangedEvent();
            //  主窗口是在主题加载之后才创建的，这里补一次材质应用
            UpdateBackdrop();
            UpdateWindowCornerRadius();

            if (mainWindow?.PageContainer != null)
            {
                mainWindow.PageContainer.OnLoadPaged += PageContainer_OnLoadPaged;
            }

            ApplyFontScale(immediate: true);
        }

        private void PageContainer_OnLoadPaged(object sender, EventArgs e)
        {
            if (sender is PageContainer pageContainer && pageContainer.Content is DependencyObject pageRoot)
            {
                UIFontScale.ApplyToRoot(pageRoot);
            }
        }

        private void UpdateFontScale()
        {
            ApplyFontScale(immediate: false);
        }

        private void ApplyFontScale(bool immediate)
        {
            if (mainWindow == null || mainWindow.IsWindowClosed)
            {
                return;
            }

            pendingFontSizePercent = appConfig.GetConfig().General.FontSizePercent;
            if (immediate)
            {
                UIFontScale.ApplyToWindow(mainWindow, pendingFontSizePercent);
                return;
            }

            if (fontScaleDebounceTimer == null)
            {
                fontScaleDebounceTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(80)
                };
                fontScaleDebounceTimer.Tick += (s, e) =>
                {
                    fontScaleDebounceTimer.Stop();
                    if (mainWindow != null && !mainWindow.IsWindowClosed)
                    {
                        UIFontScale.ApplyToWindow(mainWindow, pendingFontSizePercent);
                    }
                };
            }

            fontScaleDebounceTimer.Stop();
            fontScaleDebounceTimer.Start();
        }

        private void UpdateWindowCornerRadius()
        {
            if (mainWindow == null || mainWindow.IsWindowClosed)
            {
                return;
            }

            //  layered 窗口 WPF 直渲染任意半径：0=直角，1-24px 平滑调节
            int radius = Math.Min(24, Math.Max(0, appConfig.GetConfig().General.WindowCornerRadius));
            mainWindow.ApplyCornerRadius(radius);
        }

        private void HandleWindowSizeChangedEvent()
        {
            if (mainWindow == null || mainWindow.IsWindowClosed) return;

            mainWindow.SizeChanged -= MainWindow_SizeChanged;

            var config = appConfig.GetConfig();
            if (config.General.IsSaveWindowSize)
            {
                //  保存窗口大小信息
                mainWindow.SizeChanged += MainWindow_SizeChanged;
            }
        }

        //  窗口尺寸写盘防抖：SizeChanged 在拖拽缩放时每像素触发一次，
        //  每次都全量 JSON 序列化+写盘是缩放卡顿的主因；内存值即时更新，
        //  停止缩放 500ms 后才落盘（App.OnExit 兜底 Save 保证退出不丢）
        private System.Windows.Threading.DispatcherTimer _sizeSaveDebounceTimer;

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var config = appConfig.GetConfig();
            config.General.WindowWidth = mainWindow.ActualWidth;
            config.General.WindowHeight = mainWindow.ActualHeight;

            if (_sizeSaveDebounceTimer == null)
            {
                _sizeSaveDebounceTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                _sizeSaveDebounceTimer.Tick += (s, e2) =>
                {
                    _sizeSaveDebounceTimer.Stop();
                    appConfig.Save();
                };
            }
            _sizeSaveDebounceTimer.Stop();
            _sizeSaveDebounceTimer.Start();
        }

        public void UpdateWindowStyle()
        {
            //  已废弃："WindowStyle" 资源键在任何主题字典中都不存在，
            //  此方法从未真正应用过样式（纯死代码），保留空实现兼容既有调用
        }

        private ResourceDictionary GetResourceDictionary(string uri)
        {
            try
            {
                return new ResourceDictionary { Source = new Uri(uri, UriKind.RelativeOrAbsolute) };
            }
            catch (Exception e)
            {
                Logger.Error(e.ToString());
                return null;
            }
        }
    }
}
