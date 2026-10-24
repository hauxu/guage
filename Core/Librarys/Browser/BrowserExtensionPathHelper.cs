using System;
using System.IO;

namespace Core.Librarys.Browser
{
    /// <summary>
    /// 定位 Guage.Chrome 浏览器扩展目录（安装包内或开发仓库内）。
    /// </summary>
    public static class BrowserExtensionPathHelper
    {
        private const string ExtensionFolderName = "Guage.Chrome";
        private const string LegacyFolderName = "WebExtensions\\Chrome";

        public static bool TryResolveDirectory(out string directory)
        {
            directory = null;
            string installDir = FileHelper.GetInstallDirectory();
            if (string.IsNullOrWhiteSpace(installDir))
            {
                return false;
            }

            string current = Path.GetFullPath(installDir);
            for (int depth = 0; depth <= 6; depth++)
            {
                if (TryDirectory(Path.Combine(current, ExtensionFolderName), out directory))
                {
                    return true;
                }

                if (TryDirectory(Path.Combine(current, LegacyFolderName), out directory))
                {
                    return true;
                }

                DirectoryInfo parent = Directory.GetParent(current);
                if (parent == null)
                {
                    break;
                }

                current = parent.FullName;
            }

            return false;
        }

        private static bool TryDirectory(string path, out string directory)
        {
            directory = null;
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return false;
            }

            string manifestPath = Path.Combine(path, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                return false;
            }

            directory = path;
            return true;
        }
    }
}
