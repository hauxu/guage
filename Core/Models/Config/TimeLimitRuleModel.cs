namespace Core.Models.Config
{
    /// <summary>
    /// 时长限制规则
    /// </summary>
    public class TimeLimitRuleModel
    {
        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; } = "未命名";
        /// <summary>
        /// 进程名（不含 .exe）
        /// </summary>
        public string ProcessName { get; set; } = string.Empty;
        /// <summary>
        /// 可执行文件完整路径,用于精确匹配
        /// </summary>
        public string ExecutablePath { get; set; } = string.Empty;
        /// <summary>
        /// 每日限制时长(分钟)
        /// </summary>
        public int LimitMinutes { get; set; } = 60;
        /// <summary>
        /// 超限动作,见 <see cref="Enums.TimeLimitAction"/>
        /// </summary>
        public int Action { get; set; } = 0;
        /// <summary>
        /// 倒计时秒数
        /// </summary>
        public int CountdownSeconds { get; set; } = 60;
        /// <summary>
        /// 是否启用
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 仅提示模式：是否使用 Windows 原生消息弹窗
        /// </summary>
        public bool UseNativeMessageBox { get; set; } = false;

        /// <summary>
        /// 倒计时关闭：是否使用右上角置顶半透明悬浮倒计时
        /// </summary>
        public bool ShowFloatingCountdown { get; set; } = true;

        /// <summary>
        /// 悬浮倒计时透明度（0.1-0.95）
        /// </summary>
        public double CountdownOpacity { get; set; } = 0.5;

        /// <summary>
        /// 悬浮倒计时：三击隐藏倒计时窗
        /// （仅作用于倒计时窗；锁定遮罩不提供隐藏入口，安全优先）
        /// </summary>
        public bool HideByTripleClick { get; set; } = true;

        /// <summary>
        /// 倒计时结束：禁止该进程在当前会话内再次启动
        /// </summary>
        public bool BanRestart { get; set; } = true;

        /// <summary>
        /// 锁定遮罩：密码方式（0=自定义 1=随机 6 位数字字母混合）
        /// </summary>
        public int PasswordMode { get; set; } = 1;

        /// <summary>
        /// 锁定遮罩：自定义密码 SHA256 哈希
        /// </summary>
        public string CustomPasswordHash { get; set; } = string.Empty;

        /// <summary>
        /// 锁定遮罩：解锁后再次锁定分钟数（默认 10，0=不再次锁定）
        /// </summary>
        public int RelockMinutes { get; set; } = 10;

        /// <summary>
        /// 锁定遮罩：锁定期间退出进程再打开时立即重新锁定
        /// </summary>
        public bool RelockAfterExit { get; set; } = true;
    }
}
