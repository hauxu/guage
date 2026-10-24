using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using UI.Base;
using UI.Base.Animation;
using UI.Controls.Navigation.Models;

namespace UI.Controls.Tabbar
{
    public class Tabbar : Control
    {

        public Color SelectedTextColor
        {
            get { return (Color)GetValue(SelectedTextColorProperty); }
            set { SetValue(SelectedTextColorProperty, value); }
        }
        public static readonly DependencyProperty SelectedTextColorProperty =
            DependencyProperty.Register("SelectedTextColor", typeof(Color), typeof(Tabbar));
        public int SelectedIndex
        {
            get { return (int)GetValue(SelectedIndexProperty); }
            set { SetValue(SelectedIndexProperty, value); }
        }
        public static readonly DependencyProperty SelectedIndexProperty =
            DependencyProperty.Register("SelectedIndex", typeof(int), typeof(Tabbar), new PropertyMetadata((int)0, new PropertyChangedCallback(OnSelectedItemChanged)));

        private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = d as Tabbar;
            if (e.NewValue != e.OldValue)
            {
                control.ScrollToActive(int.Parse(e.OldValue.ToString()));
            }
        }


        public ObservableCollection<string> Data
        {
            get
            {
                return (ObservableCollection<string>)GetValue(DataProperty);
            }
            set
            {
                SetValue(DataProperty, value);
            }
        }
        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register("Data", typeof(ObservableCollection<string>), typeof(Tabbar));


        //private StackPanel ItemsPanel;
        private List<TextBlock> ItemsDictionary;
        private Grid ItemsContainer;
        //  选中标记块
        private Border ActiveBlock;
        //  标记块的横向位移
        private TranslateTransform activeTranslate;
        //  标记块是否已完成首次定位，首次不播放动画
        private bool isActiveBlockPlaced;

        //  未选中项的文字颜色
        private static readonly Color UnselectedTextColor = (Color)ColorConverter.ConvertFromString("#CCCCCC");

        public Tabbar()
        {
            DefaultStyleKey = typeof(Tabbar);
            ItemsDictionary = new List<TextBlock>();
        }



        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            //ItemsPanel = GetTemplateChild("ItemsPanel") as StackPanel;
            ActiveBlock = GetTemplateChild("ActiveBlock") as Border;
            ItemsContainer = GetTemplateChild("ItemsContainer") as Grid;

            CreateAnimations();
            Render();
        }


        private void AddItem(string item, int col)
        {
            if (ItemsContainer != null)
            {
                var control = new TextBlock();
                control.TextAlignment = TextAlignment.Center;
                control.Text = item;
                control.Margin = new Thickness(0, 0, 10, 0);
                control.TextTrimming = TextTrimming.CharacterEllipsis;
                control.FontSize = UIFontSizes.Body;
                control.Cursor = Cursors.Hand;
                control.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F1F1F"));
                control.VerticalAlignment = VerticalAlignment.Bottom;
                control.MouseUp += (e, c) =>
                {
                    int index = Data.IndexOf(item);
                    if (SelectedIndex != index)
                    {
                        SelectedIndex = index;
                    }
                };
                if (Data.IndexOf(item) == Data.Count - 1)
                {
                    //  最后一项完成布局意味着整行标签的尺寸都已确定，此时才能定位标记块
                    control.Loaded += (e, c) =>
                    {
                        ScrollToActive();
                        Reset();
                    };
                }
                Grid.SetColumn(control, col);
                ItemsContainer.Children.Add(control);
                ItemsDictionary.Add(control);
            }
        }



        private void Render()
        {
            if (Data != null)
            {
                ItemsContainer.Children.Clear();
                ItemsContainer.ColumnDefinitions.Clear();
                ItemsDictionary.Clear();

                for (int i = 0; i < Data.Count; i++)
                {
                    var item = Data[i];

                    ItemsContainer.ColumnDefinitions.Add(new ColumnDefinition()
                    {
                        Width = new GridLength(1, GridUnitType.Star)
                    });

                    AddItem(item, i);

                }
                //foreach (var item in Data)
                //{

                //    AddItem(item);
                //}

            }
        }

        #region create animations
        private void CreateAnimations()
        {
            if (ActiveBlock == null)
            {
                return;
            }

            activeTranslate = new TranslateTransform(0, 0);
            ActiveBlock.RenderTransform = activeTranslate;
            isActiveBlockPlaced = false;
        }
        #endregion

        private void ScrollToActive(int oldSelectedIndex = 0)
        {
            if (activeTranslate == null
                || ItemsDictionary.Count == 0
                || !IsLoaded
                || SelectedIndex < 0
                || SelectedIndex >= ItemsDictionary.Count)
            {
                return;
            }

            //  获取选中项及其坐标
            var item = ItemsDictionary[SelectedIndex];
            Point relativePoint = item.TransformToAncestor(this).Transform(new Point(0, 0));
            double targetX = Math.Max(0, relativePoint.X);

            if (!isActiveBlockPlaced)
            {
                //  首次定位不播动画，直接就位
                isActiveBlockPlaced = true;
                activeTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                ActiveBlock.BeginAnimation(FrameworkElement.WidthProperty, null);
                activeTranslate.X = targetX;
                ActiveBlock.Width = item.ActualWidth;
            }
            else
            {
                Duration duration = Motion.ForDistance(Math.Abs(targetX - activeTranslate.X));

                // 宽度瞬时对齐，避免每帧触发布局计算
                ActiveBlock.BeginAnimation(FrameworkElement.WidthProperty, null);
                ActiveBlock.Width = item.ActualWidth;

                activeTranslate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
                {
                    From = activeTranslate.X,
                    To = targetX,
                    Duration = duration,
                    EasingFunction = Motion.SoftSpring
                }, HandoffBehavior.SnapshotAndReplace);
            }

            //  文字颜色过渡
            AnimateForeground(item, SelectedTextColor);
            if (oldSelectedIndex != SelectedIndex
                && oldSelectedIndex >= 0
                && oldSelectedIndex < ItemsDictionary.Count)
            {
                AnimateForeground(ItemsDictionary[oldSelectedIndex], UnselectedTextColor);
            }
        }

        private static void AnimateForeground(TextBlock text, Color to)
        {
            //  笔刷可能是共享或已冻结的实例，直接动画会影响其它控件甚至抛异常
            var brush = text.Foreground as SolidColorBrush;
            if (brush == null || brush.IsFrozen)
            {
                brush = new SolidColorBrush(brush == null ? to : brush.Color);
                text.Foreground = brush;
            }

            brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
            {
                To = to,
                Duration = Motion.Normal,
                EasingFunction = Motion.Standard
            }, HandoffBehavior.SnapshotAndReplace);
        }

        private void Reset()
        {
            for (int i = 0; i < ItemsDictionary.Count; i++)
            {
                if (i != SelectedIndex)
                {
                    //  每项独立持有笔刷，避免颜色动画互相串扰
                    ItemsDictionary[i].Foreground = new SolidColorBrush(UnselectedTextColor);
                }
            }
        }
    }
}
