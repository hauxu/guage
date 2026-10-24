using Core.Librarys;
using Core.Librarys.SQLite;
using Core.Models;
using Core.Servicers.Interfaces;
using System;
using System.Data.Entity;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Core.Servicers.Instances
{
    public class AppData : IAppData
    {
        private List<AppModel> _apps;

        private readonly IDatabase _database;
        private readonly object _locker = new object();
        public AppData(IDatabase databse)
        {
            _database = databse;
        }
        public List<AppModel> GetAllApps()
        {
            return _apps;
        }

        public void Load()
        {
            Debug.WriteLine("加载app开始");
            try
            {
                using (var db = _database.GetReaderContext())
                {
                    //  分两段查询 + 内存关联：
                    //  EF6 会把与模型必需外键关系一致的 join 条件“修正”为 INNER JOIN（忽略 DefaultIfEmpty），
                    //  导致 CategoryID=0 的未分类应用被全部过滤（列表为空）。
                    var apps = db.App.ToList();
                    var categories = db.Categorys.ToList();

                    _apps = apps.Select(a => new AppModel
                    {
                        ID = a.ID,
                        Category = categories.FirstOrDefault(c => c.ID == a.CategoryID),
                        CategoryID = a.CategoryID,
                        Description = a.Description,
                        File = a.File,
                        IconFile = a.IconFile,
                        Name = a.Name,
                        Alias = a.Alias,
                        TotalTime = a.TotalTime
                    }).ToList();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("AppData.Load 异常：" + ex);
                throw;
            }
        }

        /// <summary>
        /// 更新app数据，要先调用GetApp获得后更改并传回才有效
        /// </summary>
        /// <param name="app"></param>
        public void UpdateApp(AppModel app_)
        {
            try
            {
                _database.ExecuteWrite(db =>
                {
                    var app = db.App.FirstOrDefault(c => c.ID.Equals(app_.ID));
                    if (app != null)
                    {
                        app.TotalTime = app_.TotalTime;
                        app.IconFile = app_.IconFile;
                        app.Name = app_.Name;
                        app.Description = app_.Description;
                        app.File = app_.File;
                        app.CategoryID = app_.CategoryID;
                        app.Alias = app_.Alias;
                        db.SaveChanges();
                    }
                });
            }
            catch (Exception e)
            {
                Logger.Error(e.ToString());
            }
        }
        public AppModel GetApp(string name)
        {
            lock (_locker)
            {
                return _apps.Where(m => m.Name == name).FirstOrDefault();
            }
        }
        public AppModel GetApp(int id)
        {
            lock (_locker)
            {
                return _apps.Where(m => m.ID == id).FirstOrDefault();
            }
        }
        public void AddApp(AppModel app)
        {
            lock (_locker)
            {
                if (_apps.Where(m => m.Name == app.Name).Any())
                {
                    return;
                }
                try
                {
                    _database.ExecuteWrite(db =>
                    {
                        var r = db.App.Add(app);
                        int res = db.SaveChanges();
                        if (res > 0)
                        {
                            Debug.WriteLine("add done!" + r.ID);

                            _apps.Add(app);
                        }
                    });
                }
                catch (Exception e)
                {
                    Logger.Error(e.ToString());
                }
            }
        }


        public List<AppModel> GetAppsByCategoryID(int categoryID)
        {
            lock (_locker)
            {
                return _apps.Where(m => m.CategoryID == categoryID).ToList();
            }
        }

        public Dictionary<int, int> GetAppsCountByCategory()
        {
            lock (_locker)
            {
                return _apps
                    .GroupBy(m => m.CategoryID)
                    .ToDictionary(g => g.Key, g => g.Count());
            }
        }
    }
}
