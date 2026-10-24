using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Core.Librarys.Security;

namespace Core.Librarys
{
    /// <summary>
    /// 网站图标获取。先请求 /favicon.ico，失败时解析首页 HTML 中的 link 标签，
    /// 直接向目标站点请求并转存为本地 PNG，不经过第三方图标服务。
    /// </summary>
    public static class FaviconFetcher
    {
        private const string IconDirectory = "WebIcons";
        private const int TimeoutMilliseconds = 8000;
        private const int MaxHtmlBytes = 65536;
        //  图标响应体大小上限：防御超大响应占满磁盘
        private const int MaxFaviconBytes = 512 * 1024;
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        private static readonly Regex LinkTagRegex = new Regex(
            @"<link\b[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RelRegex = new Regex(
            @"\brel\s*=\s*(['""])(?<value>.*?)\1",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex HrefRegex = new Regex(
            @"\bhref\s*=\s*(['""])(?<value>.*?)\1",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// 获取指定域名的图标，返回可直接存入 IconFile 的相对路径；失败时返回空字符串。
        /// </summary>
        public static string Fetch(string domain)
        {
            if (string.IsNullOrEmpty(domain))
            {
                return string.Empty;
            }

            string fileName = ToSafeFileName(domain) + ".png";
            string relativePath = Path.Combine(IconDirectory, fileName);
            string fullPath = Path.Combine(FileHelper.GetWebIconsDirectory(), fileName);

            if (File.Exists(fullPath))
            {
                return relativePath;
            }

            foreach (string scheme in new[] { "https", "http" })
            {
                string pageUrl = scheme + "://" + domain + "/";
                string faviconUrl = pageUrl + "favicon.ico";
                if (TrySave(faviconUrl, fullPath, pageUrl))
                {
                    return relativePath;
                }
            }

            foreach (string scheme in new[] { "https", "http" })
            {
                string pageUrl = scheme + "://" + domain + "/";
                foreach (string iconUrl in DiscoverIconUrls(domain, scheme))
                {
                    if (TrySave(iconUrl, fullPath, pageUrl))
                    {
                        return relativePath;
                    }
                }
            }

            return string.Empty;
        }

        private static IEnumerable<string> DiscoverIconUrls(string domain, string scheme)
        {
            string pageUrl = scheme + "://" + domain + "/";
            string html = DownloadHtml(pageUrl);
            if (string.IsNullOrEmpty(html))
            {
                yield break;
            }

            var candidates = new List<KeyValuePair<int, string>>();

            foreach (Match linkMatch in LinkTagRegex.Matches(html))
            {
                string tag = linkMatch.Value;
                string rel = GetAttribute(tag, RelRegex);
                string href = GetAttribute(tag, HrefRegex);

                if (string.IsNullOrEmpty(rel) || string.IsNullOrEmpty(href))
                {
                    continue;
                }

                if (!IsIconRel(rel))
                {
                    continue;
                }

                string resolved = ResolveUrl(pageUrl, href);
                if (string.IsNullOrEmpty(resolved) || IsUnsupportedIconUrl(resolved))
                {
                    continue;
                }

                candidates.Add(new KeyValuePair<int, string>(GetRelPriority(rel, resolved), resolved));
            }

            foreach (var candidate in candidates
                .OrderBy(m => m.Key)
                .Select(m => m.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                yield return candidate;
            }
        }

        private static string DownloadHtml(string url)
        {
            if (!UrlSecurity.TryCreateSafeHttpUri(url, out Uri currentUri))
            {
                return string.Empty;
            }

            //  逐跳请求：每跳都重新做安全校验并把连接钉在校验过的公网 IP 上，重定向最多 3 次
            for (int hop = 0; hop <= 3; hop++)
            {
                if (!NetworkSecurity.IsSafeRemoteUrl(currentUri.AbsoluteUri))
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
                    request.Method = "GET";

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
                                var data = new byte[4096];
                                int totalRead = 0;
                                int read;
                                while ((read = stream.Read(data, 0, data.Length)) > 0)
                                {
                                    int remaining = MaxHtmlBytes - totalRead;
                                    if (remaining <= 0)
                                    {
                                        break;
                                    }

                                    int writeCount = Math.Min(read, remaining);
                                    buffer.Write(data, 0, writeCount);
                                    totalRead += writeCount;
                                }

                                if (buffer.Length == 0)
                                {
                                    return string.Empty;
                                }

                                string charset = response.CharacterSet;
                                Encoding encoding = GetEncoding(charset);
                                return encoding.GetString(buffer.ToArray());
                            }
                        }
                    }
                }
                catch
                {
                    return string.Empty;
                }
            }

            return string.Empty;
        }

