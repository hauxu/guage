using Core.Librarys;
using Core.Librarys.Security;
using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Resources;

namespace Core.Librarys.Image
{
    public class Imager
    {
        //  图标内存缓存：BitmapImage 已 Freeze，可跨线程共享；容量有上限（超出整体清空）
        private static readonly ConcurrentDictionary<string, BitmapImage> Cache =
            new ConcurrentDictionary<string, BitmapImage>(StringComparer.OrdinalIgnoreCase);
        private const int MaxCacheSize = 1024;

        // 默认图标单例（加载失败时的兜底，避免反复解析资源）
        private static readonly Lazy<BitmapImage> FallbackImage = new Lazy<BitmapImage>(() =>
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            StreamResourceInfo info = Application.GetResourceStream(
                new Uri("pack://application:,,,/guage;component/Resources/Icons/defaultIcon.png", UriKind.RelativeOrAbsolute));
            using (var st = info.Stream)
            {
                bitmap.StreamSource = st;
                bitmap.EndInit();
                bitmap.Freeze();
            }
            return bitmap;
        });

        //  负缓存：文件不存在/读取失败 keyed by path → 首次失败时间。
        //  缺失文件（图标被清理）不做负缓存时，每 5 秒整表刷新都会重读一次并刷一条
        //  Error 日志；TTL 过后自动重试，文件补回来仍会正常显示
        private static readonly TimeSpan NegativeCacheTtl = TimeSpan.FromSeconds(60);
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> FailedAt
            = new System.Collections.Concurrent.ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);

        /// <summary>
        /// 加载图片。仅缓存成功结果——失败（文件尚在下载/解码失败）返回默认图标但不入缓存，
        /// 下次调用会重新尝试，保证图标下载完成后所有显示位置自动更新。
        /// </summary>
        public static BitmapImage Load(string filePath, string defaultPath = "pack://application:,,,/guage;component/Resources/Icons/defaultIcon.png")
        {
            //  空/缺失路径（favicon 未下载完成的网页条目）静默走兜底图标：
            //  不走 LoadCore 抛异常记 Error——否则每次 5 秒刷新都重复刷错误日志
            if (string.IsNullOrEmpty(filePath))
            {
                return FallbackImage.Value;
            }

            if (Cache.TryGetValue(filePath, out BitmapImage cached))
            {
                return cached;
            }

            //  负缓存窗口内直接走兜底：文件缺失是持续状态，反复重读磁盘毫无意义
            if (FailedAt.TryGetValue(filePath, out DateTime failedAt))
            {
                if (DateTime.UtcNow - failedAt < NegativeCacheTtl)
                {
                    return FallbackImage.Value;
                }
                FailedAt.TryRemove(filePath, out _);
            }

            BitmapImage result = LoadCore(filePath, defaultPath);
            if (result != null)
            {
                Cache[filePath ?? string.Empty] = result;
                //  淘汰最旧的一半（替代整体 Clear：全清会导致下一轮批量缓存
                //  miss → UI 线程逐图标磁盘重读，表现为一次集体掉帧）
                if (Cache.Count > MaxCacheSize)
                {
                    int evict = MaxCacheSize / 2;
                    foreach (var key in Cache.Keys.Take(evict).ToList())
                    {
                        Cache.TryRemove(key, out _);
                    }
                }
                return result;
            }

            FailedAt[filePath] = DateTime.UtcNow;
            return FallbackImage.Value;
        }

        private static BitmapImage LoadCore(string filePath, string defaultPath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    throw new ArgumentNullException(nameof(filePath));
                }

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                if (filePath.IndexOf("pack://") != -1)
                {
                    StreamResourceInfo info = Application.GetResourceStream(new Uri(filePath, UriKind.RelativeOrAbsolute));
                    using (var st = info.Stream)
                    {
                        bitmap.StreamSource = st;
                        bitmap.EndInit();
                        bitmap.Freeze();
                    }
                }
                else
                {
                    if (!PathSecurity.TryGetSafeStoragePath(filePath, out string safePath))
                    {
                        throw new UnauthorizedAccessException(filePath);
                    }

                    filePath = safePath;

                    if (!File.Exists(filePath))
                    {
                        throw new FileNotFoundException(filePath);
                    }
                    bitmap.DecodePixelWidth = 128;
                    bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    byte[] imageData;
                    using (var fileStream = new FileStream(@filePath, FileMode.Open, FileAccess.Read))

                    using (var binaryReader = new BinaryReader(fileStream))
                    {
                        imageData = binaryReader.ReadBytes((int)fileStream.Length);
                        bitmap.StreamSource = new MemoryStream(imageData);
                        bitmap.EndInit();
                        bitmap.Freeze();
                    }
                }

                return bitmap;
            }
            catch (Exception ec)
            {
                //  失败返回 null，由调用方返回默认图标（不入缓存，下次重试）
                Logger.Error("无法读取图片：" + filePath + "。" + ec.Message);
                return null;
            }
        }
    }
}
