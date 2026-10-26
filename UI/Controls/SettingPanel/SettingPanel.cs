using Core.Librarys;
using Core.Models.Config;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UI.Controls.Button;
using UI.Controls.Input;
using UI.Controls.List;
using UI.Controls.Select;
using UI.Base;

namespace UI.Controls.SettingPanel
{

    public class SettingPanel : Control
    {
        public struct Config
        {
            public ConfigAttribute Attribute { get; set; }
            public PropertyInfo PropertyInfo { get; set; }
        }
        public object Data { get { return (object)GetValue(DataProperty); } set { SetValue(DataProperty, value); } }
        public static readonly DependencyProperty DataProperty = DependencyProperty.Register("Data", typeof(object), typeof(SettingPanel), new PropertyMetadata(new PropertyChangedCallback(OnDataChanged)));

        public SolidColorBrush SpliteLineBrush { get { return (SolidColorBrush)GetValue(SpliteLineBrushProperty); } set { SetValue(SpliteLineBrushProperty, value); } }
        public static readonly DependencyProperty SpliteLineBrushProperty = DependencyProperty.Register("SpliteLineBrush", typeof(SolidColorBrush), typeof(SettingPanel));

        private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = d as SettingPanel;
            if (e.NewValue != e.OldValue)
            {
                control.Render();
            }
        }


        private StackPanel Container;
        private object configData;
        private readonly string nosetGroupKey = "noset_group";
        private bool isCanRender = true;
        private Dictionary<string, List<Config>> configList;
        public Dictionary<string, List<string>> SettingData { get; set; }

        /// <summary>
        /// 任一配置控件发生编辑（滑块/开关/下拉/文本等）。
        /// 与 Data 换绑不同：直写同引用后 DP 等值短路不再通知，编辑信号由此事件承载。
        /// </summary>
        public event EventHandler SettingEdited;

        /// <summary>
        /// 拖动/输入提交完成（松手或失焦）：重操作（如毛玻璃 accent 重提交）只应在此触发，
        /// 编辑过程中仅做轻量预览
        /// </summary>
        public event EventHandler SettingCommitted;

        private void RaiseSettingEdited()
        {
            SettingEdited?.Invoke(this, EventArgs.Empty);
        }

        private void RaiseSettingCommitted()
        {
            SettingCommitted?.Invoke(this, EventArgs.Empty);
        }

        public SettingPanel()
        {
            DefaultStyleKey = typeof(SettingPanel);
            SettingData = new Dictionary<string, List<string>>();
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            Container = GetTemplateChild("Container") as StackPanel;

            isCanRender = true;
            Render();
        }
        private void Render()
        {
            if (!isCanRender)
            {
                isCanRender = true;
                return;
            }
            configList = new Dictionary<string, List<Config>>();
            configList.Add(nosetGroupKey, new List<Config>());

            if (Container == null)
            {
                return;
            }

            Container.Children.Clear();

            if (Data == null)
            {
                return;
            }



            if (Data is IEnumerable)
            {
                //  多例
                RenderMultiPanel();
            }
            else
            {
                //  单例
                RenderSinglePanel();
            }


        }


        #region 渲染多例面板
        /// <summary>
        /// 渲染多例面板
        /// </summary>

        private void RenderMultiPanel()
        {
            var dataList = Data as IEnumerable<object>;
            var list = dataList.ToList();
            var cacheList = new Dictionary<int, object>();

            var dataType = Data.GetType();
            var itemType = dataType.GenericTypeArguments[0];
            var test1 = dataType.GetGenericArguments()[0];

            //  添加按钮
            var addBtn = new Button.Button();
            addBtn.Content = "新建项";
            addBtn.Width = 150;
            addBtn.Icon = Base.IconTypes.CalculatorAddition;
            addBtn.Click += (s, e) =>
            {
                int id = cacheList.Keys.Count > 0 ? cacheList.Keys.Max() + 1 : 1;

                //  每次新建独立实例，避免多个新建项共享同一对象导致联动修改
                var newItem = Activator.CreateInstance(itemType);
                cacheList.Add(id, newItem);

                var panel = CreateMultiPanel(newItem, id, cacheList);

                Container.Children.Insert(Container.Children.Count - 1, panel);

            };
            Container.Children.Add(addBtn);


            //  渲染数据
            for (int i = 0; i < list.Count; i++)
            {
                int id = cacheList.Keys.Count > 0 ? cacheList.Keys.Max() + 1 : 1;

                cacheList.Add(id, list[i]);
                var panel = CreateMultiPanel(list[i], id, cacheList);
                Container.Children.Insert(Container.Children.Count - 1, panel);
            }



        }


