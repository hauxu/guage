using Core.Models.Config;
using Core.Servicers.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using UI.Controls;
using UI.Controls.Base;
using UI.Models;
using UI.Servicers;
using UI.Views;

namespace UI.ViewModels
{
    /// <summary>
    /// 休息提醒页面 VM：工作/休息时长设置（表盘 + 小时步进）、两种模式、计时控制、提醒台账
    /// </summary>
    public class BreakReminderPageVM : ModelBase
    {
        private readonly IBreakReminderServicer _servicer;
        private readonly IAppConfig _appConfig;
        private readonly MainViewModel _mainVM;
        private readonly DispatcherTimer _uiTimer;

        // —— 设置（来自配置）——
        private bool _isWorkEnabled = true;
        private bool _isBreakEnabled = true;
        private int _workHours;
        private int _workMinutes = 25;
        private int _breakHours;
        private int _breakMinutes = 5;

        // —— 计时状态 ——
        private string _phaseText = "空闲";
        private string _remainingText = "--:--";
        private string _startButtonText = "开始计时";
        private double _progress;
        private string _phaseKey = "Idle";
        private IconTypes _startIcon = IconTypes.Play;
        private bool _isTiming;

        /// <summary>
        /// 小时步进上限（表盘 0-59 分钟 + 小时 0-12，共最长 12 小时 59 分）
        /// </summary>
        public const int MaxHours = 12;

        public ObservableCollection<BreakReminderRecordItemVM> Records { get; } = new ObservableCollection<BreakReminderRecordItemVM>();

        public BreakReminderPageVM(
            IBreakReminderServicer servicer,
            IAppConfig appConfig,
            MainViewModel mainVM)
        {
            _servicer = servicer;
            _appConfig = appConfig;
            _mainVM = mainVM;

            LoadConfig();

            StartPauseCommand = new Command(new Action<object>(OnStartPause));
            OpenRingTossCommand = new Command(new Action<object>(OnOpenRingToss));
            ResetCommand = new Command(new Action<object>(OnReset));
            WorkHoursIncreaseCommand = new Command(new Action<object>(o => WorkHours = Math.Min(MaxHours, WorkHours + 1)));
            WorkHoursDecreaseCommand = new Command(new Action<object>(o => WorkHours = Math.Max(0, WorkHours - 1)));
            BreakHoursIncreaseCommand = new Command(new Action<object>(o => BreakHours = Math.Min(MaxHours, BreakHours + 1)));
            BreakHoursDecreaseCommand = new Command(new Action<object>(o => BreakHours = Math.Max(0, BreakHours - 1)));

            _servicer.PhaseChanged += Servicer_PhaseChanged;
            _servicer.RunningChanged += Servicer_RunningChanged;
            _servicer.PhaseCompleted += Servicer_PhaseCompleted;

            _uiTimer = new DispatcherTimer();
            _uiTimer.Interval = TimeSpan.FromMilliseconds(500);
            _uiTimer.Tick += UiTimer_Tick;

            UpdateState();
            LoadRecords();
        }

        /// <summary>
        /// 页面可见时启动 UI 刷新计时器（由页面 Loaded 调用）
        /// </summary>
        public void StartUiTimer()
        {
            if (_uiTimer != null && !_uiTimer.IsEnabled)
            {
                _uiTimer.Start();
            }
        }

        /// <summary>
        /// 页面隐藏时停止 UI 刷新计时器（由页面 Unloaded 调用）
        /// </summary>
        public void StopUiTimer()
        {
            if (_uiTimer != null && _uiTimer.IsEnabled)
            {
                _uiTimer.Stop();
            }
        }

        public override void Dispose()
        {
            _servicer.PhaseChanged -= Servicer_PhaseChanged;
            _servicer.RunningChanged -= Servicer_RunningChanged;
            _servicer.PhaseCompleted -= Servicer_PhaseCompleted;
            if (_uiTimer != null)
            {
                _uiTimer.Stop();
                _uiTimer.Tick -= UiTimer_Tick;
            }
            base.Dispose();
        }

        // ============ 命令 ============
        public Command StartPauseCommand { get; }
        public Command OpenRingTossCommand { get; }
        public Command ResetCommand { get; }
        public Command WorkHoursIncreaseCommand { get; }
        public Command WorkHoursDecreaseCommand { get; }
        public Command BreakHoursIncreaseCommand { get; }
        public Command BreakHoursDecreaseCommand { get; }

