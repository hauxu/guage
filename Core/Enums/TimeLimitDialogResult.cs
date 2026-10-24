namespace Core.Enums
{
    /// <summary>
    /// 时长限制弹窗的用户选择结果
    /// </summary>
    public enum TimeLimitDialogResult
    {
        Confirmed = 0,
        Cancelled = 1,
        Kill = 2,
        Extend = 3,
        /// <summary>
        /// 受限应用进程在锁定期间被强制结束（如任务管理器）
        /// </summary>
        ProcessExited = 4
    }
}
