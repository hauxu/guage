using Core.Servicers.Instances;
using System;
using System.IO;
using System.Text;

namespace Guage.Tests
{
    /// <summary>
    /// AppConfig 加载/保存测试。
    /// 可测性说明：AppConfig 的存储路径来自静态 FileHelper（路径在静态构造里按入口程序集定死，
    /// 既没有构造参数也没有接口注入的接缝），所以这里用 FileHelper 的公开入口 ApplyStoragePath
    /// 把存储根目录改道到独立临时目录（见 TempStorageScope），跑完恢复并清理。
    /// 全程只读写临时目录，绝不触碰 UI\bin\Debug\Data\AppConfig.json（历史上曾因测试覆写真实配置
    /// 导致用户设置被重置），也没有为可测性改动任何产品代码。
    /// </summary>
    public static class AppConfigTests
    {
        public static void RunAll()
        {
            Run("配置-未加载时Save不落盘", TestSaveWithoutLoadDoesNotWriteFile);
            Run("配置-未加载时Save不覆写已有文件", TestSaveWithoutLoadKeepsExistingFile);
            Run("配置-空文件损坏后回退默认", TestLoadEmptyFileFallsBack);
            Run("配置-非法文本损坏后回退默认", TestLoadBrokenJsonFallsBack);
            Run("配置-缺失分节自动补齐", TestLoadFillsMissingSections);
            Run("配置-脏规则路径清洗", TestLoadSanitizesRulePaths);
            Run("配置-默认配置必需字段", TestCreateDefaultConfigFields);
            Run("配置-文件缺失时生成并落盘", TestLoadCreatesFileWhenMissing);
        }

        /// <summary>
        /// config == null 时 Save 必须直接返回：既不创建文件，也不落字面量 "null"、不残留 .tmp。
        /// </summary>
        private static void TestSaveWithoutLoadDoesNotWriteFile()
        {
            using (TempStorageScope scope = TempStorageScope.Begin())
            {
                AppConfig appConfig = new AppConfig();
                Assert.IsFalse(appConfig.IsLoaded, "未 Load 时 IsLoaded 必须为 false");

                appConfig.Save();

                Assert.IsFalse(File.Exists(scope.AppConfigPath), "配置未加载时 Save 不得创建配置文件");
                Assert.IsFalse(File.Exists(scope.AppConfigPath + ".tmp"), "不得残留临时文件");
            }
        }

        /// <summary>
        /// 未加载时的 Save 是空操作：已存在的配置文件内容必须原样保留（第二实例直接退出的场景）。
        /// </summary>
        private static void TestSaveWithoutLoadKeepsExistingFile()
        {
            const string sentinel = "{\"General\":{\"IsStartatboot\":true}}";

            using (TempStorageScope scope = TempStorageScope.Begin())
            {
                WriteConfig(scope.AppConfigPath, sentinel);

                AppConfig appConfig = new AppConfig();
                appConfig.Save();

                Assert.AreEqual(sentinel, File.ReadAllText(scope.AppConfigPath),
                    "配置未加载时 Save 不得覆写已有配置文件");
            }
        }

        /// <summary>
        /// 空文件：反序列化返回 null 而不抛异常，必须走损坏处理重建默认配置且不把 "null" 落盘。
        /// </summary>
        private static void TestLoadEmptyFileFallsBack()
        {
            using (TempStorageScope scope = TempStorageScope.Begin())
            {
                WriteConfig(scope.AppConfigPath, string.Empty);

                AppConfig appConfig = new AppConfig();
                appConfig.Load();

                Assert.IsTrue(appConfig.IsLoaded, "空文件损坏后应重建默认配置");
                var config = appConfig.GetConfig();
                Assert.IsNotNull(config, "重建后配置对象不能为空");
                Assert.IsNotNull(config.General, "重建后 General 不能为空");
                Assert.IsNotNull(config.TimeLimit, "重建后 TimeLimit 不能为空");

                string saved = File.ReadAllText(scope.AppConfigPath);
                Assert.IsTrue(saved.Length > 0, "重建后应把默认配置写回文件");
                Assert.IsFalse(saved.Trim() == "null", "绝不能把字面量 null 写进配置文件");
                Assert.IsTrue(saved.Contains("TimeLimit"), "落盘内容应包含 TimeLimit 节");
            }
        }

        /// <summary>
        /// 非法文本：解析异常必须被兜底为默认配置，Load 不得向上抛。
        /// </summary>
        private static void TestLoadBrokenJsonFallsBack()
        {
            using (TempStorageScope scope = TempStorageScope.Begin())
            {
                WriteConfig(scope.AppConfigPath, "{ \"General\": [ 这不是合法 JSON ");

                AppConfig appConfig = new AppConfig();
                appConfig.Load();

                Assert.IsTrue(appConfig.IsLoaded, "非法 JSON 应回退到默认配置");
                Assert.IsNotNull(appConfig.GetConfig(), "回退后配置对象不能为空");
                Assert.IsNotNull(appConfig.GetConfig().General, "回退后 General 必须补齐");
                Assert.IsNotNull(appConfig.GetConfig().BreakReminder, "回退后 BreakReminder 必须补齐");
                Assert.IsTrue(File.ReadAllText(scope.AppConfigPath).Contains("TimeLimit"),
                    "回退后应重新落盘默认配置");
            }
        }

