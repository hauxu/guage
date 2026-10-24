using System;
using System.IO;
using System.Net;
using System.Text;

namespace Updater
{
    internal static class UpdateHttpClient
    {
        private const string UserAgent = "Guage-Updater/1.0 (Windows NT 10.0; .NET Framework 4.8)";

        public static string DownloadText(string url, int timeoutMs = 60000)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;
            if (request == null)
            {
                return null;
            }

            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;
            request.AllowAutoRedirect = true;
            request.UserAgent = UserAgent;

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (MemoryStream buffer = new MemoryStream())
            {
                if (stream == null)
                {
                    return null;
                }

                stream.CopyTo(buffer);
                if (buffer.Length > 4096)
                {
                    throw new InvalidOperationException("校验文件过大。");
                }

                return Encoding.UTF8.GetString(buffer.ToArray());
            }
        }
    }
}
