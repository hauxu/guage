using Core.Librarys.Security;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Core.Librarys.Browser.Favicon
{
    public class FaviconDownloader
    {
        private const int TimeoutMilliseconds = 6000;
        private const int MaxFaviconBytes = 512 * 1024;
        private const int MaxRedirects = 3;
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        /// <summary>
        /// 下载文件。逐跳安全校验（SSRF 防护）+ 大小限制 + 重定向复检。
        /// </summary>
        /// <param name="url_">文件远程地址</param>
        /// <param name="savePath_">本地保存路径</param>
        public static async Task<string> DownloadAsync(string url_, string saveName_, string pageUrl = null)
        {
            if (string.IsNullOrEmpty(url_) || !UrlSecurity.TryCreateSafeHttpUri(url_, out Uri currentUri))
            {
                return string.Empty;
            }

            string savePath = Path.Combine(FileHelper.GetWebFaviconsDirectory(), saveName_);
            string dir = Path.GetDirectoryName(savePath);

            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            if (File.Exists(savePath))
            {
                return savePath;
            }

            //  仅替换路径中的 .svg 扩展名，避免破坏查询参数
            if (currentUri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                UriBuilder builder = new UriBuilder(currentUri);
                builder.Path = builder.Path.Substring(0, builder.Path.Length - 4) + ".png";
                currentUri = builder.Uri;
            }

            //  请求头 Referer：部分站点（如哔哩哔哩）对无 Referer 的图标请求返回 4xx
            string referer = null;
            if (!string.IsNullOrWhiteSpace(pageUrl)
                && UrlSecurity.TryCreateSafeHttpUri(pageUrl, out Uri pageUri))
            {
                referer = pageUri.Scheme + "://" + pageUri.Host + "/";
            }

            return await Task.Run(() => DownloadWithChecks(currentUri, savePath, pageUrl, referer));
        }

        private static string DownloadWithChecks(Uri startUri, string savePath, string pageUrl, string referer)
        {
            Uri currentUri = startUri;

            for (int hop = 0; hop <= MaxRedirects; hop++)
            {
                bool isSafe = string.IsNullOrWhiteSpace(pageUrl)
                    ? NetworkSecurity.IsSafeRemoteUrl(currentUri.AbsoluteUri)
                    : NetworkSecurity.IsSafeFaviconUrl(currentUri.AbsoluteUri, pageUrl);

                if (!isSafe)
                {
                    return string.Empty;
                }

                try
                {
                    var request = (HttpWebRequest)WebRequest.Create(currentUri);
                    request.Timeout = TimeoutMilliseconds;
                    request.ReadWriteTimeout = TimeoutMilliseconds;
                    request.AllowAutoRedirect = false;
                    request.UserAgent = UserAgent;
                    if (!string.IsNullOrWhiteSpace(referer))
                    {
                        request.Referer = referer;
                    }

                    //  将连接钉在校验过的公网 IP 上，防止 DNS 重绑定
                    if (!NetworkSecurity.TryBindSafeRemote(request, currentUri))
                    {
                        return string.Empty;
                    }

                    using (var response = (HttpWebResponse)request.GetResponse())
                    {
                        int status = (int)response.StatusCode;
                        if (status >= 300 && status < 400)
                        {
                            string location = response.Headers["Location"];
                            if (string.IsNullOrWhiteSpace(location)
                                || !Uri.TryCreate(currentUri, location, out Uri next)
                                || (next.Scheme != Uri.UriSchemeHttp && next.Scheme != Uri.UriSchemeHttps))
                            {
                                return string.Empty;
                            }

                            currentUri = next;
                            continue;
                        }

                        if (status != 200)
                        {
                            return string.Empty;
                        }

                        using (var stream = response.GetResponseStream())
                        {
                            if (stream == null)
                            {
                                return string.Empty;
                            }

                            using (var buffer = new MemoryStream())
                            {
                                var data = new byte[8192];
                                int total = 0;
                                int read;
                                while ((read = stream.Read(data, 0, data.Length)) > 0)
                                {
                                    total += read;
                                    if (total > MaxFaviconBytes)
                                    {
                                        return string.Empty;
                                    }

                                    buffer.Write(data, 0, read);
                                }

                                if (buffer.Length == 0)
                                {
                                    return string.Empty;
                                }

                                File.WriteAllBytes(savePath, buffer.ToArray());
                                return savePath;
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Error("下载图标失败，" + e);
                    return string.Empty;
                }
            }

            return string.Empty;
        }
    }
}
