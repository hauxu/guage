using Core.Librarys;
using Core.Models;
using Core.Servicers.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UI.Controls;
using UI.Models;
using UI.Models.Category;
using UI.Models.CategoryAppList;
using UI.Servicers;
using UI.Views;

namespace UI.ViewModels
{
    public class CategoryAppListPageVM : CategoryAppListPageModel
    {
        private readonly IAppData appData;
        private readonly MainViewModel mainVM;
        private readonly Servicers.UsageDataRefreshServicer _refreshServicer;
        private readonly IUIServicer _uiServicer;
        private IDisposable _autoRefreshSubscription;
        private List<ChooseAppModel> appList;
        public Command ShowChooseCommand { get; set; }
        public Command ChoosedCommand { get; set; }
        public Command GotoDetailCommand { get; set; }
        public Command SearchCommand { get; set; }
        public Command ChooseCloseCommand { get; set; }
        public Command DelCommand { get; set; }
        public CategoryAppListPageVM(IAppData appData, MainViewModel mainVM, Servicers.UsageDataRefreshServicer refreshServicer, IUIServicer uiServicer)
        {
            this.appData = appData;
            this.mainVM = mainVM;
            _refreshServicer = refreshServicer;
            _uiServicer = uiServicer;


            ShowChooseCommand = new Command(new Action<object>(OnShowChoose));
            ChoosedCommand = new Command(new Action<object>(OnChoosed));
            GotoDetailCommand = new Command(new Action<object>(OnGotoDetail));
            SearchCommand = new Command(new Action<object>(OnSearch));
            ChooseCloseCommand = new Command(new Action<object>(OnChooseClose));
            DelCommand = new Command(new Action<object>(OnDel));

            LoadData();

        }
        public override void Dispose()
        {
            StopAutoRefresh();
            base.Dispose();
            Data = null;
            AppList = null;
        }

        /// <summary>
        /// 页面可见时启动自动刷新（幂等，5 秒周期）。
        /// </summary>
        public void StartAutoRefresh()
        {
            if (_autoRefreshSubscription == null)
            {
                _autoRefreshSubscription = _refreshServicer.Subscribe(OnAutoRefresh);
            }
        }

        /// <summary>
        /// 页面离开时停止自动刷新。
        /// </summary>
        public void StopAutoRefresh()
        {
            _autoRefreshSubscription?.Dispose();
            _autoRefreshSubscription = null;
        }

        private void OnAutoRefresh()
        {
            LoadData();
        }
        private async void OnDel(object obj)
        {
            if (SelectedItem != null)
            {
                //  破坏性操作二次确认
                if (_uiServicer != null
                    && !await _uiServicer.ShowConfirmDialogAsync("移除确认", $"确定将「{SelectedItem.Name}」从分类中移除？"))
                {
                    return;
                }

                var list = Data.ToList();

                list.Remove(SelectedItem);

                var app = appData.GetApp(SelectedItem.ID);
                if (app != null)
                {
                    app.CategoryID = 0;
                    app.Category = null;
                    appData.UpdateApp(app);
                }

                Data = list;
            }
        }

        private void OnChooseClose(object obj)
        {
            ChooseVisibility = System.Windows.Visibility.Collapsed;
            SearchInput = "";
        }

        private void OnSearch(object obj)
        {
            string keyword = obj.ToString();

            if (keyword == "vscode")
            {
                keyword = "Visual Studio Code";
            }
            else if (keyword == "ps")
            {
                keyword = "Photoshop";
            }
            Search(keyword);
        }



        private void OnGotoDetail(object obj)
        {
            if (SelectedItem != null)
            {
                mainVM.Data = SelectedItem;
                mainVM.Uri = nameof(DetailPage);
            }
        }

        private void OnChoosed(object obj)
        {
            ChooseVisibility = System.Windows.Visibility.Collapsed;

            SearchInput = "";

            var data = new List<AppModel>();
            var toUpdate = new List<AppModel>();

            foreach (var item in AppList)
            {
                var app = appData.GetApp(item.App.ID);
                if (app == null)
                {
                    continue;
                }

                if (item.IsChoosed)
                {
                    //  处理选中
                    if (app.CategoryID != Category.Data.ID)
                    {
                        app.CategoryID = Category.Data.ID;
                        app.Category = Category.Data;
                        toUpdate.Add(app);
                    }

                    data.Add(app);
                }
                else
                {
                    //  处理取选
                    bool isHas = Data.Where(m => m.ID == item.App.ID).Any();
                    if (isHas)
                    {
                        app.CategoryID = 0;
                        app.Category = null;
                        toUpdate.Add(app);
                    }
                }
            }
            Data = data;

            //  DB 写入移到后台线程，避免应用较多时界面卡顿
            if (toUpdate.Count > 0)
            {
                var updateList = toUpdate.ToList();
                Task.Run(() =>
                {
                    try
                    {
                        foreach (var app in updateList)
                        {
                            appData.UpdateApp(app);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex.ToString());
                    }
                });
            }
        }

        private void OnShowChoose(object obj)
        {
            ChooseVisibility = System.Windows.Visibility.Visible;


            LoadApps();

        }


        private void LoadData()
        {
            Task.Run(() =>
            {
                //  后台线程只做取值，绑定属性赋值必须回到 UI 线程（消除跨线程绑定异常）
                var category = mainVM.Data as UI.Models.Category.CategoryModel;
                if (category?.Data == null)
                {
                    //  从详情页返回时 mainVM.Data 可能已是 AppModel，跳过本次刷新
                    return;
                }
                var data = appData.GetAppsByCategoryID(category.Data.ID);

                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    Category = category;
                    Data = data;
                }));
            });
        }

        private void LoadApps()
        {
            Task.Run(() =>
            {
                var list = new List<ChooseAppModel>();

                if (Category?.Data == null)
                {
                    return;
                }
                int categoryId = Category.Data.ID;
                foreach (var item in appData.GetAllApps())
                {
                    var app = new ChooseAppModel();
                    app.App = item;
                    app.IsChoosed = item.CategoryID == categoryId;
                    app.Value.Name = String.IsNullOrEmpty(item.Description) ? item.Name : item.Description;
                    app.Value.Img = item.IconFile;

                    if (app.IsChoosed || item.CategoryID == 0)
                    {
                        list.Add(app);
                    }
                }
                list = list.OrderBy(m => m.App.Description).ToList();

                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    appList = list;
                    AppList = list;
                }));
            });
        }

        private void Search(string keyword)
        {
            Task.Run(() =>
              {
                  if (!string.IsNullOrEmpty(keyword))
                  {
                      Debug.WriteLine(keyword);
                      keyword = keyword.ToLower();

                      //var list = appList.Where(m => m.App.Description.ToLower().IndexOf(keyword) != -1 || m.App.Name.ToLower().IndexOf(keyword) != -1).ToList();
                      var list = AppList.ToList();

                      foreach (var item in list)
                      {
                          item.Visibility = item.App.Description != null && item.App.Description.ToLower().IndexOf(keyword) != -1 || item.App.Name != null && item.App.Name.ToLower().IndexOf(keyword) != -1 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
                      }

                      return list;

                  }
                  else
                  {
                      var list = AppList.ToList();

                      foreach (var item in list)
                      {
                          item.Visibility = System.Windows.Visibility.Visible;
                      }

                      return list;

                  }
              })

      .ContinueWith(task =>
      {
          AppList = task.Result;

      }, TaskScheduler.FromCurrentSynchronizationContext());

        }
    }
}
