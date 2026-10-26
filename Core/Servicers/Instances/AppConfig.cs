using Core.Event;
using Core.Librarys;
using Core.Models.Config;
using Core.Servicers.Interfaces;
using Core.Librarys.Security;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Core.Servicers.Instances
{
    public class AppConfig : IAppConfig
    {
        private string fileName;
        private ConfigModel config;

        public event AppConfigEventHandler ConfigChanged;
        public event Action StoragePathChanged;
        private ConfigModel oldConfig;

        public AppConfig()
        {
            fileName = FileHelper.GetAppConfigPath();
        }
        public void Load()
        {
            FileHelper.EnsureStorageDirectories();

            string configText = string.Empty;
            try
            {
                if (File.Exists(fileName))
                {
                    configText = File.ReadAllText(fileName);
                    config = JsonConvert.DeserializeObject<ConfigModel>(configText);
                    if (config == null)
                    {
                        //  文件存在但内容为空/仅空白：反序列化返回 null 而不抛异常，
                        //  走损坏处理重建默认配置，避免 config=null 一路 NRE 到启动崩溃
                        HandleLoadFail("配置文件内容为空或反序列化结果为 null", configText);
                        return;
                    }
                    MigrateWindowCornerStyle(configText, config);
                }
                else
                {
                    //  创建基础配置
                    CreateDefaultConfig();

                    Save();
                }

                SyncStoragePathFromRuntime();
                EnsureTimeLimitDefaults();
                EnsureBreakReminderDefaults();
                EnsureBrowserExtensionSecurity();
                SanitizeTimeLimitRulePaths();
                CopyToOldConfig();

                CheckOption(config);
            }
            catch (JsonSerializationException ex)
            {
                HandleLoadFail(ex.Message, configText);
            }
            catch (JsonReaderException ex)
            {
                HandleLoadFail(ex.Message, configText);
            }
            catch (Exception ex)
            {
                Logger.Error(ex.Message);
            }
        }

        /// <summary>
        /// 一次性迁移：圆角从"任意像素滑块"改成"系统档位"后，把旧的半径值折算成档位。
        /// 0 → 直角，1~11 → 小圆角，12 及以上 → 圆角。只在原始 JSON 里还留有旧字段时触发
        /// </summary>
        private static void MigrateWindowCornerStyle(string configText, ConfigModel config)
        {
            if (config?.General == null || string.IsNullOrEmpty(configText))
            {
                return;
            }

            try
            {
                var raw = JObject.Parse(configText);
                var oldRadius = (int?)raw["General"]?["WindowCornerRadius"];
                if (oldRadius == null)
                {
                    return;
                }

                if (oldRadius.Value <= 0)
                {
                    config.General.WindowCornerStyle = 0;
                }
                else if (oldRadius.Value < 12)
                {
                    config.General.WindowCornerStyle = 1;
                }
                else
                {
                    config.General.WindowCornerStyle = 2;
                }
            }
            catch
            {
                //  原始 JSON 解析失败不影响加载（外层损坏处理已兜底）
            }
        }

        private void SyncStoragePathFromRuntime()
        {
            if (config?.General == null)
            {
                return;
            }

            config.General.StoragePath = FileHelper.GetConfiguredStoragePath();
        }

        private void EnsureTimeLimitDefaults()
        {
            if (config.TimeLimit == null)
            {
                config.TimeLimit = new TimeLimitSettingsModel();
            }

            if (config.TimeLimit.Rules == null)
            {
                config.TimeLimit.Rules = new List<TimeLimitRuleModel>();
            }
        }

        private void EnsureBreakReminderDefaults()
        {
            if (config.BreakReminder == null)
            {
                config.BreakReminder = new BreakReminderModel();
            }
        }

        /// <summary>
        /// 清洗历史脏数据：规则路径里的双反斜杠（旧版本序列化/录入错误），
        /// 如 "D:\\\\Apps\\x.exe" → "D:\\Apps\\x.exe"。UNC 路径（\\\\server）不动
        /// </summary>
        private void SanitizeTimeLimitRulePaths()
        {
            if (config.TimeLimit?.Rules == null)
            {
                return;
            }

            foreach (var rule in config.TimeLimit.Rules)
            {
                if (string.IsNullOrEmpty(rule.ExecutablePath))
                {
                    continue;
                }

                rule.ExecutablePath = TimeLimitLogic.SanitizeExecutablePath(rule.ExecutablePath);
            }
        }

        private void EnsureBrowserExtensionSecurity()
        {
            if (config?.General == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(config.General.BrowserExtensionToken))
            {
                config.General.BrowserExtensionToken = Guid.NewGuid().ToString("N");
                Save();
            }

            WebSocketSecurity.SetToken(config.General.BrowserExtensionToken);
        }

        private void CopyToOldConfig()
        {
            string configStr = JsonConvert.SerializeObject(config);
            oldConfig = JsonConvert.DeserializeObject<ConfigModel>(configStr);
        }

        private void HandleLoadFail(string err, string configText)
        {
            //  覆盖之前先把损坏的配置留一份：配置解析失败会让用户一次性丢掉全部设置
            //  （时长限制规则、休息提醒、忽略列表、存储位置），没有副本就再也找不回来
            try
            {
                if (!string.IsNullOrEmpty(fileName) && File.Exists(fileName))
                {
                    string backupPath = fileName + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                    File.Copy(fileName, backupPath, true);
                    Logger.Error("配置文件已损坏，原件备份到：" + backupPath);
                }
            }
            catch (Exception backupEx)
            {
                Logger.Error("备份损坏配置失败：" + backupEx.Message);
            }

            //  创建基础配置
            CreateDefaultConfig();

            Save();

            Logger.Error(err + "\r\nConfig content length: " + (configText?.Length ?? 0));
        }
        public void CreateDefaultConfig()
        {
            config = new ConfigModel();
            config.Links = new List<Models.Config.Link.LinkModel>();

            config.General = new GeneralModel();
            config.General.IsStartatboot = false;

            config.Behavior = new BehaviorModel();
            config.TimeLimit = new TimeLimitSettingsModel();
            config.TimeLimit.Rules = new List<TimeLimitRuleModel>();
            config.BreakReminder = new BreakReminderModel();
        }

        public ConfigModel GetConfig()
        {
            return config;
        }

        /// <summary>
        /// 配置是否已成功加载。未加载时（如第二实例直接 Shutdown）禁止 Save——
        /// 否则会把字面量 "null" 写进配置文件，下次启动触发全量重置
        /// </summary>
        public bool IsLoaded
        {
            get { return config != null; }
        }


        private readonly object _saveLock = new object();

        public void Save()
        {
            //  设置页的防抖保存和 TimeLimitServicer 的 Task.Run 会在不同线程同时调进来：
            //  不加锁时两个线程共用同一个 .tmp 文件，互相覆盖会让配置静默丢失
            lock (_saveLock)
            {
                SaveCore();
            }
        }

        private void SaveCore()
        {
            if (config == null)
            {
                Logger.Error("配置未加载，跳过保存（防止覆写配置文件）");
                return;
            }

            try
            {
                string previousStoragePath = oldConfig?.General?.StoragePath ?? string.Empty;
                string nextStoragePath = config?.General?.StoragePath ?? string.Empty;
                bool storagePathChanged = !string.Equals(
                    NormalizeStoragePath(previousStoragePath),
                    NormalizeStoragePath(nextStoragePath),
                    StringComparison.OrdinalIgnoreCase);

                if (storagePathChanged)
                {
                    FileHelper.ApplyStoragePath(nextStoragePath, migrateData: true);
                    config.General.StoragePath = FileHelper.GetConfiguredStoragePath();
                    fileName = FileHelper.GetAppConfigPath();
                }

                string dir = Path.GetDirectoryName(fileName);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                //  原子写入：先写临时文件再替换，避免写一半崩溃损坏配置
                string tempFile = fileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(tempFile, JsonConvert.SerializeObject(config));
                try
                {
                    if (File.Exists(fileName))
                    {
                        File.Replace(tempFile, fileName, null);
                    }
                    else
                    {
                        File.Move(tempFile, fileName);
                    }
                }
                catch
                {
                    try
                    {
                        if (File.Exists(tempFile))
                        {
                            File.Delete(tempFile);
                        }
                    }
                    catch
                    {
                        //  忽略清理失败
                    }

                    throw;
                }

                if (config?.General != null)
                {
                    WebSocketSecurity.SetToken(config.General.BrowserExtensionToken);
                }

                ConfigChanged?.Invoke(oldConfig ?? config, config);

                if (storagePathChanged)
                {
                    StoragePathChanged?.Invoke();
                }

                CopyToOldConfig();
            }
            catch (Exception ex)
            {
                Logger.Error(ex.Message);
            }
        }

        private static string NormalizeStoragePath(string configuredPath)
        {
            return string.IsNullOrWhiteSpace(configuredPath)
                ? FileHelper.GetInstallDirectory()
                : Path.GetFullPath(configuredPath.Trim());
        }

        private void CheckOption(object obj)
        {
            System.Reflection.PropertyInfo[] properties = obj.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

            foreach (System.Reflection.PropertyInfo item in properties)
            {
                string name = item.Name;
                object value = item.GetValue(obj, null);
                if (value == null)
                {
                    //配置项不存在时创建
                    Type[] types = new Type[0];
                    object[] objs = new object[0];

                    ConstructorInfo ctor = item.PropertyType.GetConstructor(types);
                    if (ctor != null)
                    {
                        object instance = ctor.Invoke(objs);
                        item.SetValue(obj, instance);
                    }
                    else if (item.PropertyType == typeof(List<TimeLimitRuleModel>))
                    {
                        item.SetValue(obj, new List<TimeLimitRuleModel>());
                    }
                }
            }
        }
    }
}
