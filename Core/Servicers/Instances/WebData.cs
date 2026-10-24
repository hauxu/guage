using Core.Librarys;
using Core.Librarys.Browser;
using Core.Librarys.SQLite;
using Core.Models.Data;
using Core.Models.Db;
using Core.Models.WebPage;
using Core.Servicers.Interfaces;
using CsvHelper;
using Newtonsoft.Json;
using Npoi.Mapper;
using NPOI.SS.Formula.Functions;
using Org.BouncyCastle.Security;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;

using System.Threading.Tasks;
using static System.Windows.Forms.AxHost;

namespace Core.Servicers.Instances
{
    public class WebData : IWebData
    {
        private readonly IDatabase _database;
        private readonly object _createUrlLocker = new object();
        public WebData(IDatabase database)
        {
            _database = database;
        }

        #region AddUrlBrowseTime
        /// <summary>
        /// 单条消息的最大时长（秒）：一天，用于防御伪造消息造成的资源耗尽。
        /// </summary>
        private const int MaxDurationPerMessage = 86400;

        public void AddUrlBrowseTime(Site site_, int duration_, DateTime? dateTime_ = null, string msgId = null)
        {
            Debug.WriteLine("AddUrlBrowseTime");

            if (string.IsNullOrEmpty(site_?.Url))
            {
                return;
            }

            //  时长校验：0 秒是正常的亚秒级切页（静默忽略），超上限才是真异常
            if (duration_ == 0)
            {
                return;
            }
            if (duration_ < 0 || duration_ > MaxDurationPerMessage)
            {
                Logger.Error($"AddUrlBrowseTime 收到非法时长：{duration_}，URL：{site_.Url}");
                return;
            }

            //  断线重发幂等：已知 MsgId 直接跳过（登记在写库成功后进行，
            //  写库失败的消息不会被拒收，浏览器插件重发仍可入库）
            if (MsgIdDeduper.IsKnown(msgId))
            {
                Debug.WriteLine($"MsgId 已处理，跳过：{msgId}");
                return;
            }

            Task.Run(() =>
            {
                try
                {
                    var dateTime = dateTime_.HasValue ? dateTime_.Value : DateTime.Now;
                    DateTime cursor = new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, 0, 0);

                    //  59:59 临界：直接归入下一个小时桶（保持原语义）
                    if (dateTime.Minute == 59 && dateTime.Second == 59)
                    {
                        cursor = cursor.AddHours(1);
                    }

                    int remaining = duration_;
                    //  第一个小时桶按真实剩余时间计算（含秒级修正）
                    bool isFirstHour = cursor.Hour == dateTime.Hour;
                    //  迭代拆分保护：单条消息最多 86400 秒，正常不超过 25 个桶
                    int guard = 0;

                    _database.ExecuteWrite(db =>
                    {
                        //  获取站点信息
                        string domain = UrlHelper.GetDomain(site_.Url);
                        var site = db.WebSites.Where(m => m.Domain == domain).FirstOrDefault();
                        Debug.WriteLine("AddUrlBrowseTime 获取站点数据");

                        if (site == null)
                        {
                            site = db.WebSites.Add(new Models.Db.WebSiteModel()
                            {
                                Title = UrlHelper.GetName(site_.Url),
                                Domain = domain,
                            });

                            db.SaveChanges();
                        }

                        //  获取链接
                        var url = GetCreateUrl(db, site_);
                        if (url == null)
                        {
                            throw new Exception("在创建URL时异常");
                        }

                        while (remaining > 0 && guard++ < 32)
                        {
                            int hourRemaining;
                            if (isFirstHour)
                            {
                                hourRemaining = 3600 - (dateTime.Minute * 60 + dateTime.Second);
                                isFirstHour = false;
                            }
                            else
                            {
                                hourRemaining = 3600;
                            }

                            if (hourRemaining <= 0)
                            {
                                cursor = cursor.AddHours(1);
                                continue;
                            }

                            int add = remaining < hourRemaining ? remaining : hourRemaining;
                            int overflow = 0;

                            var log = db.WebBrowserLogs.FirstOrDefault(m => m.LogTime == cursor && m.UrlId == url.ID);
                            if (log != null)
                            {
                                //  同一小时桶累计不超过 3600 秒，超出部分推入下一小时
                                if (log.Duration + add > 3600)
                                {
                                    overflow = log.Duration + add - 3600;
                                    add = 3600 - log.Duration;
                                }

                                log.Duration += add;
                            }
                            else
                            {
                                log = new Models.Db.WebBrowseLogModel()
                                {
                                    Duration = add,
                                    UrlId = url.ID,
                                    LogTime = cursor,
                                    SiteId = site.ID,
                                };

                                db.WebBrowserLogs.Add(log);
                            }

                            site.Duration += add;
                            remaining -= add;
                            if (overflow > 0)
                            {
                                remaining += overflow;
                            }

                            cursor = cursor.AddHours(1);
                        }

                        //  MsgId 与业务数据同一事务持久化（跨重启防重放）
                        if (!string.IsNullOrEmpty(msgId))
                        {
                            EnsureMsgIdSeenTable(db);
                            db.Database.ExecuteSqlCommand(
                                "INSERT OR REPLACE INTO MsgIdSeens ([MsgId], [SeenAt]) VALUES (@p0, @p1)",
                                msgId, DateTime.UtcNow.Ticks);
                        }

                        db.SaveChanges();
                    });

                    //  写库成功才登记进内存去重表
                    MsgIdDeduper.Register(msgId);
                }
                catch (Exception ex)
                {
                    Logger.Error($"在更新链接[{site_.Url}]时长[{duration_}]时异常，{ex}");
                }
            });
        }