        // ============ 设置属性 ============
        public bool IsWorkEnabled
        {
            get { return _isWorkEnabled; }
            set
            {
                if (_isWorkEnabled != value)
                {
                    _isWorkEnabled = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ModeText));
                    OnConfigChanged();
                }
            }
        }

        public bool IsBreakEnabled
        {
            get { return _isBreakEnabled; }
            set
            {
                if (_isBreakEnabled != value)
                {
                    _isBreakEnabled = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ModeText));
                    OnConfigChanged();
                }
            }
        }

        /// <summary>
        /// 工作小时（0-12）
        /// </summary>
        public int WorkHours
        {
            get { return _workHours; }
            set
            {
                int v = Math.Max(0, Math.Min(MaxHours, value));
                if (_workHours != v)
                {
                    _workHours = v;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(WorkDurationText));
                    OnConfigChanged();
                }
            }
        }

        /// <summary>
        /// 工作表盘分钟（0-59）
        /// </summary>
        public int DialWorkValue
        {
            get { return _workMinutes; }
            set
            {
                int v = Math.Max(0, Math.Min(59, value));
                if (_workMinutes != v)
                {
                    _workMinutes = v;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(WorkDurationText));
                    OnConfigChanged();
                }
            }
        }

        /// <summary>
        /// 休息小时（0-12）
        /// </summary>
        public int BreakHours
        {
            get { return _breakHours; }
            set
            {
                int v = Math.Max(0, Math.Min(MaxHours, value));
                if (_breakHours != v)
                {
                    _breakHours = v;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BreakDurationText));
                    OnConfigChanged();
                }
            }
        }

        /// <summary>
        /// 休息表盘分钟（0-59）
        /// </summary>
        public int DialBreakValue
        {
            get { return _breakMinutes; }
            set
            {
                int v = Math.Max(0, Math.Min(59, value));
                if (_breakMinutes != v)
                {
                    _breakMinutes = v;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BreakDurationText));
                    OnConfigChanged();
                }
            }
        }

        public int TotalWorkMinutes
        {
            get { return WorkHours * 60 + _workMinutes; }
        }

        public int TotalBreakMinutes
        {
            get { return BreakHours * 60 + _breakMinutes; }
        }

        public string WorkDurationText
        {
            get { return FormatDuration(TotalWorkMinutes); }
        }

        public string BreakDurationText
        {
            get { return FormatDuration(TotalBreakMinutes); }
        }

        /// <summary>
        /// 模式说明文案
        /// </summary>
        public string ModeText
        {
            get
            {
                if (IsWorkEnabled && IsBreakEnabled)
                {
                    return "循环模式：工作一段时间后提醒休息，休息结束后自动回到工作，如此循环。";
                }
                if (IsWorkEnabled)
                {
                    return "仅提醒模式：只在工作结束后提醒一次，不进入休息阶段。";
                }
                if (IsBreakEnabled)
                {
                    return "仅休息模式：从休息阶段开始，休息结束后提醒一次。";
                }
                return "请至少勾选工作时长或休息时长。";
            }
        }

        // ============ 计时状态属性 ============
        public string PhaseText
        {
            get { return _phaseText; }
            set
            {
                if (value == _phaseText) return;
                _phaseText = value; OnPropertyChanged();
            }
        }

        public string RemainingText
        {
            get { return _remainingText; }
            set
            {
                if (value == _remainingText) return;
                _remainingText = value; OnPropertyChanged();
            }
        }

        public string StartButtonText
        {
            get { return _startButtonText; }
            set
            {
                if (value == _startButtonText) return;
                _startButtonText = value; OnPropertyChanged();
            }
        }

        /// <summary>
        /// 当前阶段剩余进度（0-1，用于环形进度）
        /// </summary>
        public double Progress
        {
            get { return _progress; }
            set
            {
                if (value.Equals(_progress)) return;
                _progress = value; OnPropertyChanged();
            }
        }

        /// <summary>
        /// 阶段键（Idle/Work/Break，用于 XAML 触发器切换配色）
        /// </summary>
        public string PhaseKey
        {
            get { return _phaseKey; }
            set
            {
                if (value == _phaseKey) return;
                _phaseKey = value; OnPropertyChanged();
            }
        }

        /// <summary>
        /// 开始/暂停按钮图标
        /// </summary>
        public IconTypes StartIcon
        {
            get { return _startIcon; }
            set
            {
                if (value == _startIcon) return;
                _startIcon = value; OnPropertyChanged();
            }
        }

        /// <summary>
        /// Running or Paused
        /// </summary>
        public bool IsTiming
        {
            get { return _isTiming; }
            set
            {
                if (value == _isTiming)
                {
                    return;
                }
                _isTiming = value; OnPropertyChanged();
            }
        }

        private bool _isPaused;
        /// <summary>
        /// 暂停中：页面据此让水波/呼吸进入"静滞"慢速状态
        /// </summary>
        public bool IsPaused
        {
            get { return _isPaused; }
            set
            {
                if (value == _isPaused)
                {
                    return;
                }
                _isPaused = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 阶段自然完成（区别于手动停止/暂停）：页面据此播放庆祝动效
        /// </summary>
        public event EventHandler PhaseCompletedCelebration;

        /// <summary>
        /// 点开始计时（点香）：页面据此播放"点香"点火动画
        /// </summary>
        public event EventHandler PhaseStartedIgnition;


        // ============ 逻辑 ============
        private void LoadConfig()
        {
            var config = _appConfig.GetConfig();
            if (config?.BreakReminder == null)
            {
                return;
            }

            var model = config.BreakReminder;
            _isWorkEnabled = model.IsWorkEnabled;
            _isBreakEnabled = model.IsBreakEnabled;
            _workMinutes = Math.Max(0, Math.Min(59, model.WorkMinutes % 60));
            _workHours = Math.Max(0, Math.Min(MaxHours, model.WorkMinutes / 60));
            _breakMinutes = Math.Max(0, Math.Min(59, model.BreakMinutes % 60));
            _breakHours = Math.Max(0, Math.Min(MaxHours, model.BreakMinutes / 60));
        }

        private void SaveConfig()
        {
            try
            {
                var config = _appConfig.GetConfig();
                if (config?.BreakReminder == null)
                {
                    return;
                }

                config.BreakReminder.IsWorkEnabled = IsWorkEnabled;
                config.BreakReminder.IsBreakEnabled = IsBreakEnabled;
                config.BreakReminder.WorkMinutes = TotalWorkMinutes;
                config.BreakReminder.BreakMinutes = TotalBreakMinutes;
                _appConfig.Save();
            }
            catch (Exception ex)
            {
                Core.Librarys.Logger.Error(ex.ToString());
            }
        }

        /// <summary>
        /// 设置变化：正在计时则停止（行为可预期），并保存配置
        /// </summary>
        private void OnConfigChanged()
        {
            if (_servicer.IsRunning || _servicer.IsPaused)
            {
                _servicer.Stop();
                _mainVM.Info("设置已更改，计时已停止，请重新开始");
            }
            SaveConfig();
        }

        /// <summary>
        /// 打开套圈水机弹窗设置时长（参数 "work" / "break"）。
        /// </summary>
        private void OnOpenRingToss(object obj)
        {
            bool isWork = string.Equals(obj?.ToString(), "work", StringComparison.OrdinalIgnoreCase);
            int initial = isWork ? TotalWorkMinutes : TotalBreakMinutes;
            var window = new UI.Controls.Window.RingTossWindow(isWork, initial, ApplyRingTossResult);

            // Owner 必须是已显示的主窗口（Application.MainWindow 可能指向从未显示的保活窗口）
            var app = System.Windows.Application.Current;
            if (app != null)
            {
                foreach (System.Windows.Window w in app.Windows)
                {
                    if (w is UI.MainWindow && w.IsVisible)
                    {
                        window.Owner = w;
                        break;
                    }
                }
            }

            window.ShowDialog();
        }

        /// <summary>
        /// 套圈水机结果回写：换算为小时与分钟。
        /// </summary>
        private void ApplyRingTossResult(bool isWork, int totalMinutes)
        {
            if (isWork)
            {
                WorkHours = totalMinutes / 60;
                DialWorkValue = totalMinutes % 60;
            }
            else
            {
                BreakHours = totalMinutes / 60;
                DialBreakValue = totalMinutes % 60;
            }
        }

        private void OnStartPause(object obj)
        {
            if (_servicer.IsPaused)
            {
                _servicer.Resume();
                return;
            }
            if (_servicer.IsRunning)
            {
                _servicer.Pause();
                return;
            }

            // 校验
            if (!IsWorkEnabled && !IsBreakEnabled)
            {
                _mainVM.Error("请至少勾选工作时长或休息时长");
                return;
            }
            if (IsWorkEnabled && TotalWorkMinutes <= 0)
            {
                _mainVM.Error("工作时长必须大于 0 分钟");
                return;
            }
            if (IsBreakEnabled && TotalBreakMinutes <= 0)
            {
                _mainVM.Error("休息时长必须大于 0 分钟");
                return;
            }

            try
            {
                _servicer.Start(TotalWorkMinutes, TotalBreakMinutes, IsWorkEnabled, IsBreakEnabled);
                SaveConfig();

                //  点香：通知页面播放点火动画
                PhaseStartedIgnition?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _mainVM.Error(ex.Message);
            }
        }

        private void OnReset(object obj)
        {
            if (_servicer.IsRunning || _servicer.IsPaused)
            {
                _servicer.Stop();
                _mainVM.Info("计时已重置");
            }
        }

        private void Servicer_PhaseChanged(BreakReminderPhase phase)
        {
            UpdateState();
        }

        private void Servicer_RunningChanged()
        {
            UpdateState();
        }

        private void Servicer_PhaseCompleted(BreakReminderPhase phase, bool sleepElapsed)
        {
            //  服务事件在线程池线程触发：庆祝信号驱动页面燃尽动画，必须切回 UI 线程——
            //  直通会在 PlayBurnout 访问视觉树时抛跨线程异常，并斩断其后的记录刷新
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                OnPhaseCompletedUI();
            }
            else
            {
                dispatcher.BeginInvoke(new Action(OnPhaseCompletedUI));
            }
        }

        private void OnPhaseCompletedUI()
        {
            //  阶段自然完成（非手动停止）：广播庆祝信号驱动页面大动效
            PhaseCompletedCelebration?.Invoke(this, EventArgs.Empty);
            LoadRecords();
        }

        private void UiTimer_Tick(object sender, EventArgs e)
        {
            RefreshRemaining();
        }

        private void UpdateState()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(UpdateState));
                return;
            }

            switch (_servicer.CurrentPhase)
            {
                case BreakReminderPhase.Work:
                    PhaseText = "工作中";
                    PhaseKey = "Work";
                    break;
                case BreakReminderPhase.Break:
                    PhaseText = "休息中";
                    PhaseKey = "Break";
                    break;
                default:
                    PhaseText = "空闲";
                    PhaseKey = "Idle";
                    break;
            }

            IsTiming = _servicer.IsRunning || _servicer.IsPaused;
            IsPaused = _servicer.IsPaused;

            if (_servicer.IsPaused)
            {
                StartButtonText = "继续";
                StartIcon = IconTypes.Play;
            }
            else if (_servicer.IsRunning)
            {
                StartButtonText = "暂停";
                StartIcon = IconTypes.Pause;
            }
            else
            {
                StartButtonText = "开始计时";
                StartIcon = IconTypes.Play;
            }

            RefreshRemaining();
            LoadRecords();
        }

        private void RefreshRemaining()
        {
            if (_servicer.IsRunning || _servicer.IsPaused)
            {
                var remaining = _servicer.Remaining;
                if (remaining.TotalHours >= 1)
                {
                    RemainingText = remaining.ToString(@"h\:mm\:ss");
                }
                else
                {
                    RemainingText = remaining.ToString(@"mm\:ss");
                }
            }
            else
            {
                RemainingText = "--:--";
            }

            double total = _servicer.PhaseDuration.TotalSeconds;
            Progress = total > 0 ? Math.Max(0, Math.Min(1, _servicer.Remaining.TotalSeconds / total)) : 0;
        }

        private void LoadRecords()
        {
            Records.Clear();
            var list = _servicer.GetRecords();
            foreach (var item in list.AsEnumerable().Reverse().Take(20))
            {
                Records.Add(new BreakReminderRecordItemVM(item));
            }
        }

        /// <summary>
        /// 格式化分钟为「X 小时 Y 分钟」/「X 小时」/「Y 分钟」
        /// </summary>
        public static string FormatDuration(int totalMinutes)
        {
            int h = totalMinutes / 60;
            int m = totalMinutes % 60;
            if (h > 0 && m > 0)
            {
                return $"{h} 小时 {m} 分钟";
            }
            if (h > 0)
            {
                return $"{h} 小时";
            }
            return $"{m} 分钟";
        }
    }

    /// <summary>
    /// 台账条目展示模型
    /// </summary>
    public class BreakReminderRecordItemVM : UINotifyPropertyChanged
    {
        public string TimeText { get; }
        public string PhaseText { get; }
        public string EventText { get; }
        public string Note { get; }

        public BreakReminderRecordItemVM(BreakReminderRecordModel model)
        {
            TimeText = model.Time.ToString("MM-dd HH:mm:ss");
            switch (model.Phase)
            {
                case "Work":
                    PhaseText = "工作";
                    break;
                case "Break":
                    PhaseText = "休息";
                    break;
                default:
                    PhaseText = "空闲";
                    break;
            }
            switch (model.Event)
            {
                case "Start":
                    EventText = "开始";
                    break;
                case "Complete":
                    EventText = "完成";
                    break;
                case "Finished":
                    EventText = "本轮结束";
                    break;
                case "Pause":
                    EventText = "暂停";
                    break;
                case "Resume":
                    EventText = "继续";
                    break;
                case "Reset":
                    EventText = "停止";
                    break;
                case "Sleep":
                    EventText = "休眠";
                    break;
                case "WakeElapsed":
                    EventText = "唤醒补提醒";
                    break;
                default:
                    EventText = model.Event;
                    break;
            }
            Note = model.Note;
        }
    }
}
