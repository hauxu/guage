using Core.Models;
using Core.Models.Db;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Librarys.SQLite
{
    public class GuageDbContext : DbContext
    {
        /// <summary>
        /// 每日数据
        /// </summary>
        public DbSet<DailyLogModel> DailyLog { get; set; }
        /// <summary>
        /// 时段数据
        /// </summary>
        public DbSet<HoursLogModel> HoursLog { get; set; }
        public DbSet<AppModel> App { get; set; }
        /// <summary>
        /// 分类
        /// </summary>
        public DbSet<CategoryModel> Categorys { get; set; }
        /// <summary>
        /// 网站
        /// </summary>
        public DbSet<WebSiteModel> WebSites { get; set; }
        /// <summary>
        /// 网站分类
        /// </summary>
        public DbSet<WebSiteCategoryModel> WebSiteCategories { get; set; }
        /// <summary>
        /// 网页浏览记录（每小时）
        /// </summary>
        public DbSet<WebBrowseLogModel> WebBrowserLogs { get; set; }
        /// <summary>
        /// 网页链接
        /// </summary>
        public DbSet<WebUrlModel> WebUrls { get; set; }

        private static string DbFilePath => FileHelper.GetDatabasePath();
        public GuageDbContext()
       : base(new SQLiteConnection()
       {
           ConnectionString = $"Data Source={DbFilePath}",
           //  写事务期间读请求的等待窗口（毫秒）：60ms 远小于一个写事务耗时，
           //  撞锁即抛 "database is locked" 并沿 VM 冒泡导致应用退出
           BusyTimeout = 15000
       }, true)
        {
            DbConfiguration.SetConfiguration(new SQLiteConfiguration());
        }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            var model = modelBuilder.Build(Database.Connection);
            if (!new SQLiteBuilder(model).SelfCheck())
            {
                //  迁移失败不能继续跑：表结构不完整的库会让之后每条查询静默失败，
                //  抛出去交给 Main 的恢复流程用 data.handle.backup 重建
                throw new InvalidOperationException("数据库结构迁移失败");
            }
        }

        public void SelfCheck()
        {
            Database.ExecuteSqlCommand("select count(*) from sqlite_master where type='table' and name='tai'");
        }
    }
}