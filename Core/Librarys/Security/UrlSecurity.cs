using System;
using System.Diagnostics;

namespace Core.Librarys.Security
{
    /// <summary>
    /// 安全打开外部链接，仅允许 http/https。
    /// </summary>
    public static class UrlSecurity
    {
        public static bool TryCreateSafeHttpUri(string url, out Uri uri)
        {
            uri = null;
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            string normalized = url.Trim();
            if (!normalized.Contains("://"))
            {
                normalized = "http://" + normalized;
            }

            if (!Uri.TryCreate(normalized, UriKind.Absolute, out uri))
            {
                return false;
            }

            return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryLaunchUrl(string url)
        {
            if (!TryCreateSafeHttpUri(url, out Uri uri))
            {
                return false;
            }

            Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
            return true;
        }
    }
}