        /// <summary>
        /// 缺 BreakReminder / TimeLimit 节（老版本配置）：加载后必须补齐默认值，而不是留 null。
        /// </summary>
        private static void TestLoadFillsMissingSections()
        {
            using (TempStorageScope scope = TempStorageScope.Begin())
            {
                WriteConfig(scope.AppConfigPath, "{\"General\":{}}");

                AppConfig appConfig = new AppConfig();
                appConfig.Load();

                var config = appConfig.GetConfig();
                Assert.IsNotNull(config, "配置不能为空");
                Assert.IsNotNull(config.TimeLimit, "缺失的 TimeLimit 节应补默认值");
                Assert.IsNotNull(config.TimeLimit.Rules, "缺失的 TimeLimit.Rules 应补空列表");
                Assert.AreEqual(0, config.TimeLimit.Rules.Count, "补齐的规则列表应为空");
                Assert.IsNotNull(config.BreakReminder, "缺失的 BreakReminder 节应补默认值");
            }
        }

        /// <summary>
        /// 规则里的历史脏路径（双反斜杠）加载后被清洗为单反斜杠，UNC 前缀保持不变，且清洗结果可落盘。
        /// </summary>
        private static void TestLoadSanitizesRulePaths()
        {
            //  脏数据：值本身含双反斜杠（旧版本序列化/录入错误）
            string dirtyPath = @"D:\\Apps\\chrome.exe";
            string uncPath = @"\\server\share\app.exe";

            string json = "{\"TimeLimit\":{\"IsEnabled\":false,\"Rules\":["
                + "{\"Name\":\"Chrome\",\"ProcessName\":\"chrome\",\"Enabled\":true,\"ExecutablePath\":" + JsonString(dirtyPath) + "},"
                + "{\"Name\":\"Unc\",\"ProcessName\":\"app\",\"Enabled\":true,\"ExecutablePath\":" + JsonString(uncPath) + "}"
                + "]},\"BreakReminder\":{}}";

            using (TempStorageScope scope = TempStorageScope.Begin())
            {
                WriteConfig(scope.AppConfigPath, json);

                AppConfig appConfig = new AppConfig();
                appConfig.Load();

                var rules = appConfig.GetConfig().TimeLimit.Rules;
                Assert.AreEqual(2, rules.Count, "两条规则都应保留");
                Assert.AreEqual(@"D:\Apps\chrome.exe", rules[0].ExecutablePath,
                    "规则里的双反斜杠应清洗为单反斜杠");
                Assert.AreEqual(uncPath, rules[1].ExecutablePath,
                    "UNC 路径开头的双反斜杠必须原样保留");

                appConfig.Save();
                string saved = File.ReadAllText(scope.AppConfigPath);
                Assert.IsTrue(saved.Contains(JsonString(@"D:\Apps\chrome.exe")), "清洗后的路径应落盘");
                Assert.IsFalse(saved.Contains(JsonString(dirtyPath)), "落盘内容不应残留双反斜杠脏数据");
            }
        }

        /// <summary>
        /// CreateDefaultConfig 只建内存不落盘，且必需字段全部非空。
        /// </summary>
        private static void TestCreateDefaultConfigFields()
        {
            using (TempStorageScope scope = TempStorageScope.Begin())
            {
                AppConfig appConfig = new AppConfig();
                appConfig.CreateDefaultConfig();

                var config = appConfig.GetConfig();
                Assert.IsNotNull(config, "默认配置对象不能为空");
                Assert.IsNotNull(config.Links, "Links 不能为空");
                Assert.IsNotNull(config.General, "General 不能为空");
                Assert.IsNotNull(config.Behavior, "Behavior 不能为空");
                Assert.IsNotNull(config.TimeLimit, "TimeLimit 不能为空");
                Assert.IsNotNull(config.TimeLimit.Rules, "TimeLimit.Rules 不能为空");
                Assert.IsNotNull(config.BreakReminder, "BreakReminder 不能为空");
                Assert.IsTrue(appConfig.IsLoaded, "CreateDefaultConfig 后 IsLoaded 应为 true");
                Assert.IsFalse(File.Exists(scope.AppConfigPath), "CreateDefaultConfig 只建内存，不应落盘");
            }
        }

        /// <summary>
        /// 首次启动（配置文件不存在）：Load 生成默认配置并落盘，二次加载必须能正常解析回来。
        /// </summary>
        private static void TestLoadCreatesFileWhenMissing()
        {
            using (TempStorageScope scope = TempStorageScope.Begin())
            {
                Assert.IsFalse(File.Exists(scope.AppConfigPath), "前提：临时目录内没有配置文件");

                AppConfig appConfig = new AppConfig();
                appConfig.Load();

                Assert.IsTrue(File.Exists(scope.AppConfigPath), "配置文件缺失时应生成默认配置并落盘");
                string saved = File.ReadAllText(scope.AppConfigPath);
                Assert.IsFalse(saved.Trim() == "null", "绝不能把字面量 null 写进配置文件");
                Assert.IsTrue(saved.Contains("BreakReminder"), "落盘内容应包含 BreakReminder 节");

                AppConfig reloaded = new AppConfig();
                reloaded.Load();

                Assert.IsTrue(reloaded.IsLoaded, "重新加载应成功");
                var config = reloaded.GetConfig();
                Assert.IsNotNull(config, "重新加载后配置不能为空");
                Assert.IsNotNull(config.TimeLimit, "重新加载后 TimeLimit 不能为空");
                Assert.IsNotNull(config.BreakReminder, "重新加载后 BreakReminder 不能为空");
            }
        }

        private static void WriteConfig(string path, string content)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        /// <summary>
        /// 把原始字符串转成 JSON 字符串字面量（用于手工拼装带反斜杠的配置文件）。
        /// </summary>
        private static string JsonString(string raw)
        {
            return "\"" + raw.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static void Run(string name, Action test)
        {
            Program.Run(name, test);
        }
    }
}
