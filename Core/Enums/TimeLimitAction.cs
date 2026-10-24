namespace Core.Enums
{
    /// <summary>
    /// 达到时长上限后的处理方式
    /// </summary>
    public enum TimeLimitAction
    {
        /// <summary>
        /// 仅提醒
        /// </summary>
        Warn = 0,
        /// <summary>
        /// 倒计时后结束进程
        /// </summary>
        CountdownKill = 1,
        /// <summary>
        /// 全屏锁定遮罩
        /// </summary>
        LockOverlay = 2
    }
}
