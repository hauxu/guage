using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Models.Config
{
    /// <summary>
    /// 休息提醒（番茄钟）设置
    /// </summary>
    public class BreakReminderModel
    {
        /// <summary>
        /// 是否启用工作时长（勾选工作时长复选框）
        /// </summary>
        public bool IsWorkEnabled { get; set; } = true;

        /// <summary>
        /// 是否启用休息时长（勾选休息时长复选框）
        /// </summary>
        public bool IsBreakEnabled { get; set; } = true;

        /// <summary>
        /// 工作时长（分钟），合法范围 1 - 720（12 小时）
        /// </summary>
        public int WorkMinutes { get; set; } = 25;

        /// <summary>
        /// 休息时长（分钟），合法范围 1 - 720（12 小时）
        /// </summary>
        public int BreakMinutes { get; set; } = 5;
    }
}
