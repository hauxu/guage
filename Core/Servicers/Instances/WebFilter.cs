using Core.Librarys;
using Core.Librarys.Browser;
using Core.Servicers.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Core.Servicers.Instances
{
    public class WebFilter : IWebFilter
    {
        private readonly IAppConfig _appConfig;
        private readonly object _ignoreCacheLocker = new object();
        private List<string> _ignoreURLCache;
        private List<Regex> _compiledIgnoreRegexList = new List<Regex>();
        //  忽略域名快照：重建时整体替换引用（不可变快照），WS 线程与 UI 线程读写无竞态
        private volatile List<string> _domainIgnoreSnapshot = new List<string>();
        //  默认忽略的规则
        private readonly string[] _ignoreRegexArr = { "^chrome://(.*)", "^edge://(.*)", "^view-source:(.*)" };
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(500);

        public WebFilter(IAppConfig appConfig_)
        {
            _appConfig = appConfig_;
        }

        public void Init()
        {
            _ignoreURLCache = new List<string>();
            _appConfig.ConfigChanged += _appConfig_ConfigChanged;
            RebuildCompiledRegexList();
            RebuildDomainSnapshot();
        }

        private void _appConfig_ConfigChanged(Models.Config.ConfigModel oldConfig, Models.Config.ConfigModel newConfig)
        {
            if (oldConfig.Behavior.IgnoreURLList != newConfig.Behavior.IgnoreURLList)
            {
                lock (_ignoreCacheLocker)
                {
                    _ignoreURLCache.Clear();
                }

                RebuildCompiledRegexList();
                RebuildDomainSnapshot();
            }
        }

        /// <summary>
        /// 重建忽略域名快照（配置变更时调用；UI 修改忽略列表后 Save 会触发本方法）。
        /// </summary>
        private void RebuildDomainSnapshot()
        {
            var config = _appConfig.GetConfig();
            var ignoreURLList = config?.Behavior?.IgnoreURLList ?? new List<string>();
            _domainIgnoreSnapshot = ignoreURLList.Where(m => !Regex.IsMatch(m, @"[\*|\$|\!|\(|\)|\{|\\|\[|\^|\|]")).ToList();
        }

        private void RebuildCompiledRegexList()
        {
            _compiledIgnoreRegexList.Clear();
            var config = _appConfig.GetConfig();
            var ignoreURLList = config?.Behavior?.IgnoreURLList ?? new List<string>();

            var regexList = ignoreURLList
                .Where(m => Regex.IsMatch(m, @"[\*|\$|\!|\(|\)|\{|\\|\[|\^|\|]"))
                .Concat(_ignoreRegexArr)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (string pattern in regexList)
            {
                try
                {
                    _compiledIgnoreRegexList.Add(new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout));
                }
                catch (Exception ex)
                {
                    Logger.Error("忽略 URL 正则无效，已跳过：" + pattern + "。" + ex.Message);
                }
            }
        }

        public bool IsIgnore(string url_)
        {
            if (string.IsNullOrEmpty(url_))
            {
                return true;
            }

            lock (_ignoreCacheLocker)
            {
                if (_ignoreURLCache.Contains(url_))
                {
                    return true;
                }
            }

            if (IsDomainIgnore(url_))
            {
                lock (_ignoreCacheLocker)
                {
                    _ignoreURLCache.Add(url_);
                }

                return true;
            }

            if (IsRegexIgnore(url_))
            {
                lock (_ignoreCacheLocker)
                {
                    _ignoreURLCache.Add(url_);
                }

                return true;
            }

            return false;
        }

        public bool IsDomainIgnore(string url_)
        {
            url_ = UrlHelper.GetDomain(url_);

            //  读不可变快照：WS 线程安全，不与 UI 线程的原地修改产生 List 结构竞态
            return _domainIgnoreSnapshot.Contains(url_);
        }

        public bool IsRegexIgnore(string url_)
        {
            foreach (Regex regex in _compiledIgnoreRegexList)
            {
                try
                {
                    if (regex.IsMatch(url_))
                    {
                        return true;
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    Logger.Error("忽略 URL 正则匹配超时，规则未生效：" + regex);
                }
            }

            return false;
        }
    }
}
