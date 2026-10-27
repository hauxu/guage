using Core.Librarys;
using Core.Librarys.SQLite;
using Core.Models;
using Core.Servicers.Instances;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Guage.Tests
{
    /// <summary>
    /// 数据导出的真实落盘验证：在临时存储根里灌一条应用与当天用量，
    /// 跑 ExportToExcel，检查产出的 xlsx 是不是真的能打开（zip 结构）。
    /// </summary>
    public static class ExportTests
    {
        public static void RunAll()
        {
            Run("导出-生成可打开的 xlsx", TestExportExcel);
        }

        private static void TestExportExcel()
        {
            using (var scope = TempStorageScope.Begin())
            {
                var database = new Database();
                var appData = new AppData(database);
                var data = new Data(appData, database);

                DateTime today = DateTime.Now.Date;
                DateTime hourSlot = new DateTime(today.Year, today.Month, today.Day, DateTime.Now.Hour, 0, 0);

                database.ExecuteWrite(db =>
                {
                    var app = new AppModel { Name = "exporttest", File = @"C:\Test\exporttest.exe", TotalTime = 300 };
                    db.App.Add(app);
                    db.SaveChanges();

                    db.DailyLog.Add(new DailyLogModel { Date = today, AppModelID = app.ID, Time = 300 });
                    db.HoursLog.Add(new HoursLogModel { DataTime = hourSlot, AppModelID = app.ID, Time = 300 });
                    db.SaveChanges();
                });

                string outDir = Path.Combine(scope.Root, "export");
                Directory.CreateDirectory(outDir);

                data.ExportToExcel(outDir, today, today);

                string[] files = Directory.GetFiles(outDir, "*.xlsx");
                Assert.IsTrue(files.Length > 0, "应导出至少一个 xlsx");

                foreach (string file in files)
                {
                    var info = new FileInfo(file);
                    Assert.IsTrue(info.Length > 2000, "xlsx 内容不应为空：" + Path.GetFileName(file));

                    using (var stream = File.OpenRead(file))
                    {
                        byte[] head = new byte[2];
                        stream.Read(head, 0, 2);
                        Assert.IsTrue(head[0] == 0x50 && head[1] == 0x4B, "xlsx 应是 zip 结构（PK）：" + Path.GetFileName(file));
                    }
                }
            }
        }

        private static void Run(string name, Action test) => Program.Run(name, test);
    }
}
