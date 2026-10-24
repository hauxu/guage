using Core.Models.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Servicers.Interfaces
{
    /// <summary>
    /// 休息提醒（番茄钟）阶段
    /// </summary>
    public enum BreakReminderPhase
    {
        /// <summary>
        /// 空闲（未计时）
        /// </summary>
        Idle = 0,
        /// <summary>
        /// 工作中
        /// </summary>
        Work = 1,
        /// <summary>
        /// 休息中
        /// </summary>
        Break = 2
    }

    /// <summary>
    /// 休息提醒（番茄钟）服务：工作/休息倒计时，支持「工作+休息循环」与「仅工作后提醒」两种模式
    /// </summary>
    public interface IBreakReminderServicer
    {
        /// <summary>
        /// 当前阶段
        /// </summary>
        BreakReminderPhase CurrentPhase { get; }

        /// <summary>
        /// 是否正在计时（运行中且未暂停）
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// 是否已暂停
        /// </summary>
        bool IsPaused { get; }

        /// <summary>
        /// 当前阶段剩余时间（未运行时返回 0）
        /// </summary>
        TimeSpan Remaining { get; }

        /// <summary>
        /// 当前阶段总时长（用于计算进度；未运行时返回 0）
        /// </summary>
        TimeSpan PhaseDuration { get; }

        /// <summary>
        /// 阶段变化事件（Idle/Work/Break）
        /// </summary>
        event Action<BreakReminderPhase> PhaseChanged;

        /// <summary>
        /// 运行状态变化事件（开始/暂停/继续/停止）
        /// </summary>
        event Action RunningChanged;

        /// <summary>
        /// 阶段完成事件（参数：完成的阶段、是否休眠期间到期）
        /// </summary>
        event Action<BreakReminderPhase, bool> PhaseCompleted;

        /// <summary>
        /// 开始计时
        /// </summary>
        /// <param name="workMinutes">工作时长（分钟）</param>
        /// <param name="breakMinutes">休息时长（分钟）</param>
        /// <param name="workEnabled">是否启用工作阶段</param>
        /// <param name="breakEnabled">是否启用休息阶段</param>
        void Start(int workMinutes, int breakMinutes, bool workEnabled, bool breakEnabled);

        /// <summary>
        /// 暂停计时
        /// </summary>
        void Pause();

        /// <summary>
        /// 继续计时
        /// </summary>
        void Resume();

        /// <summary>
        /// 停止并复位
        /// </summary>
        void Stop();

        /// <summary>
        /// 读取提醒记录（台账）
        /// </summary>
        List<BreakReminderRecordModel> GetRecords();
    }
}
