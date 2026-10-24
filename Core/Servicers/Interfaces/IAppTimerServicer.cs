using Core.Event;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Servicers.Interfaces
{
    public interface IAppTimerServicer
    {
        /// <summary>
        /// 当有应用的计时更新时发生
        /// </summary>
        event AppTimerEventHandler OnAppDurationUpdated;

        /// <summary>
        /// 启动计时服务
        /// </summary>
        void Start();
        /// <summary>
        /// 停止计时服务
        /// </summary>
        void Stop();
        /// <summary>
        /// 获取当前应用的计时信息
        /// </summary>
        /// <returns></returns>
        AppDurationUpdatedEventArgs GetAppDuration();

        /// <summary>
        /// 结算当前会话（退出前调用）：停表并强制提交一次当前时长，绕过重复去重
        /// </summary>
        AppDurationUpdatedEventArgs Flush();
    }
}
