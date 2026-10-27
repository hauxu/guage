using System;
using System.IO;
using System.Text;

namespace Core.Librarys
{
    /// <summary>
    /// 时长限制事件日志，写入 Log/TimeLimit-yyyy-MM-dd.log
    /// </summary>
    public static class TimeLimitLogger
    {
        private static readonly object WriteLock = new object();

        public static void LogTriggered(string appName, string processName, int limitMinutes, string action)
        {
            Write($"[触发] {appName} ({processName}) 已达 {limitMinutes} 分钟上限，处理方式：{action}");
        }

        public static void LogExtension(string appName, string processName, int minutes)
        {
            Write($"[延长] {appName} ({processName}) 临时延长 {minutes} 分钟");
        }

        public static void LogKill(string appName, string processName, int pid)
        {
            Write($"[关闭] {appName} ({processName}) 进程已结束，PID={pid}");
        }

        /// <summary>
        /// 诊断行：即时落盘，便于出问题时直接看现场（心跳、睡眠状态、前台应用）。
        /// </summary>
        public static void LogDiag(string message)
        {
            Write("[诊断] " + message);
        }

        private static void Write(string message)
        {
            try
            {
                lock (WriteLock)
                {
                    string dir = FileHelper.GetLogDirectory();
                    Directory.CreateDirectory(dir);
                    string path = Path.Combine(dir, "TimeLimit-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                    string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
                    File.AppendAllText(path, line, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("写入时长限制日志失败：" + ex.Message);
            }
        }
    }
}