        private SettingPanelMultiItem CreateMultiPanel(object data, int id, Dictionary<int, object> cacheList)
        {
            var panel = new SettingPanelMultiItem();
            panel.Data = data;
            panel.Tag = id;
            panel.SettingData = SettingData;
            panel.DataChanged += (e, c) =>
            {
                int pid = (int)panel.Tag;

                if (cacheList.ContainsKey(pid))
                {
                    //  更新
                    cacheList[pid] = panel.Data;

                    var listType = typeof(List<>);
                    var constructedListType = listType.MakeGenericType(panel.Data.GetType());

                    var newList = (IList)Activator.CreateInstance(constructedListType);

                    foreach (var item in cacheList.Values)
                    {
                        newList.Add(item);
                    }

                    isCanRender = false;
                    Data = newList;
                }
            };
            panel.OnRemoveAction = new Command(new Action<object>((s) =>
              {
                  cacheList.Remove(id);
                  var listType = typeof(List<>);
                  var constructedListType = listType.MakeGenericType(panel.Data.GetType());

                  var newList = (IList)Activator.CreateInstance(constructedListType);
                  foreach (var item in cacheList.Values)
                  {
                      newList.Add(item);
                  }

                  Container.Children.Remove(panel);
                  isCanRender = false;
                  Data = newList;
              }));
            return panel;
        }
        #endregion

        /// <summary>
        /// 渲染单例面板
        /// </summary>
        private void RenderSinglePanel()
        {
            //  configData 直接引用 Data 原对象：控件直写即实时改写 config，
            //  不再"副本换绑"（同引用赋值会被 DP 等值短路导致后续零通知，
            //  且稀疏副本会重置 WindowWidth/Height/BrowserExtensionToken 等属性）
            configData = Data;

            // 遍历方法特性
            foreach (PropertyInfo pi in Data.GetType().GetProperties())
            {
                foreach (Attribute attr in pi.GetCustomAttributes(true))
                {
                    if (attr is ConfigAttribute)
                    {
                        ConfigAttribute attribute = (ConfigAttribute)attr;

                        var config = new Config()
                        {
                            Attribute = attribute,
                            PropertyInfo = pi
                        };
                        if (string.IsNullOrEmpty(attribute.Group))
                        {
                            //  未分组
                            configList[nosetGroupKey].Add(config);
                        }
                        else
                        {
                            if (!configList.ContainsKey(attribute.Group))
                            {
                                configList.Add(attribute.Group, new List<Config>());
                            }
                            configList[attribute.Group].Add(config);
                        }


                    }
                }
            }

            //  渲染已分组的项目
            foreach (var item in configList)
            {
                if (item.Key != nosetGroupKey)
                {
                    RenderGroup(item.Value, item.Key);
                }
            }

            //  渲染未分组项目
            RenderGroup(configList[nosetGroupKey]);
        }
        private void RenderGroup(List<Config> configList, string groupName = null)
        {
            if (configList == null || configList.Count == 0)
            {
                return;
            }
            var itemContainer = new StackPanel();

            var sortList = configList.OrderBy(m => m.Attribute.Index).ToList();
            for (int i = 0; i < sortList.Count; i++)
            {
                var config = sortList[i];

                var control = RenderConfigItem(config.Attribute, config.PropertyInfo);
                if (control != null)
                {
                    itemContainer.Children.Add(control);

                    if (i != sortList.Count - 1)
                    {
                        //  添加分割线
                        var spliteLine = new Border();
                        spliteLine.Height = 1;
                        spliteLine.Background = SpliteLineBrush;
                        itemContainer.Children.Add(spliteLine);
                    }
                }


            }

            var groupControl = GetCreateGroupContainer(itemContainer, groupName);
            Container.Children.Add(groupControl);
        }

