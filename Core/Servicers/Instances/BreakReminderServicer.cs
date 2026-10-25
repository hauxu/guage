using Core.Enums;
using Core.Librarys;
using Core.Models.Config;
using Core.Servicers.Interfaces;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace Core.Servicers.Instances
{
    /// <summary>
    /// 休息提醒（番茄钟）服务实现
    /// 计时方式：1 秒 System.Timers.Timer + 墙钟目标时间（DateTime），休眠期间倒计时按墙钟继续走，
    /// 唤醒后发现阶段已到期则在下一个 tick 立即完成并标记「休眠期间到期」。
    /// 提醒记录（台账）持久化到 Data\BreakReminderRecords.json，最多保留 200 条。
    /// </summary>
    public class BreakReminderServicer : IBreakReminderServicer
    {
        private readonly ISleepdiscover _sleepdiscover;
        private readonly object _syncRoot = new object();
        private readonly System.Timers.Timer _timer;
        private readonly List<BreakReminderRecordModel> _records = new List<BreakReminderRecordModel>();
        //  存储目录可以在设置里改到别处，所以路径必须每次现算，
        //  否则换目录后新记的台账还写进旧目录，前后分裂成两份
        private string RecordsPath
        {
            get { return Path.Combine(FileHelper.GetDataDirectory(), "BreakReminderRecords.json"); }
        }

        private BreakReminderPhase _currentPhase = BreakReminderPhase.Idle;
        private bool _isRunning;
        private bool _isPaused;
        private bool _isSleeping;
        private bool _wakeElapsedPending;
        private DateTime _phaseEndTime;
        private TimeSpan _remaining = TimeSpan.Zero;
        private int _workMinutes = 25;
        private int _breakMinutes = 5;
        private bool _workEnabled = true;
        private bool _breakEnabled = true;

        /// <summary>
        /// 记录台账最大条数
        /// </summary>
        private const int MaxRecordCount = 200;

        /// <summary>
        /// 单阶段最大时长（分钟）：与 UI 上限一致（12 小时 59 分）
        /// </summary>
        private const int MaxMinutes = 779;

        public event Action<BreakReminderPhase> PhaseChanged;
        public event Action RunningChanged;
        public event Action<BreakReminderPhase, bool> PhaseCompleted;

        public BreakReminderPhase CurrentPhase
        {
            get { lock (_syncRoot) { return _currentPhase; } }
        }

        public bool IsRunning
        {
            get { lock (_syncRoot) { return _isRunning && !_isPaused; } }
        }

        public bool IsPaused
        {
            get { lock (_syncRoot) { return _isPaused; } }
        }

        public TimeSpan Remaining
        {
            get
            {
                lock (_syncRoot)
                {
                    if (!_isRunning || _isPaused)
                    {
                        return _remaining;
                    }
                    TimeSpan r = _phaseEndTime - DateTime.Now;
                    return r < TimeSpan.Zero ? TimeSpan.Zero : r;
                }
            }
        }

        public TimeSpan PhaseDuration
        {
            get { lock (_syncRoot) { return TimeSpan.FromMinutes(GetPhaseMinutes(_currentPhase)); } }
        }

        public BreakReminderServicer(ISleepdiscover sleepdiscover)
        {
            _sleepdiscover = sleepdiscover;
            _timer = new System.Timers.Timer(1000);
            _timer.Elapsed += Timer_Elapsed;
            _timer.AutoReset = true;

            LoadRecords();

            if (_sleepdiscover != null)
            {
                _sleepdiscover.SleepStatusChanged += Sleepdiscover_SleepStatusChanged;
            }
        }

        private void Sleepdiscover_SleepStatusChanged(SleepStatus status)
        {
            lock (_syncRoot)
            {
                bool isSleep = status == SleepStatus.Sleep;
                _isSleeping = isSleep;

                if (isSleep)
                {
                    if (_isRunning && !_isPaused)
                    {
                        AddRecord(_currentPhase, "Sleep", GetPhaseMinutes(_currentPhase), "系统休眠，倒计时按墙钟继续走");
                    }
                }
                else
                {
                    // 唤醒：若阶段已过期，标记为休眠期间到期，下一个 tick 立即完成
                    if (_isRunning && !_isPaused && DateTime.Now >= _phaseEndTime)
                    {
                        _wakeElapsedPending = true;
                        AddRecord(_currentPhase, "WakeElapsed", GetPhaseMinutes(_currentPhase), "唤醒时阶段已到期");
                    }
                }
            }
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            List<Action> pending = null;
            lock (_syncRoot)
            {
                if (!_isRunning || _isPaused)
                {
                    return;
                }

                if (_isSleeping)
                {
                    // 兜底：若休眠事件丢失导致 _isSleeping 卡死，超过目标时间 5 分钟后按到期处理
                    if (DateTime.Now - _phaseEndTime > TimeSpan.FromMinutes(5))
                    {
                        _isSleeping = false;
                    }
                    else
                    {
                        return;
                    }
                }

                if (DateTime.Now >= _phaseEndTime)
                {
                    pending = CompletePhase();
                }
            }

            //  事件在锁外触发：订阅方可能回调本服务方法，锁内 Invoke 会死锁
            if (pending != null)
            {
                foreach (var action in pending)
                {
                    action();
                }
            }
        }

        private List<Action> CompletePhase()
        {
            //  调用方必须持有 _syncRoot 锁；事件收集到列表由调用方在锁外触发
            BreakReminderPhase completed = _currentPhase;
            bool sleepElapsed = _wakeElapsedPending;
            _wakeElapsedPending = false;

            AddRecord(completed, "Complete", GetPhaseMinutes(completed), sleepElapsed ? "休眠期间到期" : "阶段结束");

            var pending = new List<Action>();

            // 决定下一阶段：工作完进休息、休息完回工作；未启用的阶段不进入
            if (completed == BreakReminderPhase.Work && _breakEnabled)
            {
                SetPhase(BreakReminderPhase.Break, _breakMinutes);
            }
            else if (completed == BreakReminderPhase.Break && _workEnabled)
            {
                SetPhase(BreakReminderPhase.Work, _workMinutes);
            }
            else
            {
                // 仅提醒模式：本轮结束，回到空闲
                _isRunning = false;
                _isPaused = false;
                _remaining = TimeSpan.Zero;
                StopTimer();
                _currentPhase = BreakReminderPhase.Idle;
                AddRecord(completed, "Finished", GetPhaseMinutes(completed), "本轮结束（仅提醒模式）");
                pending.Add(() => RunningChanged?.Invoke());
                pending.Add(() => PhaseChanged?.Invoke(BreakReminderPhase.Idle));
            }

            if (_isRunning)
            {
                //  循环切换进入新阶段
                pending.Add(() => PhaseChanged?.Invoke(_currentPhase));
            }

            pending.Add(() => PhaseCompleted?.Invoke(completed, sleepElapsed));
            return pending;
        }

        public void Start(int workMinutes, int breakMinutes, bool workEnabled, bool breakEnabled)
        {
            BreakReminderPhase startPhase;
            lock (_syncRoot)
            {
                // 非法时长兜底：小于 1 分钟按 1 分钟处理，避免计时异常
                _workMinutes = Math.Max(1, Math.Min(MaxMinutes, workMinutes));
                _breakMinutes = Math.Max(1, Math.Min(MaxMinutes, breakMinutes));
                _workEnabled = workEnabled;
                _breakEnabled = breakEnabled;

                if (!_workEnabled && !_breakEnabled)
                {
                    throw new InvalidOperationException("至少需要勾选工作时长或休息时长");
                }

                StopTimer();
                _isRunning = false;
                _isPaused = false;
                _isSleeping = false;
                _wakeElapsedPending = false;

                // 仅勾选休息时长时从休息阶段开始；否则从工作阶段开始
                if (!_workEnabled && _breakEnabled)
                {
                    startPhase = BreakReminderPhase.Break;
                    SetPhase(startPhase, _breakMinutes);
                }
                else
                {
                    startPhase = BreakReminderPhase.Work;
                    SetPhase(startPhase, _workMinutes);
                }

                _isRunning = true;
                AddRecord(_currentPhase, "Start", GetPhaseMinutes(_currentPhase), "开始计时");
                StartTimer();
            }

            //  事件在锁外触发
            RunningChanged?.Invoke();
            PhaseChanged?.Invoke(startPhase);
        }

        private void SetPhase(BreakReminderPhase phase, int minutes)
        {
            //  仅更新状态；阶段变更事件由调用方在锁外触发
            _currentPhase = phase;
            _phaseEndTime = DateTime.Now.AddMinutes(minutes);
            _remaining = TimeSpan.FromMinutes(minutes);
        }

        private int GetPhaseMinutes(BreakReminderPhase phase)
        {
            return phase == BreakReminderPhase.Break ? _breakMinutes : _workMinutes;
        }

        public void Pause()
        {
            lock (_syncRoot)
            {
                if (!_isRunning || _isPaused)
                {
                    return;
                }

                _remaining = _phaseEndTime - DateTime.Now;
                if (_remaining < TimeSpan.Zero)
                {
                    _remaining = TimeSpan.Zero;
                }
                _isPaused = true;
                StopTimer();
                AddRecord(_currentPhase, "Pause", GetPhaseMinutes(_currentPhase), "暂停计时");
            }

            //  事件在锁外触发
            RunningChanged?.Invoke();
        }

        public void Resume()
        {
            lock (_syncRoot)
            {
                if (!_isRunning || !_isPaused)
                {
                    return;
                }

                _phaseEndTime = DateTime.Now.Add(_remaining);
                _isPaused = false;
                StartTimer();
                AddRecord(_currentPhase, "Resume", GetPhaseMinutes(_currentPhase), "继续计时");
            }

            //  事件在锁外触发
            RunningChanged?.Invoke();
        }

        public void Stop()
        {
            bool changed = false;
            lock (_syncRoot)
            {
                if (!_isRunning && _currentPhase == BreakReminderPhase.Idle)
                {
                    return;
                }

                AddRecord(_currentPhase, "Reset", GetPhaseMinutes(_currentPhase), "停止计时");
                StopTimer();
                _isRunning = false;
                _isPaused = false;
                _wakeElapsedPending = false;
                _currentPhase = BreakReminderPhase.Idle;
                _remaining = TimeSpan.Zero;
                changed = true;
            }

            //  事件在锁外触发
            if (changed)
            {
                RunningChanged?.Invoke();
                PhaseChanged?.Invoke(BreakReminderPhase.Idle);
            }
        }

        private void StartTimer()
        {
            try
            {
                if (!_timer.Enabled)
                {
                    _timer.Start();
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex.ToString());
            }
        }

        private void StopTimer()
        {
            try
            {
                if (_timer.Enabled)
                {
                    _timer.Stop();
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex.ToString());
            }
        }

        private void AddRecord(BreakReminderPhase phase, string eventName, int durationMinutes, string note)
        {
            try
            {
                _records.Add(new BreakReminderRecordModel
                {
                    Time = DateTime.Now,
                    Phase = phase.ToString(),
                    Event = eventName,
                    DurationMinutes = durationMinutes,
                    Note = note
                });

                while (_records.Count > MaxRecordCount)
                {
                    _records.RemoveAt(0);
                }

                SaveRecords();
            }
            catch (Exception ex)
            {
                Logger.Error(ex.ToString());
            }
        }

        private void SaveRecords()
        {
            try
            {
                FileHelper.EnsureStorageDirectories();

                //  原子写入：写盘中途被杀（崩溃/断电）时不至于损坏唯一一份台账，
                //  旧实现直写目标文件，损坏后 LoadRecords 静默清空、后续保存覆盖全丢
                string tempPath = RecordsPath + ".tmp";
                File.WriteAllText(tempPath, JsonConvert.SerializeObject(_records));
                try
                {
                    if (File.Exists(RecordsPath))
                    {
                        File.Replace(tempPath, RecordsPath, null);
                    }
                    else
                    {
                        File.Move(tempPath, RecordsPath);
                    }
                }
                catch
                {
                    try
                    {
                        if (File.Exists(tempPath))
                        {
                            File.Delete(tempPath);
                        }
                    }
                    catch
                    {
                        //  忽略清理失败
                    }
                    throw;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex.ToString());
            }
        }

        private void LoadRecords()
        {
            try
            {
                if (File.Exists(RecordsPath))
                {
                    var list = JsonConvert.DeserializeObject<List<BreakReminderRecordModel>>(File.ReadAllText(RecordsPath));
                    if (list != null)
                    {
                        _records.Clear();
                        _records.AddRange(list);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex.ToString());
            }
        }

        public List<BreakReminderRecordModel> GetRecords()
        {
            lock (_syncRoot)
            {
                return _records.ToList();
            }
        }
    }
}
