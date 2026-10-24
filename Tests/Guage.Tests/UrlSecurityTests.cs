using Core.Librarys.Security;
using System;

namespace Guage.Tests
{
    public static class UrlSecurityTests
    {
        public static void RunAll()
        {
            Run("URL-http/https 放行", TestHttpAllowed);
            Run("URL-危险协议拒绝", TestDangerousRejected);
            Run("URL-无协议自动补 http", TestSchemeAutoComplete);
            Run("URL-空白与非法拒绝", TestInvalidRejected);
        }

        private static void TestHttpAllowed()
        {
            Assert.IsTrue(UrlSecurity.TryCreateSafeHttpUri("http://example.com", out _));
            Assert.IsTrue(UrlSecurity.TryCreateSafeHttpUri("https://example.com/path?q=1", out _));
        }

        private static void TestDangerousRejected()
        {
            Assert.IsFalse(UrlSecurity.TryCreateSafeHttpUri("javascript:alert(1)", out _));
            Assert.IsFalse(UrlSecurity.TryCreateSafeHttpUri("file:///C:/Windows/win.ini", out _));
            Assert.IsFalse(UrlSecurity.TryCreateSafeHttpUri("data:text/html,x", out _));
            Assert.IsFalse(UrlSecurity.TryCreateSafeHttpUri("chrome://settings", out _));
            Assert.IsFalse(UrlSecurity.TryCreateSafeHttpUri("ftp://example.com", out _));
        }

        private static void TestSchemeAutoComplete()
        {
            Assert.IsTrue(UrlSecurity.TryCreateSafeHttpUri("example.com", out Uri uri));
            Assert.AreEqual("http://example.com/", uri.AbsoluteUri);
        }

        private static void TestInvalidRejected()
        {
            Assert.IsFalse(UrlSecurity.TryCreateSafeHttpUri(null, out _));
            Assert.IsFalse(UrlSecurity.TryCreateSafeHttpUri("   ", out _));
        }

        private static void Run(string name, Action test) => Program.Run(name, test);
    }
}
