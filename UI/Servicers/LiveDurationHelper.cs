using Core.Event;
using Core.Librarys;
using Core.Models;
using Core.Models.Db;
using System;
using System.Collections.Generic;
using UI.Controls.Charts.Model;

namespace UI.Servicers
{
    internal static class LiveDurationHelper
    {
        public static void ApplyActiveAppDuration(IEnumerable<ChartsDataModel> items, AppDurationUpdatedEventArgs live)
        {
            if (items == null)
            {
                return;
            }

            foreach (var item in items)
            {
                if (!(item?.Data is DailyLogModel log) || log.AppModel == null)
                {
                    continue;
                }

                var seconds = log.Time;
                if (live != null
                    && !string.IsNullOrEmpty(live.App?.Process)
                    && string.Equals(log.AppModel.Name, live.App.Process, StringComparison.OrdinalIgnoreCase))
                {
                    seconds += live.Duration;
                }

                UpdateItemDuration(item, seconds);
            }
        }

        public static void ApplyCompletedAppSession(IEnumerable<ChartsDataModel> items, AppDurationUpdatedEventArgs completed)
        {
            if (items == null || completed?.App == null || string.IsNullOrEmpty(completed.App.Process))
            {
                return;
            }

            foreach (var item in items)
            {
                if (!(item?.Data is DailyLogModel log) || log.AppModel == null)
                {
                    continue;
                }

                if (!string.Equals(log.AppModel.Name, completed.App.Process, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                log.Time += completed.Duration;
                UpdateItemDuration(item, log.Time);
            }
        }

        public static void ApplyWebBrowseDuration(IEnumerable<ChartsDataModel> items, string domain, int duration)
        {
            if (items == null || string.IsNullOrEmpty(domain) || duration <= 0)
            {
                return;
            }

            foreach (var item in items)
            {
                if (!(item?.Data is WebSiteModel site))
                {
                    continue;
                }

                if (!string.Equals(site.Domain, domain, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                site.Duration += duration;
                UpdateItemDuration(item, site.Duration);
            }
        }

        private static void UpdateItemDuration(ChartsDataModel item, int seconds)
        {
            var value = (double)seconds;
            var tag = Time.ToString(seconds);

            if (Math.Abs(item.Value - value) > 0.5)
            {
                item.Value = value;
            }

            if (item.Tag != tag)
            {
                item.Tag = tag;
            }
        }
    }
}