        private StackPanel GetCreateGroupContainer(UIElement item, string groupName = null)
        {
            var container = new StackPanel();
            container.Margin = new Thickness(0, 10, 0, 20);
            if (groupName != null)
            {
                var groupNameControl = new TextBlock();
                groupNameControl.Text = groupName;
                groupNameControl.FontSize = UIFontSizes.Body;
                groupNameControl.Margin = new Thickness(0, 0, 0, 10);
                container.Children.Add(groupNameControl);
            }

            var border = new Border();
            border.Background = Background;
            border.CornerRadius = new CornerRadius(6);
            border.BorderBrush = BorderBrush;
            border.BorderThickness = new Thickness(1);
            border.Child = item;

            container.Children.Add(border);

            return container;
        }


        private UIElement RenderConfigItem(ConfigAttribute attribute, PropertyInfo pi)
        {
            UIElement uIElement = null;
            if (pi.PropertyType == typeof(bool))
            {
                uIElement = RenderBooleanConfigControl(attribute, pi);
            }
            else if (pi.PropertyType == typeof(List<string>))
            {
                uIElement = RenderListStringConfigControl(attribute, pi);
            }
            else if (pi.PropertyType == typeof(int))
            {
                uIElement = attribute.IsSlider
                    ? RenderSliderConfigControl(attribute, pi)
                    : RenderOptionsConfigControl(attribute, pi);
            }
            else if (pi.PropertyType == typeof(string))
            {
                if (attribute.IsPathPicker)
                {
                    uIElement = RenderPathConfigControl(attribute, pi);
                }
                else if (pi.Name.LastIndexOf("Color") != -1)
                {
                    uIElement = RenderColorConfigControl(attribute, pi);
                }
            }
            return uIElement;
        }

        /// <summary>
        /// 渲染文件夹路径选择配置项
        /// </summary>
        private UIElement RenderPathConfigControl(ConfigAttribute configAttribute, PropertyInfo pi)
        {
            var container = new StackPanel();
            container.Margin = new Thickness(15, 16, 15, 16);

            var title = new TextBlock();
            title.Text = configAttribute.Name;
            title.FontSize = UIFontSizes.Body;
            title.SetResourceReference(TextBlock.ForegroundProperty, "StandardTextBrush");
            container.Children.Add(title);

            if (!string.IsNullOrEmpty(configAttribute.Description))
            {
                var description = new TextBlock();
                description.Text = configAttribute.Description;
                description.FontSize = UIFontSizes.Secondary;
                description.Margin = new Thickness(0, 4, 0, 0);
                description.TextWrapping = TextWrapping.WrapWithOverflow;
                description.SetResourceReference(TextBlock.ForegroundProperty, "SubTextBrush");
                container.Children.Add(description);
            }

            string configuredPath = (string)pi.GetValue(Data) ?? string.Empty;
            var pathBox = new InputBox();
            pathBox.IsReadOnly = true;
            pathBox.Margin = new Thickness(0, 12, 0, 0);
            pathBox.Text = GetStoragePathDisplayText(configuredPath);
            pathBox.ToolTip = pathBox.Text;

            var actionRow = new StackPanel();
            actionRow.Margin = new Thickness(0, 10, 0, 0);
            actionRow.Orientation = Orientation.Horizontal;

            var browseButton = new Button.Button();
            browseButton.Content = "浏览...";
            browseButton.Width = 90;
            browseButton.Click += (s, e) =>
            {
                var dialog = new System.Windows.Forms.FolderBrowserDialog();
                dialog.Description = "选择数据存储位置";
                dialog.ShowNewFolderButton = true;
                if (!string.IsNullOrWhiteSpace(configuredPath) && Directory.Exists(configuredPath))
                {
                    dialog.SelectedPath = configuredPath;
                }
                else if (Directory.Exists(FileHelper.GetInstallDirectory()))
                {
                    dialog.SelectedPath = FileHelper.GetInstallDirectory();
                }

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    configuredPath = dialog.SelectedPath;
                    pathBox.Text = GetStoragePathDisplayText(configuredPath);
                    pathBox.ToolTip = pathBox.Text;
                    pi.SetValue(configData, configuredPath);
                    isCanRender = false;
                    Data = configData; RaiseSettingEdited();
                }
            };