        private static Encoding GetEncoding(string charset)
        {
            if (string.IsNullOrEmpty(charset))
            {
                return Encoding.UTF8;
            }

            try
            {
                return Encoding.GetEncoding(charset);
            }
            catch
            {
                return Encoding.UTF8;
            }
        }

        private static string GetAttribute(string tag, Regex regex)
        {
            Match match = regex.Match(tag);
            return match.Success ? WebUtility.HtmlDecode(match.Groups["value"].Value.Trim()) : string.Empty;
        }

        private static bool IsIconRel(string rel)
        {
            var tokens = rel.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string token in tokens)
            {
                if (token.Equals("icon", StringComparison.OrdinalIgnoreCase)
                    || token.Equals("shortcut", StringComparison.OrdinalIgnoreCase)
                    || token.Equals("apple-touch-icon", StringComparison.OrdinalIgnoreCase)
                    || token.Equals("apple-touch-icon-precomposed", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static int GetRelPriority(string rel, string url)
        {
            string lowerRel = rel.ToLowerInvariant();
            string lowerUrl = url.ToLowerInvariant();

            if (lowerRel.Contains("apple-touch-icon"))
            {
                return 30;
            }

            if (lowerRel.Contains("shortcut"))
            {
                return 20;
            }

            if (lowerUrl.Contains(".png"))
            {
                return 0;
            }

            if (lowerUrl.Contains(".ico"))
            {
                return 5;
            }

            return 10;
        }

        private static bool IsUnsupportedIconUrl(string url)
        {
            string lower = url.ToLowerInvariant();
            return lower.Contains(".svg")
                || lower.StartsWith("data:")
                || lower.StartsWith("javascript:");
        }

        private static string ResolveUrl(string baseUrl, string href)
        {
            try
            {
                if (href.StartsWith("//", StringComparison.Ordinal))
                {
                    var baseUri = new Uri(baseUrl);
                    href = baseUri.Scheme + ":" + href;
                }

                var resolved = new Uri(new Uri(baseUrl), href);
                if (resolved.Scheme != Uri.UriSchemeHttp && resolved.Scheme != Uri.UriSchemeHttps)
                {
                    return string.Empty;
                }

                return resolved.AbsoluteUri;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool TrySave(string url, string savePath, string pageUrl = null)
        {
            //  逐跳请求：每跳都重新做安全校验并把连接钉在校验过的公网 IP 上，重定向最多 3 次
            for (int hop = 0; hop <= 3; hop++)
            {
                if (!UrlSecurity.TryCreateSafeHttpUri(url, out Uri currentUri))
                {
                    return false;
                }

                bool isSafe = string.IsNullOrWhiteSpace(pageUrl)
                    ? NetworkSecurity.IsSafeRemoteUrl(currentUri.AbsoluteUri)
                    : NetworkSecurity.IsSafeFaviconUrl(currentUri.AbsoluteUri, pageUrl);

                if (!isSafe)
                {
                    return false;
                }

                try
                {
                    var request = (HttpWebRequest)WebRequest.Create(currentUri);
                    request.Timeout = TimeoutMilliseconds;
                    request.ReadWriteTimeout = TimeoutMilliseconds;
                    request.AllowAutoRedirect = false;
                    request.UserAgent = UserAgent;

                    if (!NetworkSecurity.TryBindSafeRemote(request, currentUri))
                    {
                        return false;
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
                                return false;
                            }

                            url = next.AbsoluteUri;
                            continue;
                        }

                        if (status != 200)
                        {
                            return false;
                        }

                        using (var stream = response.GetResponseStream())
                        {
                            if (stream == null)
                            {
                                return false;
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
                                        //  响应超出大小上限，拒绝落盘
                                        return false;
                                    }

                                    buffer.Write(data, 0, read);
                                }

                                if (buffer.Length == 0)
                                {
                                    return false;
                                }

                                return SaveAsPng(buffer, savePath);
                            }
                        }
                    }
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private static bool SaveAsPng(MemoryStream buffer, string savePath)
        {
            string directory = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            buffer.Position = 0;
            try
            {
                using (var image = Image.FromStream(buffer))
                {
                    image.Save(savePath, System.Drawing.Imaging.ImageFormat.Png);
                    return true;
                }
            }
            catch
            {
                //  继续按真正的 ICO 解析
            }

            buffer.Position = 0;
            try
            {
                using (var icon = new Icon(buffer))
                using (var bitmap = icon.ToBitmap())
                {
                    bitmap.Save(savePath, System.Drawing.Imaging.ImageFormat.Png);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static string ToSafeFileName(string domain)
        {
            var name = domain;
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }
            return name.Replace('.', '_');
        }
    }
}
