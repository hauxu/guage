using Core.Librarys.SQLite;
using Core.Models;
using Core.Servicers.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Core.Servicers.Instances
{
    public class Categorys : ICategorys
    {
        private List<CategoryModel> _categories;
        private readonly object _locker = new object();
        private readonly IDatabase _database;

        public Categorys(IDatabase database)
        {
            _database = database;
            this._categories = new List<CategoryModel>();
        }

        public CategoryModel Create(CategoryModel category)
        {
            _database.ExecuteWrite(db =>
            {
                db.Categorys.Add(category);
                db.SaveChanges();
            });

            lock (_locker)
            {
                _categories.Add(category);
            }

            return category;
        }

        public void Delete(CategoryModel category)
        {
            _database.ExecuteWrite(db =>
            {
                var item = db.Categorys.FirstOrDefault(m => m.ID == category.ID);
                if (item != null)
                {
                    db.Categorys.Remove(item);
                    //  分类 ID 是 INTEGER PRIMARY KEY，删除后会被新建分类复用；
                    //  不把引用清掉，旧分类下的应用下次会静默归入新分类
                    db.Database.ExecuteSqlCommand("update AppModels set CategoryID = 0 where CategoryID = " + category.ID);
                    db.SaveChanges();
                }
            });

            lock (_locker)
            {
                var cached = _categories.FirstOrDefault(m => m.ID == category.ID);
                if (cached != null)
                {
                    _categories.Remove(cached);
                }
            }
        }

        public List<CategoryModel> GetCategories()
        {
            lock (_locker)
            {
                //  返回副本：调用方常在锁外 foreach，直接暴露本体会与后台写库线程
                //  的 Create/Delete 并发修改抛 InvalidOperationException
                return this._categories.ToList();
            }
        }

        public CategoryModel GetCategory(int id)
        {
            lock (_locker)
            {
                return _categories.FirstOrDefault(m => m.ID == id);
            }
        }

        public void Load()
        {
            Debug.WriteLine("加载分类");
            using (var db = _database.GetReaderContext())
            {
                var list = db.Categorys.ToList();
                lock (_locker)
                {
                    this._categories = list;
                }
                Debug.WriteLine("加载分类完成");
            }
        }

        public void Update(CategoryModel category)
        {
            _database.ExecuteWrite(db =>
            {
                db.Entry(category).State = System.Data.Entity.EntityState.Modified;
                db.SaveChanges();
            });
        }
    }
}