        //  MsgIdSeen 表只需创建一次（ExecuteWrite 串行执行，无并发 DDL）
        private static bool _msgIdSeenTableChecked;

        /// <summary>
        /// 确保 MsgId 持久化表存在（IF NOT EXISTS，随业务写同事务执行，规避 schema 版本门槛）
        /// </summary>
        private static void EnsureMsgIdSeenTable(GuageDbContext db)
        {
            if (_msgIdSeenTableChecked)
            {
                return;
            }
            db.Database.ExecuteSqlCommand(
                "CREATE TABLE IF NOT EXISTS MsgIdSeens ([MsgId] nvarchar PRIMARY KEY, [SeenAt] integer)");
            _msgIdSeenTableChecked = true;
        }

        /// <summary>
        /// 启动时预加载最近 7 天已处理的 MsgId（跨重启防重放），并清理过期记录
        /// </summary>
        public void PreloadMsgIdDedup()
        {
            try
            {
                _database.ExecuteWrite(db =>
                {
                    EnsureMsgIdSeenTable(db);
                    db.Database.ExecuteSqlCommand(
                        "DELETE FROM MsgIdSeens WHERE [SeenAt] < @p0",
                        DateTime.UtcNow.AddDays(-7).Ticks);
                    var ids = db.Database
                        .SqlQuery<string>("SELECT [MsgId] FROM MsgIdSeens ORDER BY [SeenAt] DESC LIMIT 8192")
                        .ToList();
                    MsgIdDeduper.Load(ids);
                });
            }
            catch (Exception ex)
            {
                Logger.Error("预加载 MsgId 去重表失败：" + ex.Message);
            }
        }
        #endregion

