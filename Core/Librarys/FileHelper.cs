using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace Core.Librarys
{
    /// <summary>
    /// 文件与存储路径帮助类。默认所有数据保存在软件安装目录；
    /// 用户可在设置中指定自定义目录，通过安装目录下的 storage.path 引导加载。
    /// </summary>
    public static class FileHelper
    {
        private const string StoragePointerFileName = "storage.path";

        private static string _installDirectory;
        private static string _storageRoot;

        static FileHelper()
        {
            Initialize();
        }

        public static void Initialize()
        {
            _installDirectory = ResolveInstallDirectory();
            _storageRoot = ResolveStorageRoot(_installDirectory);
        }

        /// <summary>
        /// 软件安装目录，始终为 guage.exe 所在目录。
        /// </summary>
        public static string GetInstallDirectory()
        {
            if (string.IsNullOrEmpty(_installDirectory))
            {
                Initialize();
            }
            return _installDirectory;
        }

        /// <summary>
        /// 当前数据存储根目录。兼容旧代码，等同于 <see cref="GetStorageRoot"/>。
        /// </summary>
        public static string GetRootDirectory()
        {
            return GetStorageRoot();
        }

        /// <summary>
        /// 当前数据存储根目录，包含 Data、Log、AppIcons 等子目录。
        /// </summary>
        public static string GetStorageRoot()
        {
            if (string.IsNullOrEmpty(_storageRoot))
            {
                Initialize();
            }
            return _storageRoot;
        }

        public static string GetDataDirectory()
        {
            return Path.Combine(GetStorageRoot(), "Data");
        }

        public static string GetLogDirectory()
        {
            return Path.Combine(GetStorageRoot(), "Log");
        }

        public static string GetAppIconsDirectory()
        {
            return Path.Combine(GetStorageRoot(), "AppIcons");
        }

        public static string GetWebIconsDirectory()
        {
            return Path.Combine(GetStorageRoot(), "WebIcons");
        }

        public static string GetWebFaviconsDirectory()
        {
            return Path.Combine(GetStorageRoot(), "WebFavicons");
        }

        public static string GetAppConfigPath()
        {
            return Path.Combine(GetDataDirectory(), "AppConfig.json");
        }

        public static string GetDatabasePath()
        {
            return Path.Combine(GetDataDirectory(), "data.db");
        }

        /// <summary>
        /// 供配置界面显示：空字符串表示使用默认安装目录。
        /// </summary>
        public static string GetConfiguredStoragePath()
        {
            if (IsDefaultStorageRoot())
            {
                return string.Empty;
            }
            return GetStorageRoot();
        }

        public static bool IsDefaultStorageRoot()
        {
            return string.Equals(
                GetStorageRoot(),
                GetInstallDirectory(),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 应用新的存储目录，并在需要时迁移旧数据。
        /// </summary>
        public static void ApplyStoragePath(string configuredPath, bool migrateData = true)
        {
            string newRoot = NormalizeStoragePath(configuredPath);
            string oldRoot = GetStorageRoot();

            if (string.Equals(oldRoot, newRoot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (migrateData)
            {
                MigrateStorageData(oldRoot, newRoot);
            }

            _storageRoot = newRoot;
            WriteStoragePointer(newRoot);
            EnsureStorageDirectories();
        }

        public static void EnsureStorageDirectories()
        {
            Directory.CreateDirectory(GetDataDirectory());
            Directory.CreateDirectory(GetLogDirectory());
            Directory.CreateDirectory(GetAppIconsDirectory());
            Directory.CreateDirectory(GetWebIconsDirectory());
            Directory.CreateDirectory(GetWebFaviconsDirectory());
            Directory.CreateDirectory(Path.Combine(GetStorageRoot(), "CategoryIcons"));
        }

        /// <summary>
        /// 读取安装目录下的 storage.path，供 GuageBug 等未引用 Core 的程序使用。
        /// </summary>
        public static string ReadStorageRootFromPointer(string installDirectory)
        {
            if (string.IsNullOrEmpty(installDirectory))
            {
                return installDirectory;
            }

            try
            {
                string pointerPath = Path.Combine(installDirectory, StoragePointerFileName);
                if (!File.Exists(pointerPath))
                {
                    return installDirectory;
                }

                string customPath = File.ReadAllText(pointerPath, Encoding.UTF8).Trim();
                if (string.IsNullOrEmpty(customPath))
                {
                    return installDirectory;
                }

                customPath = Path.GetFullPath(customPath);
                return Directory.Exists(customPath) ? customPath : installDirectory;
            }
            catch
            {
                return installDirectory;
            }
        }

        private static string ResolveInstallDirectory()
        {
            Assembly entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly != null && !string.IsNullOrEmpty(entryAssembly.Location))
            {
                return Path.GetDirectoryName(entryAssembly.Location);
            }

            return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        }

        private static string ResolveStorageRoot(string installDirectory)
        {
            return ReadStorageRootFromPointer(installDirectory);
        }

        private static string NormalizeStoragePath(string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                return GetInstallDirectory();
            }

            return Path.GetFullPath(configuredPath.Trim());
        }

        private static void WriteStoragePointer(string storageRoot)
        {
            string pointerPath = Path.Combine(GetInstallDirectory(), StoragePointerFileName);
            if (string.Equals(storageRoot, GetInstallDirectory(), StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(pointerPath))
                {
                    File.Delete(pointerPath);
                }
                return;
            }

            File.WriteAllText(pointerPath, storageRoot, Encoding.UTF8);
        }

        private static void MigrateStorageData(string fromRoot, string toRoot)
        {
            if (string.IsNullOrEmpty(fromRoot)
                || string.IsNullOrEmpty(toRoot)
                || string.Equals(fromRoot, toRoot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            CopyDirectoryContents(Path.Combine(fromRoot, "Data"), Path.Combine(toRoot, "Data"));
            CopyDirectoryContents(Path.Combine(fromRoot, "Log"), Path.Combine(toRoot, "Log"));
            CopyDirectoryContents(Path.Combine(fromRoot, "AppIcons"), Path.Combine(toRoot, "AppIcons"));
            CopyDirectoryContents(Path.Combine(fromRoot, "WebIcons"), Path.Combine(toRoot, "WebIcons"));
            CopyDirectoryContents(Path.Combine(fromRoot, "WebFavicons"), Path.Combine(toRoot, "WebFavicons"));
        }

        private static void CopyDirectoryContents(string sourceDir, string targetDir)
        {
            if (!Directory.Exists(sourceDir))
            {
                return;
            }

            Directory.CreateDirectory(targetDir);

            foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relativePath = file.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string targetFile = Path.Combine(targetDir, relativePath);
                string targetFileDir = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrEmpty(targetFileDir))
                {
                    Directory.CreateDirectory(targetFileDir);
                }

                if (!File.Exists(targetFile))
                {
                    File.Copy(file, targetFile);
                }
            }
        }
    }
}
