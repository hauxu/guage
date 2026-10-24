using Core.Librarys;
using System;
using System.IO;

namespace Core.Librarys.Security
{
    /// <summary>
    /// 限制文件访问在 更矩 数据存储目录内。
    /// </summary>
    public static class PathSecurity
    {
        public static bool TryGetSafeStoragePath(string filePath, out string fullPath)
        {
            fullPath = null;
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            if (filePath.StartsWith("pack://", StringComparison.OrdinalIgnoreCase))
            {
                fullPath = filePath;
                return true;
            }

            string storageRoot = Path.GetFullPath(FileHelper.GetStorageRoot());
            string installRoot = Path.GetFullPath(FileHelper.GetInstallDirectory());

            string candidate = filePath.IndexOf(':') >= 0
                ? Path.GetFullPath(filePath)
                : Path.GetFullPath(Path.Combine(storageRoot, filePath));

            if (IsUnderRoot(candidate, storageRoot) || IsUnderRoot(candidate, installRoot))
            {
                fullPath = candidate;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 将 pack:// 资源、数据目录内路径或外部图片统一规范为可安全读取的路径。
        /// 外部图片会复制到数据目录下的指定子文件夹。
        /// </summary>
        public static bool TryNormalizeIconPath(string filePath, string storageSubFolder, out string normalizedPath)
        {
            normalizedPath = null;
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            if (filePath.StartsWith("pack://", StringComparison.OrdinalIgnoreCase))
            {
                normalizedPath = filePath;
                return true;
            }

            if (TryGetSafeStoragePath(filePath, out string safePath))
            {
                normalizedPath = ToStorageRelativePath(safePath);
                return true;
            }

            if (!File.Exists(filePath))
            {
                return false;
            }

            try
            {
                string targetDir = Path.Combine(FileHelper.GetStorageRoot(), storageSubFolder ?? "CategoryIcons");
                Directory.CreateDirectory(targetDir);

                string extension = Path.GetExtension(filePath);
                if (string.IsNullOrWhiteSpace(extension))
                {
                    extension = ".png";
                }

                string targetFileName = Guid.NewGuid().ToString("N") + extension.ToLowerInvariant();
                string targetFullPath = Path.Combine(targetDir, targetFileName);
                File.Copy(filePath, targetFullPath, overwrite: false);
                normalizedPath = Path.Combine(storageSubFolder ?? "CategoryIcons", targetFileName);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("导入外部图标失败：" + ex.Message);
                return false;
            }
        }

        private static string ToStorageRelativePath(string fullPath)
        {
            string storageRoot = Path.GetFullPath(FileHelper.GetStorageRoot());
            string normalized = Path.GetFullPath(fullPath);
            if (normalized.StartsWith(storageRoot, StringComparison.OrdinalIgnoreCase))
            {
                return normalized.Substring(storageRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }

            return normalized;
        }

        private static bool IsUnderRoot(string fullPath, string rootPath)
        {
            if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(rootPath))
            {
                return false;
            }

            string normalizedRoot = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fullPath, rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }
    }
}