        #region UpdateUrlFavicon
        public void UpdateUrlFavicon(Site site_, string iconFile_)
        {
            if (string.IsNullOrEmpty(iconFile_) || string.IsNullOrEmpty(site_.Url))
            {
                return;
            }

            Debug.WriteLine("UpdateUrlFavicon");

            Task.Run(() =>
            {
                try
                {
                    //  判断是否是主域名
                    bool isIndex = UrlHelper.IsIndexUrl(site_.Url);

                    _database.ExecuteWrite(db =>
                    {
                        //  更新站点图标
                        var site = GetCreateWebSite(db, site_.Url);
                        Debug.WriteLine("UpdateUrlFavicon 获取站点数据");
                        if (site != null && (string.IsNullOrEmpty(site.IconFile) || (isIndex && site.IconFile != iconFile_)))
                        {
                            site.IconFile = iconFile_;
                        }

                        //  更新链接图标
                        var url = GetCreateUrl(db, site_);
                        if (url != null)
                        {
                            url.IconFile = iconFile_;
                        }

                        db.SaveChanges();
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error($"UpdateUrlFavicon 异常：{ex}");
                }
            });

        }
        #endregion

        #region FillMissingFavicons
        public void FillMissingFavicons()
        {
            Task.Run(() =>
            {
                try
                {
                    List<string> domains;
                    using (var db = _database.GetReaderContext())
                    {
                        domains = db.WebSites
                            .Where(m => (m.IconFile == null || m.IconFile == "") && m.Domain != null)
                            .Select(m => m.Domain)
                            .ToList();
                    }

                    foreach (string domain in domains)
                    {
                        string iconFile = FaviconFetcher.Fetch(domain);
                        if (string.IsNullOrEmpty(iconFile))
                        {
                            continue;
                        }

                        _database.ExecuteWrite(db =>
                        {
                            var site = db.WebSites.FirstOrDefault(m => m.Domain == domain);
                            if (site != null && string.IsNullOrEmpty(site.IconFile))
                            {
                                site.IconFile = iconFile;
                                db.SaveChanges();
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"补齐网站图标时异常，{ex}");
                }
            });
        }
        #endregion

        #region GetCreateSite
        /// <summary>
        /// 输入URL获取站点信息,不存在时创建
        /// </summary>
        /// <param name="url_">URL</param>
        /// <returns></returns>
        private WebSiteModel GetCreateWebSite(GuageDbContext db, string url_)
        {
            //  获取主域名
            string domain = UrlHelper.GetDomain(url_);

            //using (var db = _database.GetWriterContext())
            //{
            var site = db.WebSites.Where(m => m.Domain == domain).FirstOrDefault();
            if (site == null)
            {
                site = db.WebSites.Add(new WebSiteModel()
                {
                    Title = UrlHelper.GetName(url_),
                    Domain = domain,
                });
                db.SaveChanges();
            }

            return site;
            //}
        }
        #endregion
        #region GetCreateUrl
        /// <summary>
        /// 获取url数据,不存在时创建
        /// </summary>
        /// <param name="url_"></param>
        /// <returns></returns>
        private Models.Db.WebUrlModel GetCreateUrl(GuageDbContext db, Site site_)
        {
            lock (_createUrlLocker)
            {
                var result = db.WebUrls.Where(m => m.Url == site_.Url).FirstOrDefault();
                if (result == null)
                {
                    result = db.WebUrls.Add(new Models.Db.WebUrlModel()
                    {
                        Url = site_.Url,
                        Title = site_.Title,
                    });
                    db.SaveChanges();
                }
                else
                {
                    if (result.Title != site_.Title)
                    {
                        result.Title = site_.Title;
                        db.SaveChanges();
                    }
                }
                return result;

            }
        }
        #endregion

        #region GetDateRangeWebSiteList
        public List<WebSiteModel> GetDateRangeWebSiteList(DateTime start, DateTime end, int take = 0, int skip = -1, bool isTime_ = false)
        {
            if (isTime_)
            {
                start = new DateTime(start.Year, start.Month, start.Day, start.Hour, 0, 0);
                end = new DateTime(end.Year, end.Month, end.Day, end.Hour, 59, 59);
            }
            else
            {
                start = new DateTime(start.Year, start.Month, start.Day, 0, 0, 0);
                end = new DateTime(end.Year, end.Month, end.Day, 23, 59, 59);
            }
            using (var db = _database.GetReaderContext())
            {
                var data = db.WebBrowserLogs
              .Where(m => m.LogTime >= start && m.LogTime <= end && m.SiteId != 0)
              .GroupBy(m => m.SiteId)
              .Select(s => new
              {
                  //Title = s.FirstOrDefault().Site.Title,
                  //Domain = s.FirstOrDefault().Site.Domain,
                  //CategoryID = s.FirstOrDefault().Site.CategoryID,
                  //IconFile = s.FirstOrDefault().Site.IconFile,
                  //ID = s.FirstOrDefault().Site.ID,
                  Site = s.FirstOrDefault().Site,
                  Duration = s.Sum(m => m.Duration),
              });

                if (skip > 0 && take > 0)
                {
                    data = data.OrderByDescending(m => m.Duration).Skip(skip).Take(take);
                }
                else if (skip > 0)
                {
                    data = data.OrderByDescending(m => m.Duration).Skip(skip);
                }
                else if (take > 0)
                {
                    data = data.OrderByDescending(m => m.Duration).Take(take);
                }
                else
                {
                    data = data.OrderByDescending(m => m.Duration);
                }
                var list = data.Select(s => new
                {
                    Alias = s.Site.Alias,
                    Title = s.Site.Title,
                    Domain = s.Site.Domain,
                    CategoryID = s.Site.CategoryID,
                    IconFile = s.Site.IconFile,
                    ID = s.Site.ID,
                    Duration = s.Duration,
                }).ToList();

                var result = JsonConvert.DeserializeObject<List<WebSiteModel>>(JsonConvert.SerializeObject(list));
                return result;
            }
        }


        #endregion

        #region GetWebSiteCategories
        public List<WebSiteCategoryModel> GetWebSiteCategories()
        {
            using (var db = _database.GetReaderContext())
            {
                var result = db.WebSiteCategories.ToList();
                return result;
            }
        }


        #endregion

        #region CreateWebSiteCategory
        public WebSiteCategoryModel CreateWebSiteCategory(WebSiteCategoryModel data_)
        {
            _database.ExecuteWrite(db =>
            {
                db.WebSiteCategories.Add(data_);
                db.SaveChanges();
            });

            return data_;
        }
        #endregion

        #region UpdateWebSiteCategory
        public void UpdateWebSiteCategory(WebSiteCategoryModel data_)
        {
            _database.ExecuteWrite(db =>
            {
                db.Entry(data_).State = System.Data.Entity.EntityState.Modified;
                db.SaveChanges();
            });
        }
        #endregion

        #region DeleteWebSiteCategory
        public void DeleteWebSiteCategory(WebSiteCategoryModel data_)
        {
            Task.Run(() =>
            {
                try
                {
                    _database.ExecuteWrite(db =>
                    {
                        //  取消网站关联分类
                        db.Database.ExecuteSqlCommand("update WebSiteModels set CategoryID=0 where CategoryID=" + data_.ID);

                        //  删除分类
                        db.Entry(data_).State = System.Data.Entity.EntityState.Deleted;
                        db.SaveChanges();
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error($"DeleteWebSiteCategory 异常：{ex}");
                }
            });
        }
        #endregion
        #region GetWebSites
        public List<WebSiteModel> GetWebSites(int categoryId_)
        {
            using (var db = _database.GetReaderContext())
            {
                var result = db.WebSites.Where(m => m.CategoryID == categoryId_).ToList();
                return result;
            }
        }
        #endregion
        #region GetWebSitesCount
        public int GetWebSitesCount(int categoryId_)
        {
            using (var db = _database.GetReaderContext())
            {
                var result = db.WebSites.Where(m => m.CategoryID == categoryId_).Count();
                return result;
            }
        }

        public Dictionary<int, int> GetWebSitesCountByCategory()
        {
            var result = new Dictionary<int, int>();
            try
            {
                using (var db = _database.GetReaderContext())
                {
                    result = db.WebSites
                        .GroupBy(m => m.CategoryID)
                        .Select(g => new { CategoryID = g.Key, Count = g.Count() })
                        .ToDictionary(m => m.CategoryID, m => m.Count);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("GetWebSitesCountByCategory 异常：" + ex.Message);
            }

            return result;
        }



        //public List<WebSiteModel> GetWebSites(Expression<Func<WebSiteModel, bool>> predicate_)
        //{
        //    using (var db = new GuageDbContext())
        //    {
        //        return db.WebSites.Where(predicate_).ToList();
        //    }
        //}
        #endregion
        #region GetUnSetCategoryWebSites
        public List<WebSiteModel> GetUnSetCategoryWebSites()
        {
            using (var db = _database.GetReaderContext())
            {
                var result = db.WebSites.Where(m => m.CategoryID == 0).ToList();
                return result;
            }
        }


        #endregion

        public void UpdateWebSitesCategory(int[] siteIds_, int categoryId_)
        {
            Task.Run(() =>
            {
                try
                {
                    _database.ExecuteWrite(db =>
                    {
                        string sql = $"update WebSiteModels set CategoryID={categoryId_} where ID in ({string.Join(",", siteIds_)})";
                        db.Database.ExecuteSqlCommand(sql);
                        db.SaveChanges();
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error($"UpdateWebSitesCategory 异常：{ex}");
                }
            });
        }

        public List<CommonDataModel> GetCategoriesStatistics(DateTime start_, DateTime end_)
        {
            using (var db = _database.GetReaderContext())
            {
                var sql = "select sum(WebBrowseLogModels.Duration) as Value,WebSiteCategoryModels.ID as ID,WebSiteCategoryModels.Name as Name from WebBrowseLogModels join WebSiteModels on WebBrowseLogModels.SiteId=WebSiteModels.ID JOIN WebSiteCategoryModels on WebSiteModels.CategoryID=WebSiteCategoryModels.ID WHERE WebBrowseLogModels.LogTime>='" + start_.ToString("yyyy-MM-dd 00:00:00", CultureInfo.InvariantCulture) + "' and WebBrowseLogModels.LogTime<= '" + end_.ToString("yyyy-MM-dd 23:59:59", CultureInfo.InvariantCulture) + "' GROUP BY WebSiteCategoryModels.ID";

                var data = db.Database.SqlQuery<CommonDataModel>(sql).ToList();
                //  未分类

                sql = "select sum(WebBrowseLogModels.Duration) as Value,WebSiteModels.CategoryID as ID,WebSiteModels.Title as Name from WebBrowseLogModels left join WebSiteModels on WebBrowseLogModels.SiteId=WebSiteModels.ID WHERE WebSiteModels.CategoryID=0 AND WebBrowseLogModels.LogTime>='" + start_.ToString("yyyy-MM-dd 00:00:00", CultureInfo.InvariantCulture) + "' and WebBrowseLogModels.LogTime<= '" + end_.ToString("yyyy-MM-dd 23:59:59", CultureInfo.InvariantCulture) + "' Group BY WebSiteModels.CategoryID";
                var noCategoryData = db.Database.SqlQuery<CommonDataModel>(sql).ToList();
                var result = data.Concat(noCategoryData).ToList();
                return result;
            }
        }

        public WebSiteCategoryModel GetWebSiteCategory(int categoryId_)
        {
            using (var db = _database.GetReaderContext())
            {
                var result = db.WebSiteCategories.Where(m => m.ID == categoryId_).FirstOrDefault();
                return result;
            }
        }

        public List<CommonDataModel> GetBrowseDataStatistics(DateTime start_, DateTime end_, int siteId_ = 0)
        {
            using (var db = _database.GetReaderContext())
            {
                var start = new DateTime(start_.Year, start_.Month, start_.Day, 0, 0, 0);
                var end = new DateTime(end_.Year, end_.Month, end_.Day, 23, 59, 59);
                var dbResult = db.WebBrowserLogs.Where(m => m.LogTime >= start && m.LogTime <= end);
                if (siteId_ > 0)
                {
                    dbResult = dbResult.Where(m => m.SiteId == siteId_);
                }

                var data = dbResult.GroupBy(m => m.LogTime)
                .Select(m => new
                {
                    Value = m.Sum(s => s.Duration),
                    Time = m.FirstOrDefault().LogTime
                }).ToList();

                var result = new List<CommonDataModel>();

                if (start_ == end_)
                {

                    //  获取24小时数据
                    for (int i = 0; i < 24; i++)
                    {
                        var time = new DateTime(start_.Year, start_.Month, start_.Day, i, 0, 0);
                        var item = data.Where(m => m.Time == time).FirstOrDefault();
                        var dataItem = new CommonDataModel()
                        {
                            ID = i,
                            Name = i.ToString(),
                            Value = item != null ? item.Value : 0,
                        };
                        result.Add(dataItem);
                    }
                }
                else
                {
                    //  获取日期范围数据
                    var days = (end_ - start_).TotalDays + 1;
                    if (days <= 31)
                    {
                        //  按天
                        for (int i = 0; i < days; i++)
                        {
                            var date = start_.AddDays(i);
                            var startTime = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0);
                            var endTime = new DateTime(date.Year, date.Month, date.Day, 23, 59, 59);

                            var value = data.Where(m => m.Time >= startTime && m.Time <= endTime).Sum(m => m.Value);
                            var dataItem = new CommonDataModel()
                            {
                                ID = i,
                                Name = i.ToString(),
                                Value = value,
                            };
                            result.Add(dataItem);
                        }
                    }
                    else
                    {
                        //  按月
                        for (int i = 0; i < 12; i++)
                        {
                            var date = new DateTime(start_.Year, i + 1, 1, 0, 0, 0);
                            var startTime = new DateTime(date.Year, date.Month, 1, 0, 0, 0);
                            var endTime = new DateTime(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month), 23, 59, 59);

                            var value = data.Where(m => m.Time >= startTime && m.Time <= endTime).Sum(m => m.Value);
                            var dataItem = new CommonDataModel()
                            {
                                ID = i,
                                Name = i.ToString(),
                                Value = value,
                            };
                            result.Add(dataItem);
                        }
                    }
                }
                return result;
            }
        }

        private class CategoryStatisticModel
        {
            public int Duration { get; set; }
            public DateTime LogTime { get; set; }
            public int CategoryID { get; set; }
        }
        public List<ColumnDataModel> GetBrowseDataByCategoryStatistics(DateTime start_, DateTime end_)
        {
            using (var db = _database.GetReaderContext())
            {


                var start = new DateTime(start_.Year, start_.Month, start_.Day, 0, 0, 0);
                var end = new DateTime(end_.Year, end_.Month, end_.Day, 23, 59, 59);

                //  查询分类
                var categorySql = "select sum(WebBrowseLogModels.Duration) as Duration,LogTime,WebSiteModels.CategoryID as CategoryID from WebBrowseLogModels LEFT JOIN WebSiteModels on WebBrowseLogModels.SiteId=WebSiteModels.ID LEFT JOIN WebSiteCategoryModels on WebSiteModels.CategoryID=WebSiteCategoryModels.ID  WHERE LogTime>='" + start.ToString("yyyy-MM-dd 00:00:00", CultureInfo.InvariantCulture) + "' and LogTime<= '" + end.ToString("yyyy-MM-dd 23:59:59", CultureInfo.InvariantCulture) + "' GROUP BY WebSiteModels.CategoryID";

                var categories = db.Database.SqlQuery<CategoryStatisticModel>(categorySql).ToList();

                //  查询数据
                var dataSql = "select sum(WebBrowseLogModels.Duration) as Duration,LogTime,WebSiteModels.CategoryID as CategoryID from WebBrowseLogModels LEFT JOIN WebSiteModels on WebBrowseLogModels.SiteId=WebSiteModels.ID LEFT JOIN WebSiteCategoryModels on WebSiteModels.CategoryID=WebSiteCategoryModels.ID  WHERE LogTime>='" + start.ToString("yyyy-MM-dd 00:00:00", CultureInfo.InvariantCulture) + "' and LogTime<= '" + end.ToString("yyyy-MM-dd 23:59:59", CultureInfo.InvariantCulture) + "' GROUP BY WebBrowseLogModels.LogTime,WebSiteModels.CategoryID";

                var data = db.Database.SqlQuery<CategoryStatisticModel>(dataSql).ToList();
                var result = new List<ColumnDataModel>();

                if (start_ == end_)
                {
                    //  获取24小时数据
                    foreach (var category in categories)
                    {
                        result.Add(new ColumnDataModel()
                        {
                            CategoryID = category.CategoryID,
                            Values = new double[24]
                        });
                    }
                    for (int i = 0; i < 24; i++)
                    {
                        var time = new DateTime(start_.Year, start_.Month, start_.Day, i, 0, 0);
                        foreach (var category in categories)
                        {
                            var log = data.Where(m => m.CategoryID == category.CategoryID && m.LogTime == time).FirstOrDefault();
                            if (log != null)
                            {
                                var resultItem = result.Where(m => m.CategoryID == category.CategoryID).FirstOrDefault();
                                resultItem.Values[i] = log.Duration;
                            }
                        }
                    }
                }
                else
                {
                    //  获取日期范围数据
                    int days = (int)(end_ - start_).TotalDays + 1;
                    if (days <= 31)
                    {
                        //  按天
                        foreach (var category in categories)
                        {
                            result.Add(new ColumnDataModel()
                            {
                                CategoryID = category.CategoryID,
                                Values = new double[days]
                            });
                        }

                        for (int i = 0; i < days; i++)
                        {
                            var date = start_.AddDays(i);
                            var startTime = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0);
                            var endTime = new DateTime(date.Year, date.Month, date.Day, 23, 59, 59);

                            foreach (var category in categories)
                            {
                                var duration = data.Where(m => m.CategoryID == category.CategoryID && m.LogTime >= startTime && m.LogTime <= endTime).Sum(m => m.Duration);

                                var resultItem = result.Where(m => m.CategoryID == category.CategoryID).FirstOrDefault();
                                resultItem.Values[i] = duration;
                            }
                        }
                    }
                    else
                    {
                        //  按月
                        foreach (var category in categories)
                        {
                            result.Add(new ColumnDataModel()
                            {
                                CategoryID = category.CategoryID,
                                Values = new double[12]
                            });
                        }
                        for (int i = 0; i < 12; i++)
                        {
                            var date = new DateTime(start_.Year, i + 1, 1, 0, 0, 0);
                            var startTime = new DateTime(date.Year, date.Month, 1, 0, 0, 0);
                            var endTime = new DateTime(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month), 23, 59, 59);

                            foreach (var category in categories)
                            {
                                var duration = data.Where(m => m.CategoryID == category.CategoryID && m.LogTime >= startTime && m.LogTime <= endTime).Sum(m => m.Duration);

                                var resultItem = result.Where(m => m.CategoryID == category.CategoryID).FirstOrDefault();
                                resultItem.Values[i] = duration;
                            }
                        }
                    }
                }
                return result;
            }
        }

        public int GetBrowseDurationTotal(DateTime start_, DateTime end_)
        {
            start_ = new DateTime(start_.Year, start_.Month, start_.Day, 0, 0, 0);
            end_ = new DateTime(end_.Year, end_.Month, end_.Day, 23, 59, 59);

            using (var db = _database.GetReaderContext())
            {
                var data = db.WebBrowserLogs.Where(m => m.LogTime >= start_ && m.LogTime <= end_);
                var result = data.Any() ? data.Sum(m => m.Duration) : 0;
                return result;
            }
        }

        public int GetBrowseSitesTotal(DateTime start_, DateTime end_)
        {
            start_ = new DateTime(start_.Year, start_.Month, start_.Day, 0, 0, 0);
            end_ = new DateTime(end_.Year, end_.Month, end_.Day, 23, 59, 59);
            using (var db = _database.GetReaderContext())
            {
                var result = db.WebBrowserLogs.Where(m => m.LogTime >= start_ && m.LogTime <= end_).GroupBy(m => m.SiteId).Count();
                return result;
            }
        }

        public int GetBrowsePagesTotal(DateTime start_, DateTime end_)
        {
            start_ = new DateTime(start_.Year, start_.Month, start_.Day, 0, 0, 0);
            end_ = new DateTime(end_.Year, end_.Month, end_.Day, 23, 59, 59);
            using (var db = _database.GetReaderContext())
            {
                var result = db.WebBrowserLogs.Where(m => m.LogTime >= start_ && m.LogTime <= end_).GroupBy(m => m.UrlId).Count();
                return result;
            }
        }

        public List<WebBrowseLogModel> GetBrowseLogList(DateTime start_, DateTime end_, int siteId_ = 0)
        {
            start_ = new DateTime(start_.Year, start_.Month, start_.Day, 0, 0, 0);
            end_ = new DateTime(end_.Year, end_.Month, end_.Day, 23, 59, 59);
            using (var db = _database.GetReaderContext())
            {
                var query = (from log in db.WebBrowserLogs
                             join url in db.WebUrls on log.UrlId equals url.ID
                             join site in db.WebSites on log.SiteId equals site.ID
                             where log.LogTime >= start_ && log.LogTime <= end_ && log.SiteId == siteId_
                             orderby log.LogTime descending
                             select new
                             {
                                 Duration = log.Duration,
                                 ID = log.ID,
                                 LogTime = log.LogTime,
                                 Site = site,
                                 SiteId = log.SiteId,
                                 Url = url,
                                 UrlId = log.UrlId,
                             }).ToList().Select(s => new WebBrowseLogModel
                             {
                                 Duration = s.Duration,
                                 ID = s.ID,
                                 LogTime = s.LogTime,
                                 Site = s.Site,
                                 SiteId = s.SiteId,
                                 Url = s.Url,
                                 UrlId = s.UrlId,
                             });

                var result = query.ToList();
                return result;
            }
        }

        public WebSiteModel GetWebSite(int id_)
        {
            using (var db = _database.GetReaderContext())
            {
                var result = db.WebSites.Where(m => m.ID == id_).FirstOrDefault();
                return result;
            }
        }

        public WebSiteModel GetWebSite(string domain)
        {
            using (var db = _database.GetReaderContext())
            {
                var result = db.WebSites.Where(m => m.Domain.ToLower() == domain.ToLower()).FirstOrDefault();
                return result;
            }
        }

        public void Clear(DateTime start_, DateTime end_)
        {
            end_ = new DateTime(end_.Year, end_.Month, DateTime.DaysInMonth(end_.Year, end_.Month));

            _database.ExecuteWrite(db =>
            {
                db.Database.ExecuteSqlCommand("delete from WebBrowseLogModels  where LogTime>='" + start_.Date.ToString("yyyy-MM-01 00:00:00", CultureInfo.InvariantCulture) + "' and LogTime<= '" + end_.Date.ToString("yyyy-MM-dd 23:59:59", CultureInfo.InvariantCulture) + "'");

                //  按剩余日志重算站点总时长：与图表保持一致（单站点 Clear 是整删可直清零）
                db.Database.ExecuteSqlCommand("update WebSiteModels set Duration=(select coalesce(sum(Duration),0) from WebBrowseLogModels where WebBrowseLogModels.SiteId=WebSiteModels.ID)");
            });
        }

        public List<WebSiteModel> GetWebSiteLogList(DateTime start_, DateTime end_)
        {
            start_ = new DateTime(start_.Year, start_.Month, start_.Day, 0, 0, 0);
            end_ = new DateTime(end_.Year, end_.Month, end_.Day, 23, 59, 59);
            using (var db = _database.GetReaderContext())
            {
                var query = (from log in db.WebBrowserLogs
                             where log.LogTime >= start_ && log.LogTime <= end_
                             join site in db.WebSites on log.SiteId equals site.ID into newSite
                             join category in db.WebSiteCategories on log.Site.CategoryID equals category.ID into newCategory
                             from nc in newCategory.DefaultIfEmpty()
                             select new
                             {
                                 ID = log.ID,
                                 UrlId = log.UrlId,
                                 Duration = log.Duration,
                                 SiteId = log.SiteId,
                                 Site = log.Site,
                                 Category = nc
                             })
                             .ToList();
                var result = query.GroupBy(m => m.SiteId).Select(s => new WebSiteModel
                {
                    Duration = s.Sum(m => m.Duration),
                    ID = s.FirstOrDefault().Site.ID,
                    Title = s.FirstOrDefault().Site.Title,
                    Domain = s.FirstOrDefault().Site.Domain,
                    IconFile = s.FirstOrDefault().Site.IconFile,
                    CategoryID = s.FirstOrDefault().Site.CategoryID,
                    Category = s.FirstOrDefault().Category,
                    Alias = s.FirstOrDefault().Site.Alias,
                }).ToList();

                //var result = query.Select(s => new WebSiteModel
                //{
                //    Duration = s.Duration,
                //    ID = s.Site.ID,
                //    Title = s.Site.Title,
                //    Domain = s.Site.Domain,
                //    IconFile = s.Site.IconFile,
                //    //Category = s.Category,
                //    CategoryID = s.Site.CategoryID,
                //}).ToList();
                //return result;
                //return query;
                return result;
            }
        }

        public void Clear(int siteId_)
        {
            //  写操作必须走 ExecuteWrite（单写者串行），读上下文执行 DELETE 会绕过写锁
            _database.ExecuteWrite(db =>
            {
                db.Database.ExecuteSqlCommand("delete from WebBrowseLogModels  where SiteId = " + siteId_);
                db.Database.ExecuteSqlCommand("update WebSiteModels set Duration = 0  where ID = " + siteId_);
            });
        }

        public void Export(string dir_, DateTime start_, DateTime end_)
        {
            start_ = new DateTime(start_.Year, start_.Month, 1, 0, 0, 0);
            end_ = new DateTime(end_.Year, end_.Month, DateTime.DaysInMonth(end_.Year, end_.Month), 23, 59, 59);

            using (var db = _database.GetReaderContext())
            {
                var webSiteData = db.WebBrowserLogs.Where(m => m.LogTime >= start_ && m.LogTime <= end_)
                    .ToList()
                    .Select(m => new
                    {
                        时间 = m.LogTime,
                        标题 = m.Url.Title,
                        网址 = m.Url.Url,
                        时长 = m.Duration,
                    });


                var mapper = new Mapper();
                mapper.Put(webSiteData, "浏览记录");

                string name = $"更矩网页统计数据({start_.ToString("yyyy年MM月")}-{end_.ToString("yyyy年MM月")})";
                if (start_.Year == end_.Year && start_.Month == end_.Month)
                {
                    name = $"更矩网页统计数据({start_.ToString("yyyy年MM月")})";
                }
                mapper.Save(Path.Combine(dir_, $"{name}.xlsx"));

                //  导出csv
                using (var writer = new StreamWriter(Path.Combine(dir_, $"{name}.csv"), false, System.Text.Encoding.UTF8))
                using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
                {
                    csv.WriteRecords(webSiteData);
                }
            }
        }

        public WebSiteModel Update(WebSiteModel website_)
        {
            WebSiteModel website = null;
            _database.ExecuteWrite(db =>
            {
                website = db.WebSites.FirstOrDefault(m => m.ID == website_.ID);
                if (website != null)
                {
                    website.Alias = website_.Alias;
                    website.Domain = website_.Domain;
                    website.Title = website_.Title;
                    db.SaveChanges();
                }
            });
            return website;
        }
    }
}