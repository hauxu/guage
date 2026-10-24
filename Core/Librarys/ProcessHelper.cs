using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Librarys
{
    public class ProcessHelper
    {
        public static bool Run(string filename, string[] args)
        {
            try
            {
                //  参数逐项加引号并转义内部引号，避免含空格/引号的参数被错误拆分
                string arguments = string.Join(" ",
                    (args ?? new string[0]).Select(a => "\"" + (a ?? string.Empty).Replace("\"", "\\\"") + "\""));
                Process process = new Process();
                ProcessStartInfo startInfo = new ProcessStartInfo(filename, arguments);
                process.StartInfo = startInfo;
                process.Start();
                //  Start 之后即可释放本进程对象，子进程继续独立运行
                process.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex.ToString());
                return false;
            }
        }

        public static bool TryKillProcess(int pid)
        {
            if (pid <= 0)
            {
                return false;
            }

            try
            {
                using (var process = Process.GetProcessById(pid))
                {
                    process.Kill();
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex.ToString());
                return false;
            }
        }
    }
}
