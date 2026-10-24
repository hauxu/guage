using Core.Librarys.Security;
using System;
using System.Net;

namespace Guage.Tests
{
    public static class NetworkSecurityTests
    {
        public static void RunAll()
        {
            Run("IP-回环与私网全拒", TestPrivateRejected);
            Run("IP-公网放行", TestPublicAccepted);
            Run("IP-IPv6 特殊段拒绝", TestIpv6Rejected);
            Run("IP-IPv4-mapped 私网拒绝", TestMappedRejected);
        }

        private static void TestPrivateRejected()
        {
            Assert.IsFalse(NetworkSecurity.IsPublicIpAddress(IPAddress.Parse("127.0.0.1")));
            Assert.IsFalse(NetworkSecurity.IsPublicIpAddress(IPAddress.Parse("10.0.0.1")));
            Assert.IsFalse(NetworkSecurity.IsPublicIpAddress(IPAddress.Parse("192.168.1.1")));
            Assert.IsFalse(NetworkSecurity.IsPublicIpAddress(IPAddress.Parse("172.16.0.1")));
            Assert.IsFalse(NetworkSecurity.IsPublicIpAddress(IPAddress.Parse("172.31.255.255")));
            Assert.IsFalse(NetworkSecurity.IsPublicIpAddress(IPAddress.Parse("169.254.1.1")));
            Assert.IsFalse(NetworkSecurity.IsPublicIpAddress(IPAddress.Parse("0.0.0.0")));
            Assert.IsFalse(NetworkSecurity.IsPublicIpAddress(IPAddress.Parse("100.64.0.1")));
            Assert.IsFalse(NetworkSecurity.IsPublicIpAddress(IPAddress.Parse("198.18.0.1")));
            Assert.IsFalse(NetworkSecurity.IsPublicIpAddress(IPAddress.Parse("192.0.2.1")));
            Assert.IsFalse(NetworkSecurity.IsPublicIpAddress(IPAddress.Parse("198.51.100.7")));
            Assert.IsFalse(NetworkSecurity.IsPublicIpAddress(IPAddress.Parse("203.0.113.9")));
            Assert.IsFalse(NetworkSecurity.IsPublicIpAddress(IPAddress.Parse("224.0.0.1")), "组播拒绝");
            Assert.IsFalse(NetworkSecurity.IsPublicIpAddress(IPAddress.Parse("255.255.255.255")));
        }

        private static void TestPublicAccepted()
        {
            Assert.IsTrue(NetworkSecurity.IsPublicIpAddress(IPAddress.Parse("8.8.8.8")));
            Assert.IsTrue(NetworkSecurity.IsPublicIpAddress(IPAddress.Parse("1.1.1.1")));
            Assert.IsTrue(NetworkSecurity.IsPublicIpAddress(IPAddress.Parse("223.255.255.254")));
            Assert.IsTrue(NetworkSecurity.IsPublicIpAddress(IPAddress.Parse("172.32.0.1")), "172.32/16 是公网段");
            Assert.IsTrue(NetworkSecurity.IsPublicIpAddress(IPAddress.Parse("2606:4700:4700::1111")));
        }

        private static void TestIpv6Rejected()
        {
            Assert.IsFalse(NetworkSecurity.IsPublicIpAddress(IPAddress.Parse("::1")));
            Assert.IsFalse(NetworkSecurity.IsPublicIpAddress(IPAddress.Parse("::")));
            Assert.IsFalse(NetworkSecurity.IsPublicIpAddress(IPAddress.Parse("fe80::1")));
            Assert.IsFalse(NetworkSecurity.IsPublicIpAddress(IPAddress.Parse("fc00::1")), "ULA 拒绝");
            Assert.IsFalse(NetworkSecurity.IsPublicIpAddress(IPAddress.Parse("fd00::1")), "ULA 拒绝");
            Assert.IsFalse(NetworkSecurity.IsPublicIpAddress(IPAddress.Parse("ff02::1")), "组播拒绝");
            Assert.IsFalse(NetworkSecurity.IsPublicIpAddress(IPAddress.Parse("fec0::1")), "站点本地拒绝");
        }

        private static void TestMappedRejected()
        {
            Assert.IsFalse(NetworkSecurity.IsPublicIpAddress(IPAddress.Parse("::ffff:192.168.1.1")), "IPv4-mapped 私网拒绝");
            Assert.IsFalse(NetworkSecurity.IsPublicIpAddress(IPAddress.Parse("::ffff:127.0.0.1")));
            Assert.IsTrue(NetworkSecurity.IsPublicIpAddress(IPAddress.Parse("::ffff:8.8.8.8")), "IPv4-mapped 公网放行");
        }

        private static void Run(string name, Action test) => Program.Run(name, test);
    }
}
