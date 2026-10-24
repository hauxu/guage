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
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using UI.Base.Animation;
using System.Windows.Threading;
using UI.Controls.Base;
using UI.Controls.Charts.Model;

namespace UI.Controls.Charts
{
    public class ChartsItemTypeCard : Control
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
                typeof(ChartsItemTypeCard),
                new PropertyMetadata(null, OnDataChanged));


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
                typeof(ChartsItemTypeCard));


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
                typeof(ChartsItemTypeCard));


        #endregion
        private MarqueeTextBlock NameTextObj;
        private TextBlock ValueTextObj;
        private Rectangle ValueBlockObj;
        private StackPanel ValueContainer;
        private Image IconObj;
        private BlurEffect _valueBlurEffect;
        private bool isRendering = false;
        private bool IsAddEvent = false;
        private ChartsDataModel _boundData;
        public ChartsItemTypeCard()
        {
            DefaultStyleKey = typeof(ChartsItemTypeCard);
            Unloaded += ChartsItemTypeCard_Unloaded;
        }

        private void ChartsItemTypeCard_Unloaded(object sender, RoutedEventArgs e)
        {
            Unloaded -= ChartsItemTypeCard_Unloaded;
            Loaded -= ChartsItemTypeCard_Loaded;
            UnbindData();
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            NameTextObj = GetTemplateChild("NameTextObj") as MarqueeTextBlock;
            ValueTextObj = GetTemplateChild("ValueTextObj") as TextBlock;
            ValueBlockObj = GetTemplateChild("ValueBlockObj") as Rectangle;
            ValueContainer = GetTemplateChild("ValueContainer") as StackPanel;
            IconObj = GetTemplateChild("IconObj") as Image;

            if (!IsAddEvent)
            {
                Loaded += ChartsItemTypeCard_Loaded;
                IsAddEvent = true;
            }
        }

        private void ChartsItemTypeCard_Loaded(object sender, RoutedEventArgs e)
        {
            Render();
        }

        private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var card = (ChartsItemTypeCard)d;
            card.isRendering = false;
            card.Render();
        }

        /// <summary>
        /// 入场抑制标记（5 秒整表刷新重建容器时不重播入场动画）
        /// </summary>
        internal static bool SuppressEntrance;

        private void Render()
        {
            if (isRendering || Data == null)
            {
                return;
            }
            isRendering = true;

            if (ValueTextObj == null || IconObj == null || ValueBlockObj == null)
            {
                isRendering = false;
                return;
            }

            //  名称由模板中的 MarqueeTextBlock 绑定并处理超长滚动
            NameTextObj?.RefreshLayout();
            Dispatcher.BeginInvoke(new Action(() => NameTextObj?.RefreshLayout()), DispatcherPriority.Loaded);
            Dispatcher.BeginInvoke(new Action(() => NameTextObj?.RefreshLayout()), DispatcherPriority.Render);
            ValueTextObj.Text = Data.Tag;
            IconObj.Source = Imager.Load(Data.Icon);
            BindData();
            UpdateValueBlockSize();

            //  订阅前先退订，避免每次渲染重复叠加处理器
            ValueTextObj.SizeChanged -= ValueTextObj_SizeChanged;
            ValueTextObj.SizeChanged += ValueTextObj_SizeChanged;

            if (AnimationBudget.ReduceMotion)
            {
                return;
            }

            if (Data.IsNewAnimation)
            {
                Data.IsNewAnimation = false;
                ElementMotion.PlayDropIn(this, 0, 18);
            }
            else if (!SuppressEntrance)
            {
                ElementMotion.PlayEntrance(this, 0, 8);
            }
        }

        private void ValueTextObj_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateValueBlockSize();
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
                UpdateValueBlockSize();
            }
            else if (e.PropertyName == nameof(ChartsDataModel.Name))
            {
                NameTextObj?.RefreshLayout();
            }
        }

        private void UpdateValueBlockSize()
        {
            if (Data == null || ValueBlockObj == null || MaxValue <= 0)
            {
                return;
            }

            double ratio = Math.Min(1, Data.Value / MaxValue);
            double size = ratio * ActualWidth / 3;
            ValueBlockObj.Width = ValueBlockObj.Height = size;

            if (_valueBlurEffect == null)
            {
                _valueBlurEffect = new BlurEffect
                {
                    RenderingBias = RenderingBias.Performance
                };
                ValueBlockObj.Effect = _valueBlurEffect;
            }

            _valueBlurEffect.Radius = size;
        }

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            NameTextObj?.PauseMarqueeAnimation();
            VisualStateManager.GoToState(this, "MouseOver", true);
        }
        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            NameTextObj?.ResumeMarqueeAnimation();
            VisualStateManager.GoToState(this, "NormalState", true);
        }
    }
}
