using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Core.Event;

namespace Core.Servicers.Interfaces
{
    /// <summary>
    /// 主要服务
    /// </summary>
    public interface IMain
    {
        /// <summary>
        /// 运行服务（仅首次启动调用）
        /// </summary>
        void Run();
        /// <summary>
        /// 退出服务
        /// </summary>
        void Exit();
        /// <summary>
        /// 启动服务
        /// </summary>
        void Start();
        /// <summary>
        /// 停止服务
        /// </summary>
        void Stop();

        /// <summary>
        /// 退出前结算当前前台会话并等待在途写库完成（防数据丢失）
        /// </summary>
        void FlushCurrentSession();

        /// <summary>
        /// 更新某个进程时间时发生
        /// </summary>
        event EventHandler OnUpdateTime;
        /// <summary>
        /// 浏览器访问时长更新时发生
        /// </summary>
        event EventHandler<WebBrowseUpdatedEventArgs> OnWebBrowseUpdated;
        /// <summary>
        /// 初始化完成时发生
        /// </summary>
        event EventHandler OnStarted;
    }
}
