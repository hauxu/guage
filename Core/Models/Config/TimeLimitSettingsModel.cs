using System.Collections.Generic;

namespace Core.Models.Config
{
    /// <summary>
    /// 时长限制设置
    /// </summary>
    public class TimeLimitSettingsModel
    {
        /// <summary>
        /// 是否启用时长限制
        /// </summary>
        [Config(Name = "启用时长限制", Description = "开启后,对规则中的应用执行时长限制", Group = "时长限制")]
        public bool IsEnabled { get; set; } = false;

        /// <summary>
        /// 限制规则列表
        /// </summary>
        public List<TimeLimitRuleModel> Rules { get; set; } = new List<TimeLimitRuleModel>();

        /// <summary>
        /// 随机生成的 6 位数字字母混合密码（查看一次后失效）
        /// </summary>
        public string RandomPassword { get; set; } = string.Empty;

        /// <summary>
        /// 随机密码是否已查看（查看后失效，无法再次查看）
        /// </summary>
                /// <summary>
        /// 当日已延长的分钟数（规则键 → 分钟）。只对 TodayExtensionsDate 那天有效
        /// </summary>
        public Dictionary<string, int> TodayExtensions { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// TodayExtensions 归属日期（yyyyMMdd），跨天即作废
        /// </summary>
        public int TodayExtensionsDate { get; set; }

public bool RandomPasswordViewed { get; set; } = false;
    }
}
