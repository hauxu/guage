namespace Core.Servicers.Interfaces
{
    public interface ITimeLimitServicer
    {
        void Start();
        void Stop();

        /// <summary>
        /// 临时延长指定应用的可用时长（分钟），并清除当日已触发标记。
        /// </summary>
        void GrantExtension(string processName, string executablePath, int minutes = 5);

        /// <summary>
        /// 重新同步应用时长：自动检查是否已进入新的一天（跨天重置），
        /// 并从数据库把今日各应用已用时长重新同步到内存，
        /// 保证时长限制功能在跨天/外部数据变化后正常工作。
        /// </summary>
        /// <summary>
        /// 重新加载配置（启用状态与规则列表），界面改动后立即生效。
        void Reload();
        void ResyncToday();
    }
}
