using Core.Servicers.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Threading;

namespace Core.Librarys
{
    public static class Logger
    {
        private const int threshold = 50;

        private static readonly object writeLock = new object();

        //  线程安全队列：Info/Warn/Error 会被 WinEvent 钩子、计时器、后台任务等多线程并发调用
        private static System.Collections.Concurrent.ConcurrentQueue<string> loggers = new System.Collections.Concurrent.ConcurrentQueue<string>();


        static Logger()
        {
            //  定时刷盘用 System.Timers.Timer：DispatcherTimer 会绑定到首个调用线程的
            //  Dispatcher，若 Logger 首次调用发生在无消息泵的后台线程，周期刷盘将永久失效
            var flushTimer = new System.Timers.Timer(5 * 60 * 1000);
            flushTimer.Elapsed += (s, e) => Save(true);
            flushTimer.AutoReset = true;
            flushTimer.Start();
        }

        private enum LogLevel
        {
            Info,
            Warn,
            Error,
        }

        public static void Info(string message, [CallerLineNumber] int callerLineNumber = -1, [CallerFilePath] string callerFilePath = null, [CallerMemberName] string callerMemberName = null)
        {
            message = message + "\r\nLine:" + callerLineNumber + ",File:" + callerFilePath + ",name:" + callerMemberName;
            message = Format(LogLevel.Info, message);

            loggers.Enqueue(message);
        }

        public static void Warn(string message, [CallerLineNumber] int callerLineNumber = -1, [CallerFilePath] string callerFilePath = null, [CallerMemberName] string callerMemberName = null)
        {
            message = message + "\r\nLine:" + callerLineNumber + ",File:" + callerFilePath + ",name:" + callerMemberName;
            message = Format(LogLevel.Warn, message);

            loggers.Enqueue(message);
        }
        public static void Error(string message, [CallerLineNumber] int callerLineNumber = -1, [CallerFilePath] string callerFilePath = null, [CallerMemberName] string callerMemberName = null)
        {
            message = message + "\r\nLine:" + callerLineNumber + ",File:" + callerFilePath + ",name:" + callerMemberName;
            message = Format(LogLevel.Error, message);

            loggers.Enqueue(message);
        }

        private static string Format(LogLevel logLevel, string message)
        {
            message = HandleMessage(message);
            string logText = $"[{logLevel}] {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}\r\n{message}\r\n------------------------\r\n\r\n";
            Debug.WriteLine(logText);
            return logText;
        }

        /// <summary>
        /// 将log写入文件
        /// </summary>
        /// <param name="isNow">是否强制立即写入</param>
        public static void Save(bool isNow = false)
        {
            if ((!isNow && loggers.Count < threshold) || loggers.Count == 0)
            {
                return;
            }

            try
            {
                var builder = new StringBuilder();
                string item;
                while (loggers.TryDequeue(out item))
                {
                    builder.Append(item);
                }

                Write(builder.ToString());
            }
            catch (Exception ec)
            {
                Error(ec.ToString());
            }
        }

        private static void Write(string log_)
        {
            try
            {
                lock (writeLock)
                {
                    string loggerName = Path.Combine(FileHelper.GetLogDirectory(),
                                 DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                    string dir = Path.GetDirectoryName(loggerName);
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    if (!File.Exists(loggerName))
                    {
                        List<string> clientInfo = new List<string>(5);

                        //  记录客户端信息
                        clientInfo.Add(FormatItem("Core Version", Assembly.GetExecutingAssembly().GetName().Version.ToString()));
                        clientInfo.Add(FormatItem("OS Name", SystemCommon.GetWindowsVersionName()));
                        clientInfo.Add(FormatItem("Computer Type", GetComputerType()));
                        clientInfo.Add(FormatItem("Screen", GetScreenSize()));
                        clientInfo.Add("\r\n++++++++++++++++++++++++++++++++++++++++++++++++++\r\n\r\n");

                        File.WriteAllText(loggerName, string.Join("\r\n", clientInfo), Encoding.UTF8);
                    }
                    File.AppendAllText(loggerName, log_, Encoding.UTF8);
                    _writeFailures = 0;
                }
            }
            catch (Exception ec)
            {
                //  日志目录不可写（磁盘满/ACL）：丢弃本批并计数，不再重新入队——
                //  旧的重入队写法会在目录持续不可写时无限增长内存
                _writeFailures++;
                if (_writeFailures == 1 || _writeFailures % 50 == 0)
                {
                    loggers.Enqueue($"[Warn] 日志写入失败（第 {_writeFailures} 次），本批已丢弃：{ec.Message}\r\n------------------------\r\n\r\n");
                }
            }
        }

        private static int _writeFailures;


        private static string GetScreenSize()
        {
            return SystemInformation.VirtualScreen.Width + "*" + SystemInformation.VirtualScreen.Height;
        }

        /// <summary>
        /// 获取电脑设备类型
        /// </summary>
        /// <returns>Desktop台式机，Laptop笔记本</returns>
        private static string GetComputerType()
        {
            if (SystemInformation.PowerStatus.BatteryChargeStatus == BatteryChargeStatus.NoSystemBattery)
            {
                return "Desktop";
            }
            else
            {
                return "Laptop";
            }
        }
        private static string FormatItem(string name, string text)
        {
            return $"{name}:{text}";
        }

        private static string HandleMessage(string message)
        {
            message = message.Replace("\\u", "\\\\u");
            return message;
        }
    }
}
