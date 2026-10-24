using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Updater
{
    public class Unzip
    {
        /// <summary>
        /// 忽略的文件列表
        /// </summary>
        static readonly string[] IgnoreFiles = { };
        public static bool ExtractZipFile(string zipPath, string extractPath)
        {
            try
            {
                if (!extractPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                    extractPath += Path.DirectorySeparatorChar;

                //  规范化根目录，供包含性校验使用
                string root = Path.GetFullPath(extractPath);

                using (ZipArchive archive = ZipFile.OpenRead(zipPath))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (IsIgnoreFile(entry.FullName))
                        {
                            continue;
                        }

                        //  目录条目：以 / 或 \ 结尾，或 Name 为空
                        bool isDirectory = entry.FullName.EndsWith("/", StringComparison.Ordinal)
                            || entry.FullName.EndsWith("\\", StringComparison.Ordinal)
                            || string.IsNullOrEmpty(entry.Name);

                        if (isDirectory)
                        {
                            string dirPath = Path.GetFullPath(Path.Combine(root, entry.FullName));
                            if (IsUnderRoot(dirPath, root))
                            {
                                Directory.CreateDirectory(dirPath);
                            }

                            continue;
                        }

                        //  先做路径穿越校验：任何目录创建/文件删除/写入都必须在通过之后
                        string destinationPath = Path.GetFullPath(Path.Combine(root, entry.FullName));
                        if (!IsUnderRoot(destinationPath, root))
                        {
                            //  拒绝解压根目录之外的条目（zip-slip 防护）
                            Debug.WriteLine($"拒绝路径穿越条目：{entry.FullName}");
                            continue;
                        }

                        string dir = Path.GetDirectoryName(destinationPath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }

                        if (File.Exists(destinationPath))
                        {
                            File.Delete(destinationPath);
                        }

                        entry.ExtractToFile(destinationPath);
                    }
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 指示文件是否是忽略的
        /// </summary>
        static bool IsIgnoreFile(string fileName)
        {
            return (Array.IndexOf(IgnoreFiles, fileName) != -1);
        }

        /// <summary>
        /// 判断 fullPath 是否位于 root 之内（含根目录本身）。
        /// </summary>
        private static bool IsUnderRoot(string fullPath, string root)
        {
            if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(root))
            {
                return false;
            }

            string normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fullPath, root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }
    }
}
