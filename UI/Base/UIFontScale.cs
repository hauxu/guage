using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using UI.Controls.Base;

namespace UI.Base
{
    /// <summary>
    /// 按设置百分比缩放界面文字。窗口基础字号负责继承链，显式字号的控件单独按比例缩放。
    /// </summary>
    public static class UIFontScale
    {
        private static int _currentPercent = 105;

        /// <summary>
        /// 当前字体缩放系数（1.0 = 100%）：窗口尺寸需要跟随缩放时读取
        /// </summary>
        public static double CurrentScale => _currentPercent / 100.0;

        private static void UpdateFontResources(double scale)
        {
            if (Application.Current == null)
            {
                return;
            }

            Application.Current.Resources["AppFontSizeBody"] = UIFontSizes.Body * scale;
            Application.Current.Resources["AppFontSizeSecondary"] = UIFontSizes.Secondary * scale;
            Application.Current.Resources["AppFontSizeCaption"] = UIFontSizes.Caption * scale;
            Application.Current.Resources["AppFontSizeNav"] = UIFontSizes.Nav * scale;
            Application.Current.Resources["AppFontSizeNavCompact"] = UIFontSizes.NavCompact * scale;
            Application.Current.Resources["AppFontSizeTitle"] = UIFontSizes.Title * scale;
            Application.Current.Resources["AppFontSizeDisplay"] = UIFontSizes.Display * scale;

            //  标题栏高度与内容区边距随字体缩放（此前硬编码 40px，
            //  字体放大后标题栏压住内容区顶部）
            Application.Current.Resources["WindowTitleBarHeight"] = 32.0 * scale;
            Application.Current.Resources["WindowContentMargin"] = new Thickness(0, 40.0 * scale, 10, 10);
        }

        public static readonly DependencyProperty BaseFontSizeProperty =
            DependencyProperty.RegisterAttached(
                "BaseFontSize",
                typeof(double),
                typeof(UIFontScale),
                new PropertyMetadata(double.NaN));

        public static double GetBaseFontSize(DependencyObject obj) =>
            (double)obj.GetValue(BaseFontSizeProperty);

        public static void SetBaseFontSize(DependencyObject obj, double value) =>
            obj.SetValue(BaseFontSizeProperty, value);

        /// <summary>
        /// 用当前缓存的字体百分比缩放窗口（供弹窗等新窗口调用）。
        /// </summary>
        public static void ApplyToWindow(Window window)
        {
            if (window == null)
            {
                return;
            }

            ApplyToWindow(window, _currentPercent);
        }

        public static void ApplyToWindow(Window window, int fontSizePercent)
        {
            if (window == null)
            {
                return;
            }

            _currentPercent = Math.Min(150, Math.Max(80, fontSizePercent));
            double scale = _currentPercent / 100.0;
            UpdateFontResources(scale);
            window.FontSize = UIFontSizes.Body * scale;
            WalkVisualTree(window, scale);
        }

        public static void ApplyToRoot(DependencyObject root)
        {
            if (root == null)
            {
                return;
            }

            WalkVisualTree(root, _currentPercent / 100.0);
        }

        private static void WalkVisualTree(DependencyObject node, double scale)
        {
            if (node == null)
            {
                return;
            }

            //  被标记的元素（如 MarqueeTextBlock）连同其整个子树不参与缩放
            if (GetExcludeScale(node))
            {
                return;
            }

            ApplyToElement(node, scale);

            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
            {
                WalkVisualTree(VisualTreeHelper.GetChild(node, i), scale);
            }
        }

        private static void ApplyToElement(DependencyObject element, double scale)
        {
            if (ShouldSkip(element))
            {
                return;
            }

            if (element is TextBlock textBlock)
            {
                if (ShouldScaleFontSize(textBlock))
                {
                    ApplyFontSize(textBlock, scale, textBlock.FontSize);
                }

                return;
            }

            if (element is Control control)
            {
                if (ShouldScaleFontSize(control))
                {
                    ApplyFontSize(control, scale, control.FontSize);
                }
            }
        }

        private static bool ShouldScaleFontSize(DependencyObject element)
        {
            if (!double.IsNaN(GetBaseFontSize(element)))
            {
                return true;
            }

            return element.ReadLocalValue(TextElement.FontSizeProperty) != DependencyProperty.UnsetValue;
        }

        private static void ApplyFontSize(DependencyObject target, double scale, double currentSize)
        {
            double baseSize = GetBaseFontSize(target);
            if (double.IsNaN(baseSize) || baseSize <= 0)
            {
                baseSize = currentSize > 0 && !double.IsNaN(currentSize)
                    ? currentSize
                    : UIFontSizes.Body;
                SetBaseFontSize(target, baseSize);
            }

            double scaledSize = baseSize * scale;
            if (target is TextBlock textBlock)
            {
                textBlock.FontSize = scaledSize;
            }
            else if (target is Control control)
            {
                control.FontSize = scaledSize;
            }
        }

        private static bool ShouldSkip(DependencyObject element)
        {
            //  O(1) 排除：MarqueeTextBlock 在自身模板/子树上打排除标记
            //  （此前从每个元素沿父链回溯到根，复杂度 O(节点×树深) 是全树刷新卡顿主因之一）
            if (GetExcludeScale(element))
            {
                return true;
            }

            if (element is Icon)
            {
                return true;
            }

            FontFamily family = null;
            if (element is TextBlock textBlock)
            {
                family = textBlock.FontFamily;
            }
            else if (element is Control control)
            {
                family = control.FontFamily;
            }

            if (family != null && IsIconFontFamily(family.Source))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 缩放排除标记：被标记元素及其整个子树不参与字号缩放
        /// （MarqueeTextBlock 构造时自标）
        /// </summary>
        public static readonly DependencyProperty ExcludeScaleProperty =
            DependencyProperty.RegisterAttached(
                "ExcludeScale",
                typeof(bool),
                typeof(UIFontScale),
                new PropertyMetadata(false));

        public static bool GetExcludeScale(DependencyObject obj) =>
            (bool)obj.GetValue(ExcludeScaleProperty);

        public static void SetExcludeScale(DependencyObject obj, bool value) =>
            obj.SetValue(ExcludeScaleProperty, value);

        private static bool IsIconFontFamily(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return false;
            }

            return source.IndexOf("Segoe Fluent Icons", StringComparison.OrdinalIgnoreCase) >= 0
                || source.IndexOf("Segoe MDL2", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