            var resetButton = new Button.Button();
            resetButton.Content = "恢复默认";
            resetButton.Width = 90;
            resetButton.Margin = new Thickness(10, 0, 0, 0);
            resetButton.Click += (s, e) =>
            {
                configuredPath = string.Empty;
                pathBox.Text = GetStoragePathDisplayText(configuredPath);
                pathBox.ToolTip = pathBox.Text;
                pi.SetValue(configData, configuredPath);
                isCanRender = false;
                Data = configData; RaiseSettingEdited();
            };

            actionRow.Children.Add(browseButton);
            actionRow.Children.Add(resetButton);
            container.Children.Add(pathBox);
            container.Children.Add(actionRow);

            pi.SetValue(configData, pi.GetValue(Data));
            return container;
        }

        private static string GetStoragePathDisplayText(string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                return FileHelper.GetInstallDirectory() + "（默认）";
            }

            return configuredPath;
        }

        /// <summary>
        /// 渲染颜色选择配置项
        /// </summary>
        /// <param name="configAttribute"></param>
        /// <param name="pi"></param>
        /// <returns></returns>
        private UIElement RenderColorConfigControl(ConfigAttribute configAttribute, PropertyInfo pi)
        {
            var control = new Base.ColorSelect();
            control.Color = (string)pi.GetValue(Data);
            control.OnSelected += (e, c) =>
            {
                pi.SetValue(configData, control.Color);
                isCanRender = false;
                Data = configData; RaiseSettingEdited();
            };

            var item = new SettingPanelItem();
            item.Init(configAttribute, control);

            pi.SetValue(configData, pi.GetValue(Data));
            return item;
        }

        /// <summary>
        /// 渲染滑块配置项。滑块需要足够的拖动行程，因此独占一行而不是挤在右侧的控件列里。
        /// </summary>
        private UIElement RenderSliderConfigControl(ConfigAttribute configAttribute, PropertyInfo pi)
        {
            var control = new SliderInput();
            control.Minimum = configAttribute.Min;
            control.Maximum = configAttribute.Max;
            control.Unit = configAttribute.Unit;
            control.Value = (int)pi.GetValue(Data);
            control.Margin = new Thickness(0, 12, 0, 0);
            control.ValueChanged += (e, c) =>
            {
                pi.SetValue(configData, control.Value);

                isCanRender = false;
                Data = configData; RaiseSettingEdited();
            };
            control.DragCompleted += (e, c) => RaiseSettingCommitted();

            var container = new StackPanel();
            container.Margin = new Thickness(15, 16, 15, 16);

            var title = new TextBlock();
            title.Text = configAttribute.Name;
            title.FontSize = UIFontSizes.Body;
            title.SetResourceReference(TextBlock.ForegroundProperty, "StandardTextBrush");
            container.Children.Add(title);

            if (!string.IsNullOrEmpty(configAttribute.Description))
            {
                var description = new TextBlock();
                description.Text = configAttribute.Description;
                description.FontSize = UIFontSizes.Secondary;
                description.Margin = new Thickness(0, 4, 0, 0);
                description.TextWrapping = TextWrapping.WrapWithOverflow;
                description.SetResourceReference(TextBlock.ForegroundProperty, "SubTextBrush");
                container.Children.Add(description);
            }

            container.Children.Add(control);

            pi.SetValue(configData, pi.GetValue(Data));
            return container;
        }

        /// <summary>
        /// 渲染下拉选择配置项
        /// </summary>
        /// <param name="configAttribute"></param>
        /// <param name="pi"></param>
        /// <returns></returns>
        private UIElement RenderOptionsConfigControl(ConfigAttribute configAttribute, PropertyInfo pi)
        {
            var control = new Select.Select();
            var optionsArr = configAttribute.Options.Split('|');

            var options = new List<SelectItemModel>();
            for (int i = 0; i < optionsArr.Length; i++)
            {
                var name = optionsArr[i];
                options.Add(new SelectItemModel()
                {
                    Name = name,
                    Data = i
                });
            }
            control.Options = options;
            control.IsShowIcon = false;
            control.SelectedItem = options[(int)pi.GetValue(Data)];
            control.Tag = this;
            control.OnSelectedItemChanged += (e, c) =>
            {
                pi.SetValue(configData, control.SelectedItem.Data);

                isCanRender = false;
                Data = configData; RaiseSettingEdited();
            };
            //var inputControl = new Toggle.Toggle();
            //inputControl.ToggleChanged += (e, c) =>
            //{
            //    pi.SetValue(configData, inputControl.IsChecked);

            //    Data = configData; RaiseSettingEdited();
            //};

            //inputControl.IsChecked = (bool)pi.GetValue(Data);

            var item = new SettingPanelItem();
            item.Init(configAttribute, control);

            pi.SetValue(configData, pi.GetValue(Data));
            return item;
        }

        private UIElement RenderBooleanConfigControl(ConfigAttribute configAttribute, PropertyInfo pi)
        {
            var inputControl = new Toggle.Toggle();
            inputControl.ToggleChanged += (e, c) =>
            {
                pi.SetValue(configData, inputControl.IsChecked);

                isCanRender = false;
                Data = configData; RaiseSettingEdited();
            };

            inputControl.IsChecked = (bool)pi.GetValue(Data);

            var item = new SettingPanelItem();
            item.Init(configAttribute, inputControl);

            pi.SetValue(configData, pi.GetValue(Data));
            return item;
        }

        private UIElement RenderListStringConfigControl(ConfigAttribute configAttribute, PropertyInfo pi)
        {
            var list = pi.GetValue(Data) as List<string>;

            var listControl = new BaseList();
            listControl.MaxHeight = 200;
            listControl.Loaded += (sender, args) =>
            {
                listControl.Items.CollectionChanged += (o, e) =>
                {
                    var newData = new List<string>();
                    foreach (var item in listControl.Items)
                    {
                        newData.Add(item.ToString());
                    }
                    pi.SetValue(configData, newData);

                    isCanRender = false;
                    Data = configData; RaiseSettingEdited();
                };
            };
            listControl.Margin = new Thickness(15, 0, 15, 10);

            if (list != null)
            {
                foreach (string item in list)
                {
                    listControl.Items.Add(item);
                }
            }
            else
            {
                list = new List<string>();
            }
            var contextMenu = new ContextMenu();

            var contextMenuItemDel = new MenuItem();
            contextMenuItemDel.Header = "移除";
            contextMenuItemDel.Click += (e, c) =>
            {
                listControl.Items.Remove(listControl.SelectedItem);
            };
            var contextMenuItemCopy = new MenuItem();
            contextMenuItemCopy.Header = "复制内容";
            contextMenuItemCopy.Click += (e, c) =>
            {
                Clipboard.SetText(listControl.SelectedItem);
            };
            contextMenu.Items.Add(contextMenuItemCopy);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(contextMenuItemDel);
            listControl.ContextMenu = contextMenu;


            //  添加输入框
            var addInputBox = new InputBox();
            addInputBox.Placeholder = configAttribute.Placeholder;
            addInputBox.Margin = new Thickness(0, 0, 10, 0);


            //添加
            var addBtn = new Button.Button();
            //addBtn.Margin = new Thickness(15, 0, 15, 10);
            addBtn.Content = "添加";

            addBtn.Click += (e, c) =>
            {
                if (addInputBox.Text == String.Empty || list.Contains(addInputBox.Text))
                {
                    addInputBox.Error = configAttribute.Name + (addInputBox.Text == String.Empty ? "不能为空" : "已存在");
                    addInputBox.ShowError();
                    return;
                }
                //list.Add(addInputBox.Text);
                listControl.Items.Add(addInputBox.Text);
                addInputBox.Text = String.Empty;



            };
            pi.SetValue(configData, list);

            IconButton moreActionBtn = new IconButton();
            moreActionBtn.VerticalAlignment = VerticalAlignment.Center;
            moreActionBtn.HorizontalAlignment = HorizontalAlignment.Right;
            moreActionBtn.Margin = new Thickness(0, 5, 5, 0);
            moreActionBtn.Icon = Base.IconTypes.More;

            var moreActionMenu = new ContextMenu();

            moreActionBtn.MouseLeftButtonUp += (e, c) =>
            {
                moreActionMenu.IsOpen = true;
            };
            bool isHasMoreAction = false;
            if (configAttribute.IsCanImportExport)
            {
                //  允许导入导出
                isHasMoreAction = true;

                //  导入操作
                var importMenuItem = new MenuItem();
                importMenuItem.Header = "导入";
                importMenuItem.Click += (e, c) =>
                {
                    Microsoft.Win32.OpenFileDialog ofd = new Microsoft.Win32.OpenFileDialog();
                    ofd.Title = "选择文件";
                    ofd.Filter = "json(*.json)|*.json";
                    ofd.FileName = configAttribute.Name;

                    bool? result = ofd.ShowDialog();
                    if (result == true)
                    {
                        try
                        {
                            List<string> data = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(ofd.FileName));
                            if (data == null)
                            {
                                MessageBox.Show("文件格式有误或者数据为空，请选择有效的导出文件。");
                            }
                            else
                            {
                                if (MessageBox.Show("导入将覆盖现有配置，确定吗？", "注意", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
                                {
                                    pi.SetValue(configData, data);
                                    listControl.Items.Clear();
                                    foreach (string item in data)
                                    {
                                        listControl.Items.Add(item);
                                    }
                                    MessageBox.Show("导入完成！", "提示");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"导入配置“{configAttribute.Name}”时失败：{ex.Message}");
                            MessageBox.Show("导入失败！", "提示");
                        }
                    }

                };

                //  导出操作
                var exportMenuItem = new MenuItem();
                exportMenuItem.Header = "导出";
                exportMenuItem.Click += (e, c) =>
                {
                    try
                    {
                        Microsoft.Win32.SaveFileDialog sfd = new Microsoft.Win32.SaveFileDialog();
                        sfd.Title = "选择文件";
                        sfd.Filter = "json(*.json)|*.json";
                        sfd.FileName = configAttribute.Name + "导出配置";

                        bool? result = sfd.ShowDialog();
                        if (result == true)
                        {
                            File.WriteAllText(sfd.FileName, JsonConvert.SerializeObject(listControl.Items));
                            MessageBox.Show("导出完成", "提示");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"导出配置“{configAttribute.Name}”时失败：{ex.Message}");
                        MessageBox.Show("导出失败！", "提示");
                    }

                };


                moreActionMenu.Items.Add(importMenuItem);
                moreActionMenu.Items.Add(exportMenuItem);
            }


            var inputPanel = new Grid();
            inputPanel.ColumnDefinitions.Add(
                new ColumnDefinition()
                {
                    Width = new GridLength(10, GridUnitType.Star)
                });
            inputPanel.ColumnDefinitions.Add(
               new ColumnDefinition()
               {
                   Width = new GridLength(2, GridUnitType.Star)
               });

            inputPanel.Margin = new Thickness(15, 10, 15, 10);
            Grid.SetColumn(addInputBox, 0);
            Grid.SetColumn(addBtn, 1);

            inputPanel.Children.Add(addInputBox);
            inputPanel.Children.Add(addBtn);

            //  标题和说明

            var description = new TextBlock();
            description.Text = configAttribute.Description;
            description.Margin = new Thickness(10, 10, 10, 0);
            description.SetResourceReference(TextBlock.ForegroundProperty, "SubTextBrush");
            var container = new StackPanel();

            var head = new Grid();
            head.ColumnDefinitions.Add(
                 new ColumnDefinition()
                 {
                     Width = new GridLength(10, GridUnitType.Star)
                 });
            head.ColumnDefinitions.Add(
               new ColumnDefinition()
               {
                   Width = new GridLength(2, GridUnitType.Star)
               });
            Grid.SetColumn(description, 0);
            head.Children.Add(description);

            //  更多操作按钮
            if (isHasMoreAction)
            {
                Grid.SetColumn(moreActionBtn, 1);
                head.Children.Add(moreActionBtn);
            }

            container.Children.Add(head);
            container.Children.Add(inputPanel);
            container.Children.Add(listControl);
            return container;
        }
    }
}
