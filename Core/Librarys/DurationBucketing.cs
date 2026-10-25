using System;
using System.Collections.Generic;

namespace Core.Librarys
{
    /// <summary>
    /// 时长分桶纯逻辑：把一次会话时长按小时桶/天桶拆分（从 Data.UpdateAppDuration 抽取，便于单元测试）。
    /// 行为必须与历史实现完全一致：起始桶按真实剩余时间计算（含秒级修正）。
    /// </summary>
    public static class DurationBucketing
    {
        /// <summary>
        /// 按小时拆分。hours 的键为小时起点（DateTime 精确到小时）。
        /// </summary>
        public static void SplitHours(DateTime startTime, int duration,
            out Dictionary<DateTime, int> hours, out DateTime endTime)
        {
            //  开始时间剩余最大统计时长（到下一个整点的秒数）
            int startTimeMaxHoursDuration = (59 - startTime.Minute) * 60 + (60 - startTime.Second);
            //  开始时间使用时长
            int startTimeHoursDuration = duration > startTimeMaxHoursDuration ? startTimeMaxHoursDuration : duration;
            //  剩余时长
            int outHoursDuration = duration - startTimeHoursDuration;
            //  结束时间
            endTime = new DateTime(startTime.Year, startTime.Month, startTime.Day, startTime.Hour, 0, 0);
            //  时段使用数据
            hours = new Dictionary<DateTime, int>
            {
                { endTime, startTimeHoursDuration }
            };
            //  计算时段数据
            if (outHoursDuration > 0)
            {
                int outHours = outHoursDuration / 3600;

                DateTime outStartTime = new DateTime(startTime.Year, startTime.Month, startTime.Day, startTime.Hour, 0, 0);
                for (int i = 0; i < outHours; i++)
                {
                    outStartTime = outStartTime.AddHours(1);
                    int bucketDuration = 3600;
                    hours.Add(outStartTime, bucketDuration);
                }
                if (outHoursDuration % 3600 > 0)
                {
                    outStartTime = outStartTime.AddHours(1);
                    int bucketDuration = outHoursDuration - outHours * 3600;
                    hours.Add(outStartTime, bucketDuration);
                }
                endTime = outStartTime;
            }
        }

        /// <summary>
        /// 按天拆分。days 的键为日期（当天 00:00）。
        /// </summary>
        /// <summary>
        /// 按自然日切分时长。一天固定按 86400 秒处理，即假定所在时区没有夏令时；
        /// 有夏令时的地区在切换日会差 3600 秒（当前只面向无 DST 的区域，不额外处理）。
        /// </summary>
        public static void SplitDays(DateTime startTime, int duration,
            out Dictionary<DateTime, int> days, out DateTime endDayTime)
        {
            //  计算每日统计数据
            DateTime nextDayTime = new DateTime(startTime.Year, startTime.Month, startTime.Day, 0, 0, 0).AddDays(1);
            int startTimeMaxDayDuration = (int)(nextDayTime - startTime).TotalSeconds;
            //  开始时间使用时长
            int startTimeDayDuration = duration > startTimeMaxDayDuration ? startTimeMaxDayDuration : duration;
            //  剩余时长
            int outDayDuration = duration - startTimeDayDuration;
            //  结束时间
            endDayTime = startTime.Date;
            //  每日使用数据
            days = new Dictionary<DateTime, int>
            {
                { startTime.Date, startTimeDayDuration }
            };
            if (outDayDuration > 0)
            {
                int outDays = outDayDuration / 86400;

                DateTime outStartTime = new DateTime(startTime.Year, startTime.Month, startTime.Day, 0, 0, 0);
                for (int i = 0; i < outDays; i++)
                {
                    outStartTime = outStartTime.AddDays(1);
                    int bucketDuration = 86400;
                    days.Add(outStartTime, bucketDuration);
                }
                if (outDayDuration % 86400 > 0)
                {
                    outStartTime = outStartTime.AddDays(1);
                    int bucketDuration = outDayDuration - outDays * 86400;
                    days.Add(outStartTime, bucketDuration);
                }
                endDayTime = outStartTime.Date;
            }
        }
    }
}
