using System;
using System.Net;
using System.Net.Sockets;

namespace Core.Librarys.Security
{
    /// <summary>
    /// 出站网络请求安全校验，防止 SSRF 访问内网/本机。
    /// </summary>
    public static class NetworkSecurity
    {
        public static bool IsSafeRemoteUrl(string url)
        {
            if (!UrlSecurity.TryCreateSafeHttpUri(url, out Uri uri))
            {
                return false;
            }

            return IsSafeHost(uri.Host, uri.DnsSafeHost);
        }

        /// <summary>
        /// favicon 下载校验：公网 URL 直接放行；内网/本机 URL 仅在与页面同域时放行。
        /// </summary>
        public static bool IsSafeFaviconUrl(string iconUrl, string pageUrl)
        {
            if (!UrlSecurity.TryCreateSafeHttpUri(iconUrl, out Uri iconUri))
            {
                return false;
            }

            if (IsSafeRemoteUrl(iconUrl))
            {
                return true;
            }

            if (!UrlSecurity.TryCreateSafeHttpUri(pageUrl, out Uri pageUri))
            {
                return false;
            }

            return HostMatches(iconUri, pageUri);
        }

        /// <summary>
        /// 解析目标主机并校验为公网后，将请求绑定到校验过的 IP 上，
        /// 消除“校验时解析一次、连接时再解析一次”的 DNS 重绑定窗口。
        /// 返回 false 表示目标不安全，调用方应放弃请求。
        /// </summary>
        public static bool TryBindSafeRemote(HttpWebRequest request, Uri uri)
        {
            if (request == null || uri == null)
            {
                return false;
            }

            if (uri.HostNameType == UriHostNameType.IPv4 || uri.HostNameType == UriHostNameType.IPv6)
            {
                //  IP 字面量：直接校验，连接天然使用该 IP
                IPAddress literal;
                return IPAddress.TryParse(uri.Host, out literal) && IsPublicIpAddress(literal);
            }

            if (uri.HostNameType != UriHostNameType.Dns)
            {
                return false;
            }

            IPAddress[] addresses;
            try
            {
                addresses = Dns.GetHostAddresses(uri.DnsSafeHost);
            }
            catch
            {
                return false;
            }

            if (addresses == null || addresses.Length == 0)
            {
                return false;
            }

            foreach (IPAddress item in addresses)
            {
                if (!IsPublicIpAddress(item))
                {
                    return false;
                }
            }

            //  全部解析结果均为公网：把连接钉在第一个已校验的 IP 上
            IPAddress bindIp = addresses[0];
            request.ServicePoint.BindIPEndPointDelegate = (servicePoint, remoteEndPoint, retryCount) =>
                new IPEndPoint(bindIp, remoteEndPoint.Port);
            return true;
        }

        private static bool HostMatches(Uri left, Uri right)
        {
            return string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase)
                || string.Equals(left.DnsSafeHost, right.DnsSafeHost, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSafeHost(string host, string dnsSafeHost)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || dnsSafeHost.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (IPAddress.TryParse(host, out IPAddress address))
            {
                return IsPublicIpAddress(address);
            }

            try
            {
                IPAddress[] addresses = Dns.GetHostAddresses(host);
                if (addresses == null || addresses.Length == 0)
                {
                    return false;
                }

                foreach (IPAddress item in addresses)
                {
                    if (!IsPublicIpAddress(item))
                    {
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 判断 IP 是否可安全访问：拒绝回环、私网、链路本地、CGNAT、基准测试网段、
        /// 组播、保留段、IPv4-mapped IPv6 私网与 IPv6 ULA。
        /// </summary>
        public static bool IsPublicIpAddress(IPAddress address)
        {
            if (address == null)
            {
                return false;
            }

            if (IPAddress.IsLoopback(address))
            {
                return false;
            }

            //  归一化：IPv4-mapped IPv6（::ffff:a.b.c.d）先还原为 IPv4 判断
            IPAddress normalized = address.IsIPv4MappedToIPv6
                ? address.MapToIPv4()
                : address;

            if (normalized.AddressFamily == AddressFamily.InterNetwork)
            {
                return IsPublicIpv4(normalized);
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return IsPublicIpv6(address);
            }

            return false;
        }

        private static bool IsPublicIpv4(IPAddress address)
        {
            byte[] bytes = address.GetAddressBytes();
            if (bytes.Length != 4)
            {
                return false;
            }

            byte b0 = bytes[0];
            byte b1 = bytes[1];

            //  0.0.0.0/8（本网络）
            if (b0 == 0)
            {
                return false;
            }

            //  10.0.0.0/8 私网
            if (b0 == 10)
            {
                return false;
            }

            //  100.64.0.0/10 CGNAT
            if (b0 == 100 && b1 >= 64 && b1 <= 127)
            {
                return false;
            }

            //  127.0.0.0/8 回环
            if (b0 == 127)
            {
                return false;
            }

            //  169.254.0.0/16 链路本地
            if (b0 == 169 && b1 == 254)
            {
                return false;
            }

            //  172.16.0.0/12 私网
            if (b0 == 172 && b1 >= 16 && b1 <= 31)
            {
                return false;
            }

            //  192.0.0.0/24 IETF 协议保留
            if (b0 == 192 && b1 == 0)
            {
                return false;
            }

            //  192.0.2.0/24 TEST-NET-1
            if (b0 == 192 && b1 == 0 && bytes[2] == 2)
            {
                return false;
            }

            //  192.88.99.0/24 6to4 中继
            if (b0 == 192 && b1 == 88 && bytes[2] == 99)
            {
                return false;
            }

            //  192.168.0.0/16 私网
            if (b0 == 192 && b1 == 168)
            {
                return false;
            }

            //  198.18.0.0/15 基准测试
            if (b0 == 198 && (b1 == 18 || b1 == 19))
            {
                return false;
            }

            //  198.51.100.0/24 TEST-NET-2、203.0.113.0/24 TEST-NET-3
            if ((b0 == 198 && b1 == 51 && bytes[2] == 100)
                || (b0 == 203 && b1 == 0 && bytes[2] == 113))
            {
                return false;
            }

            //  224.0.0.0/4 组播与保留
            if (b0 >= 224)
            {
                return false;
            }

            return true;
        }

        private static bool IsPublicIpv6(IPAddress address)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal)
            {
                return false;
            }

            byte[] bytes = address.GetAddressBytes();
            if (bytes.Length != 16)
            {
                return false;
            }

            //  ::/128 未指定
            bool isUnspecified = true;
            for (int i = 0; i < 16; i++)
            {
                if (bytes[i] != 0)
                {
                    isUnspecified = false;
                    break;
                }
            }
            if (isUnspecified)
            {
                return false;
            }

            //  fc00::/7 ULA
            if ((bytes[0] & 0xFE) == 0xFC)
            {
                return false;
            }

            //  fe80::/10 链路本地
            if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80)
            {
                return false;
            }

            //  组播 ff00::/8
            if (bytes[0] == 0xFF)
            {
                return false;
            }

            return true;
        }
    }
}
