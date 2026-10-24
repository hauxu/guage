namespace UI.Base.Animation
{
    /// <summary>
    /// 动效档位：前台完整 → 前台降级 → 后台静默。
    /// </summary>
    public enum AnimationTier
    {
        /// <summary>
        /// 主窗口不可见或最小化：停止 UI 轮询与帧采样，尽量降低占用。
        /// </summary>
        Background = 0,

        /// <summary>
        /// 主窗口可见但系统/UI 负载较高：保留核心交互，简化过渡动画。
        /// </summary>
        Reduced = 1,

        /// <summary>
        /// 主窗口可见且负载正常：允许完整过渡与进场动效。
        /// </summary>
        Full = 2
    }
}
