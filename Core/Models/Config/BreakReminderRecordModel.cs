using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Models.Config
{
    /// <summary>
    /// 休息提醒记录（台账条目），持久化到 Data\BreakReminderRecords.json
    /// </summary>
    public class BreakReminderRecordModel
    {
        /// <summary>
        /// 事件发生时间
        /// </summary>
        public DateTime Time { get; set; }

        /// <summary>
        /// 事件发生时所在阶段：Idle / Work / Break
        /// </summary>
        public string Phase { get; set; }

        /// <summary>
        /// 事件类型：Start 开始 / Complete 阶段完成 / Finished 本轮结束 / Pause 暂停 / Resume 继续 / Reset 停止 / Sleep 进入休眠 / WakeElapsed 唤醒时已到期
        /// </summary>
        public string Event { get; set; }

        /// <summary>
        /// 该阶段设定时长（分钟）
        /// </summary>
        public int DurationMinutes { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        public string Note { get; set; }
    }
}
