using Core.Enums;
using System.Threading.Tasks;

namespace Core.Servicers.Interfaces
{
    /// <summary>
    /// 时长限制 UI 通知接口，由 UI 层实现
    /// </summary>
    public interface ITimeLimitNotifier
    {
        /// <summary>
        /// 仅提示
        /// </summary>
        /// <param name="notifyStyle">0=应用内 Toast 1=Windows 原生消息弹窗</param>
        void ShowWarning(string title, string message, int notifyStyle = 0);

        /// <summary>
        /// 倒计时关闭：返回 Kill 表示结束进程，Cancelled 表示取消，Extend 表示延长
        /// </summary>
        Task<TimeLimitDialogResult> ShowCountdownAsync(
            string title,
            string message,
            int seconds,
            string processName,
            string executablePath,
            bool floatingTop = true,
            double opacity = 0.6,
            bool hideByTripleClick = true);

        /// <summary>
        /// 锁定遮罩：需输入正确密码才可解锁
        /// </summary>
        Task<TimeLimitDialogResult> ShowLockOverlayAsync(
            string title,
            string message,
            string processName,
            string executablePath,
            int passwordMode = 1,
            string customPasswordHash = "",
            string randomPassword = "",
            bool randomPasswordViewed = false,
            int relockMinutes = 10);
    }
}
