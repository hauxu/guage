using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace UI.Controls
{
    public class TPage : Page
    {
        /// <summary>
        /// 是否填充页面而不使用自适应滚动
        /// </summary>
        public bool IsFillPage
        {
            get { return (bool)GetValue(IsFillPageDependencyProperty); }
            set { SetValue(IsFillPageDependencyProperty, value); }
        }
        public static DependencyProperty IsFillPageDependencyProperty = DependencyProperty.Register("IsFillPage", typeof(bool), typeof(TPage));

        private PageContainer pageContainer;
        public TPage()
        {
            DefaultStyleKey = typeof(TPage);
            Loaded += TPage_Loaded;
            Unloaded += TPage_Unloaded;
        }

        private void TPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (pageContainer != null)
            {
                pageContainer.SizeChanged -= Pc_SizeChanged;
                pageContainer = null;
            }
        }

        private void TPage_Loaded(object sender, RoutedEventArgs e)
        {
            UpdatePageSize();
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            HandleEvent();
        }

        private void HandleEvent()
        {
            if (pageContainer != null)
            {
                pageContainer.SizeChanged -= Pc_SizeChanged;
                pageContainer = null;
            }

            if (IsFillPage)
            {
                //  查找父容器（每步判空，避免尚未挂载时 GetParent(null) 死循环）
                var parent = VisualTreeHelper.GetParent(this);
                while (parent != null && !(parent is PageContainer))
                {
                    parent = VisualTreeHelper.GetParent(parent);
                }

                pageContainer = parent as PageContainer;
                if (pageContainer != null)
                {
                    pageContainer.SizeChanged += Pc_SizeChanged;
                }
            }
        }

        private void Pc_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdatePageSize();
        }

        private void UpdatePageSize()
        {
            if (pageContainer == null)
            {
                return;
            }

            Width = pageContainer.ActualWidth;
            Height = pageContainer.ActualHeight;

            Debug.WriteLine("UpdatePageSize");
        }
    }
}
