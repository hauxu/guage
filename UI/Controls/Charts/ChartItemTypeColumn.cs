using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using UI.Base.Animation;
using UI.Base.Color;

namespace UI.Controls.Charts
{
    public class ChartItemTypeColumn : Control
    {
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
                typeof(ChartItemTypeColumn));


        #endregion

        #region 值
        /// <summary>
        /// 样式类型
        /// </summary>
        public double Value
        {
            get { return (double)GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value",
                typeof(double),
                typeof(ChartItemTypeColumn));


        #endregion
        #region 颜色
        /// <summary>
        /// 样式类型
        /// </summary>
        public string Color
        {
            get { return (string)GetValue(ColorProperty); }
            set { SetValue(ColorProperty, value); }
        }
        public static readonly DependencyProperty ColorProperty =
            DependencyProperty.Register("Color",
                typeof(string),
                typeof(ChartItemTypeColumn));


        #endregion
        #region 列名
        /// <summary>
        /// 样式类型
        /// </summary>
        public string ColumnName
        {
            get { return (string)GetValue(ColumnNameProperty); }
            set { SetValue(ColumnNameProperty, value); }
        }
        public static readonly DependencyProperty ColumnNameProperty =
            DependencyProperty.Register("ColumnName",
                typeof(string),
                typeof(ChartItemTypeColumn));


        #endregion

        private bool isRendering = false;
        private Rectangle ValueBlockObj;
        private Border ValueContainer;
        private bool IsAddEvent = false;
        //  是否已播过首次生长动画（后续数据刷新只做短过渡，避免反复"生长"）
        private bool _hasGrown;
        public ChartItemTypeColumn()
        {
            DefaultStyleKey = typeof(ChartItemTypeColumn);
            Unloaded += ChartItemTypeColumn_Unloaded;
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            ValueBlockObj = GetTemplateChild("ValueBlockObj") as Rectangle;
            ValueContainer = GetTemplateChild("ValueContainer") as Border;
            if (!IsAddEvent)
            {
                Loaded += ChartItemTypeColumn_Loaded;
            }
        }

        private void ChartItemTypeColumn_Unloaded(object sender, RoutedEventArgs e)
        {
            Loaded -= ChartItemTypeColumn_Loaded;
            Unloaded -= ChartItemTypeColumn_Unloaded;
        }

        private void ChartItemTypeColumn_Loaded(object sender, RoutedEventArgs e)
        {
            Render();
            IsAddEvent = true;
        }


        private void Render()
        {
            if (isRendering)
            {
                return;
            }

            if (!string.IsNullOrEmpty(Color))
            {
                ValueBlockObj.Fill = UI.Base.Color.Colors.GetFromString(Color);
            }
            //isRendering = true;

            Update();
            //this.SizeChanged += (e, c) =>
            //{
            //    if (MaxValue <= 0)
            //    {
            //        return;
            //    }
            //    Update();
            //};
        }

        public void Update()
        {
            double target = (Value / MaxValue) * (ValueContainer.ActualHeight);

            if (AnimationBudget.ReduceMotion)
            {
                ValueBlockObj.Height = target;
                return;
            }

            if (!_hasGrown)
            {
                //  首次载入：从 0 弹性长到目标（柱子"长出来"）
                _hasGrown = true;
                ValueBlockObj.Height = 0;
                var grow = new System.Windows.Media.Animation.DoubleAnimation(0, target, TimeSpan.FromMilliseconds(450))
                {
                    EasingFunction = new SpringEase { Bounce = 0.35, EasingMode = EasingMode.EaseOut }
                };
                ValueBlockObj.BeginAnimation(System.Windows.Shapes.Rectangle.HeightProperty, grow);
                return;
            }

            //  数据刷新：从当前高度平滑过渡
            var transition = new System.Windows.Media.Animation.DoubleAnimation(target, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ValueBlockObj.BeginAnimation(System.Windows.Shapes.Rectangle.HeightProperty, transition);
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
