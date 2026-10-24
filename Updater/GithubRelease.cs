using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Updater
{
    public class GithubRelease
    {
        public class VersionInfo
        {
            public string Title { get; set; }
            public string Version { get; set; }
            public bool IsPre { get; set; }
            public string DownloadUrl { get; set; }
            public string HtmlUrl { get; set; }
            public string PackageFileName { get; set; }
            public string ChecksumDownloadUrl { get; set; }
            public string ExpectedSha256 { get; set; }
            public string SignatureDownloadUrl { get; set; }
            public bool HasChecksum => !string.IsNullOrWhiteSpace(ExpectedSha256);
            public bool HasSignature => !string.IsNullOrWhiteSpace(SignatureDownloadUrl);
        }

        public class GithubModel
        {
            public string tag_name { get; set; }
            public string html_url { get; set; }
            public string name { get; set; }
            public bool prerelease { get; set; }
            public List<GithubAssetsModel> assets { get; set; }
        }

        public class GithubAssetsModel
        {
            public string name { get; set; }
            public string browser_download_url { get; set; }
        }

        private readonly string githubUrl;
        private readonly string nowVersion;
        public VersionInfo Info { get; set; }

        public GithubRelease(string githubUrl, string nowVersion)
        {
            this.githubUrl = githubUrl;
            this.nowVersion = nowVersion;
            Info = new VersionInfo();
        }

        public bool IsCanUpdate()
        {
            return !(nowVersion == Info.Version);
        }

        public async Task<VersionInfo> GetRequest()
        {
            VersionInfo result = await Task.Run(() =>
            {
                try
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                    HttpWebRequest httpWebRequest = WebRequest.Create(githubUrl) as HttpWebRequest;
                    httpWebRequest.Timeout = 60 * 1000;
                    httpWebRequest.ReadWriteTimeout = 60000;
                    httpWebRequest.AllowAutoRedirect = true;
                    httpWebRequest.UserAgent = "Guage-Updater/1.0 (Windows NT 10.0; .NET Framework 4.8)";
                    using (HttpWebResponse httpWebRespones = (HttpWebResponse)httpWebRequest.GetResponse())
                    using (Stream stream = httpWebRespones.GetResponseStream())
                    using (MemoryStream buffer = new MemoryStream())
                    {
                        stream.CopyTo(buffer);
                        string body = Encoding.UTF8.GetString(buffer.ToArray());
                        GithubModel data = JsonConvert.DeserializeObject<GithubModel>(body);
                        if (data?.assets == null || data.assets.Count == 0)
                        {
                            return null;
                        }

                        GithubAssetsModel packageAsset = ResolvePackageAsset(data.assets);
                        if (packageAsset == null)
                        {
                            return null;
                        }

                        Info.IsPre = data.prerelease;
                        Info.Title = data.name;
                        Info.Version = data.tag_name;
                        Info.DownloadUrl = packageAsset.browser_download_url;
                        Info.PackageFileName = packageAsset.name;
                        Info.HtmlUrl = data.html_url;

                        GithubAssetsModel checksumAsset = ResolveChecksumAsset(data.assets, packageAsset.name);
                        if (checksumAsset != null)
                        {
                            Info.ChecksumDownloadUrl = checksumAsset.browser_download_url;
                            string checksumText = UpdateHttpClient.DownloadText(checksumAsset.browser_download_url);
                            if (UpdatePackageVerifier.TryParseExpectedHash(checksumText, packageAsset.name, out string expectedHash))
                            {
                                Info.ExpectedSha256 = expectedHash;
                            }
                        }

                        //  签名资产：对 .sha256 文件签名的 Ed25519 签名（防篡改，见 UpdateSignatureVerifier）
                        GithubAssetsModel signatureAsset = ResolveSignatureAsset(data.assets, packageAsset.name);
                        if (signatureAsset != null)
                        {
                            Info.SignatureDownloadUrl = signatureAsset.browser_download_url;
                        }

                        return Info;
                    }
                }
                catch
                {
                    return null;
                }
            });

            return result;
        }

        private static GithubAssetsModel ResolvePackageAsset(IEnumerable<GithubAssetsModel> assets)
        {
            GithubAssetsModel[] list = assets.Where(a => a != null && !string.IsNullOrWhiteSpace(a.browser_download_url)).ToArray();
            return list.FirstOrDefault(a => a.name != null && a.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                ?? list.FirstOrDefault();
        }

        private static GithubAssetsModel ResolveChecksumAsset(IEnumerable<GithubAssetsModel> assets, string packageFileName)
        {
            GithubAssetsModel[] list = assets.Where(a => a != null && !string.IsNullOrWhiteSpace(a.browser_download_url)).ToArray();
            if (list.Length == 0)
            {
                return null;
            }

            string checksumName = packageFileName + ".sha256";
            return list.FirstOrDefault(a => string.Equals(a.name, checksumName, StringComparison.OrdinalIgnoreCase))
                ?? list.FirstOrDefault(a => string.Equals(a.name, "update.zip.sha256", StringComparison.OrdinalIgnoreCase))
                ?? list.FirstOrDefault(a => string.Equals(a.name, "SHA256SUMS", StringComparison.OrdinalIgnoreCase))
                ?? list.FirstOrDefault(a => string.Equals(a.name, "sha256sums.txt", StringComparison.OrdinalIgnoreCase))
                ?? list.FirstOrDefault(a => a.name != null && a.name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 解析签名资产（minisign 风格命名：update.zip.sha256.sig）。
        /// </summary>
        private static GithubAssetsModel ResolveSignatureAsset(IEnumerable<GithubAssetsModel> assets, string packageFileName)
        {
            GithubAssetsModel[] list = assets.Where(a => a != null && !string.IsNullOrWhiteSpace(a.browser_download_url)).ToArray();
            if (list.Length == 0)
            {
                return null;
            }

            string signatureName = packageFileName + ".sha256.sig";
            return list.FirstOrDefault(a => string.Equals(a.name, signatureName, StringComparison.OrdinalIgnoreCase))
                ?? list.FirstOrDefault(a => string.Equals(a.name, "update.zip.sha256.sig", StringComparison.OrdinalIgnoreCase))
                ?? list.FirstOrDefault(a => a.name != null && a.name.EndsWith(".sha256.sig", StringComparison.OrdinalIgnoreCase))
                ?? list.FirstOrDefault(a => a.name != null && a.name.EndsWith(".sig", StringComparison.OrdinalIgnoreCase));
        }
    }
}
