using Core.Librarys.Image;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using UI.Base.Animation;
using UI.Controls.Base;
using UI.Controls.Charts.Model;

namespace UI.Controls.Charts
{
    public class ChartsItemTypeList : Control
    {
        #region ChartsType 样式类型
        /// <summary>
        /// 样式类型
        /// </summary>
        public ChartsDataModel Data
        {
            get { return (ChartsDataModel)GetValue(DataProperty); }
            set { SetValue(DataProperty, value); }
        }
        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register("Data",
                typeof(ChartsDataModel),
                typeof(ChartsItemTypeList));


        #endregion

        #region MaxValue 最大值
        /// <summary>
        /// 样式类型
        /// </summary>
        public double MaxValue
        {
            get { return (double)GetValue(MaxValueProperty); }
            set { SetValue(MaxValueProperty, value); }
        }
        public static readonly DependencyProperty MaxValueProperty =
            DependencyProperty.Register("MaxValue",
                typeof(double),
                typeof(ChartsItemTypeList));


        #endregion

        #region IsLoading 是否正在加载中
        /// <summary>
        /// 是否正在加载中
        /// </summary>
        public bool IsLoading
        {
            get { return (bool)GetValue(IsLoadingProperty); }
            set { SetValue(IsLoadingProperty, value); }
        }
        public static readonly DependencyProperty IsLoadingProperty =
            DependencyProperty.Register("IsLoading",
                typeof(bool),
                typeof(ChartsItemTypeList));


        #endregion

        #region IsShowBadge 是否显示徽章
        /// <summary>
        /// 是否显示徽章
        /// </summary>
        public bool IsShowBadge
        {
            get { return (bool)GetValue(IsShowBadgeProperty); }
            set { SetValue(IsShowBadgeProperty, value); }
        }
        public static readonly DependencyProperty IsShowBadgeProperty =
            DependencyProperty.Register("IsShowBadge",
                typeof(bool),
                typeof(ChartsItemTypeList));


        #endregion

        #region 图标大小
        /// <summary>
        /// 图标大小
        /// </summary>
        public double IconSize
        {
            get { return (double)GetValue(IconSizeProperty); }
            set { SetValue(IconSizeProperty, value); }
        }
        public static readonly DependencyProperty IconSizeProperty =
            DependencyProperty.Register("IconSize",
                typeof(double),
                typeof(ChartsItemTypeList), new PropertyMetadata((double)25));


        #endregion
        private MarqueeTextBlock NameTextObj;
        private TextBlock ValueTextObj;
        private Rectangle ValueBlockObj;
        private StackPanel ValueContainer;
        //private Grid ValueContainer;
        private Image IconObj;
        private bool isRendering = false;
        private bool IsAddEvent = false;
        private ChartsDataModel _boundData;
        public ChartsItemTypeList()
        {
            DefaultStyleKey = typeof(ChartsItemTypeList);
            Unloaded += ChartsItemTypeList_Unloaded;
        }

        private void ChartsItemTypeList_Unloaded(object sender, RoutedEventArgs e)
        {
            Unloaded -= ChartsItemTypeList_Unloaded;
            Loaded -= ChartsItemTypeList_Loaded;
            UnbindData();
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            NameTextObj = GetTemplateChild("NameTextObj") as MarqueeTextBlock;
            ValueTextObj = GetTemplateChild("ValueTextObj") as TextBlock;
            ValueBlockObj = GetTemplateChild("ValueBlockObj") as Rectangle;
            //ValueContainer = GetTemplateChild("ValueContainer") as Grid;
            ValueContainer = GetTemplateChild("ValueContainer") as StackPanel;

            IconObj = GetTemplateChild("IconObj") as Image;

            if (!IsAddEvent)
            {
                Loaded += ChartsItemTypeList_Loaded;
                IsAddEvent = true;
            }

            var parent = Parent as FrameworkElement;
            if (parent != null)
            {
                //  订阅前先退订，避免重复叠加处理器
                parent.SizeChanged -= Parent_SizeChanged;
                parent.SizeChanged += Parent_SizeChanged;
            }
        }

        private void Parent_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateValueBlockWidth();
        }

        private void ChartsItemTypeList_Loaded(object sender, RoutedEventArgs e)
        {
            Render();
        }

        /// <summary>
        /// 入场抑制标记：5 秒整表刷新重建容器时不重播入场动画
        /// （由 Charts.RenderListStyle 依据"是否首屏"设置，渲染后自动复位）
        /// </summary>
        internal static bool SuppressEntrance;

        private void Render()
        {
            if (isRendering || Data == null)
            {
                return;
            }
            isRendering = true;
            //NameTextObj.Text = Data.Name;
            //对部分程序未获取程序名的程序使用路径中名字作为程序名
            //if (Data.Name.Trim() == "" && Data.PopupText.Trim() != "")
            //{
            //    FileInfo fi = new FileInfo(Data.PopupText);
            //    NameTextObj.Text = fi.Name.Replace(fi.Extension, "");
            //}

            ValueTextObj.Text = Data.Tag;
            IconObj.Source = Imager.Load(Data.Icon);
            BindData();

            //  订阅前先退订，避免每次渲染重复叠加处理器
            ValueTextObj.SizeChanged -= ValueTextObj_SizeChanged;
            ValueTextObj.SizeChanged += ValueTextObj_SizeChanged;

            if (AnimationBudget.ReduceMotion)
            {
                return;
            }

            if (Data.IsNewAnimation)
            {
                //  新应用第一次上榜：下坠入场（"掉进来"）
                Data.IsNewAnimation = false;
                ElementMotion.PlayDropIn(this, 0, 18);
            }
            else if (!SuppressEntrance)
            {
                //  首屏列表行轻入场；整表刷新（SuppressEntrance）不重播
                ElementMotion.PlayEntrance(this, 0, 8);
            }
        }

        private void ValueTextObj_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (MaxValue <= 0)
            {
                return;
            }

            UpdateValueBlockWidth();
        }

        private void BindData()
        {
            UnbindData();
            _boundData = Data;
            if (_boundData != null)
            {
                _boundData.PropertyChanged += BoundData_PropertyChanged;
            }
        }

        private void UnbindData()
        {
            if (_boundData != null)
            {
                _boundData.PropertyChanged -= BoundData_PropertyChanged;
                _boundData = null;
            }
        }

        private void BoundData_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (Data == null || ValueTextObj == null)
            {
                return;
            }

            if (e.PropertyName == nameof(ChartsDataModel.Tag))
            {
                ValueTextObj.Text = Data.Tag;
            }
            else if (e.PropertyName == nameof(ChartsDataModel.Value))
            {
                UpdateValueBlockWidth();
            }
        }

        public void UpdateValueBlockWidth()
        {
            if (Data == null || !IsLoaded)
            {
                return;
            }
            //  Math.Max(0,...) 钳制：极小窗口下 ValueTextObj 宽度可能超过容器，
            //  差值为负——Width 不接受负值，抛 ArgumentException 会发生在 WM_SIZE
            //  同步布局内直接导致程序退出（缩到最小闪退的根因）
            double width = Math.Min(1, MaxValue <= 0 ? 0 : Data.Value / MaxValue)
                * (ValueContainer.ActualWidth * 0.95 - ValueTextObj.ActualWidth);
            ValueBlockObj.Width = Math.Max(0, width);
        }

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            VisualStateManager.GoToState(this, "MouseOver", true);
        }
        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            VisualStateManager.GoToState(this, "NormalState", true);
        }
    }
}
