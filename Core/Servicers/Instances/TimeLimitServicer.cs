using Core.Enums;
using Core.Event;
using Core.Librarys;
using Core.Models.AppObserver;
using Core.Models.Config;
using Core.Servicers.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;

namespace Core.Servicers.Instances
{
    public class TimeLimitServicer : ITimeLimitServicer
    {
        private readonly IAppObserver _appObserver;
        private readonly IAppConfig _appConfig;
        private readonly ITimeLimitNotifier _notifier;
        private readonly ISleepdiscover _sleepdiscover;

        private readonly object _syncRoot = new object();
        private Timer _timer;
        private bool _isStarted;
        private bool _isSleeping;

        //  正在弹出遮罩/倒计时的规则（按规则互斥）：
        //  旧的全局布尔会让一个未应答的遮罩冻结所有规则的计数与触发
        private readonly HashSet<string> _actingRules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private AppInfo _activeApp = AppInfo.Empty;
        private DateTime _trackDate = DateTime.Today;
        private readonly Dictionary<string, int> _todayUsageSeconds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _triggeredToday = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 会话级禁止重启：进程名 → 归一化可执行文件路径（空串 = 无路径信息，按进程名整杀）
        private readonly Dictionary<string, string> _banRestartSet = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 当前处于锁定遮罩状态的规则键（重启立即锁定）
        private readonly HashSet<string> _lockedRules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 解锁宽限期：ruleKey → 宽限截止时间（密码解锁成功后 RelockMinutes 分钟内不重锁）
        private readonly Dictionary<string, DateTime> _unlockGraceUntil = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        //  当日已补触发过的规则（重启恢复后的补执行只做一次，避免每秒重复弹窗）
        private readonly HashSet<string> _reArmedToday = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private List<TimeLimitRuleModel> _rules = new List<TimeLimitRuleModel>();
        private bool _isEnabled;

        private readonly IData _data;

        public TimeLimitServicer(
            IAppObserver appObserver,
            IAppConfig appConfig,
            ITimeLimitNotifier notifier,
            ISleepdiscover sleepdiscover,
            IData data)
        {
            _appObserver = appObserver;
            _appConfig = appConfig;
            _notifier = notifier;
            _sleepdiscover = sleepdiscover;
            _data = data;

            _appConfig.ConfigChanged += AppConfig_ConfigChanged;
            _sleepdiscover.SleepStatusChanged += Sleepdiscover_SleepStatusChanged;
        }

        private void AppConfig_ConfigChanged(ConfigModel oldConfig, ConfigModel newConfig)
        {
            LoadSettings();
        }

        private void Sleepdiscover_SleepStatusChanged(SleepStatus status)
        {
            _isSleeping = status == SleepStatus.Sleep;
        }

        public void Start()
        {
            if (_isStarted)
            {
                return;
            }

            LoadSettings();

            //  从数据库恢复今日已用时长与触发状态，防止重启软件重置计时绕过限制
            RestoreTodayUsage();

            //  同 AppTimerServicer：睡眠/唤醒会重复 Start，先释放上一个实例
            if (_timer != null)
            {
                _timer.Elapsed -= Timer_Elapsed;
                _timer.Dispose();
            }

            _timer = new Timer(1000);
            _timer.Elapsed += Timer_Elapsed;
            _timer.AutoReset = true;
            _timer.Start();

            _appObserver.OnAppActiveChanged += AppObserver_OnAppActiveChanged;
            _isStarted = true;
        }

        public void Stop()
        {
            if (!_isStarted)
            {
                return;
            }

            _timer?.Stop();
            _timer.Elapsed -= Timer_Elapsed;
            _appObserver.OnAppActiveChanged -= AppObserver_OnAppActiveChanged;
            _isStarted = false;
        }

        public void GrantExtension(string processName, string executablePath, int minutes = 5)
        {
            if (string.IsNullOrEmpty(processName))
            {
                return;
            }

            lock (_syncRoot)
            {
                string ruleKey = ResolveRuleKey(processName, executablePath);
                if (string.IsNullOrEmpty(ruleKey))
                {
                    return;
                }

                _triggeredToday.Remove(ruleKey);
                //  延长必须先解除锁定态：否则下一个 tick 又命中 _lockedRules 重新弹窗，
                //  延长的这几分钟当场作废，用户按了等于没按
                _lockedRules.Remove(ruleKey);
                _reArmedToday.Remove(ruleKey);
                _banRestartSet.Remove(processName);
                int usage = GetUsageSeconds(ruleKey);
                _todayUsageSeconds[ruleKey] = Math.Max(0, usage - Math.Max(1, minutes) * 60);

                TimeLimitRuleModel rule = _rules.FirstOrDefault(m => GetRuleKey(m) == ruleKey);
                string appName = rule != null && !string.IsNullOrWhiteSpace(rule.Name) ? rule.Name : processName;
                TimeLimitLogger.LogExtension(appName, processName, minutes);

                //  延长量必须落盘：只改内存的话，重启应用或重启系统就会回到超限状态
                AddTodayExtension(ruleKey, minutes);
            }

            //  文件写入放到锁外，避免长时间持有 _syncRoot
            _appConfig.Save();
        }

        /// <summary>
        /// 重新同步应用时长：
        /// 1. 自动检查是否已经进入新的一天（跨天时重置当日状态）；
        /// 2. 从数据库把今日各应用已用时长同步回内存，并按实际用量重建触发标记，
        ///    保证时长限制功能正常工作。
        /// </summary>
        /// <summary>
        /// 重新加载配置：界面里开关或增删规则后立刻生效，不必重启软件。
        /// 原来只在 Start() 里读一次配置，界面改动要等重启——表现就是"启用了也没反应"。
        /// </summary>
        public void Reload()
        {
            LoadSettings();
            RestoreTodayUsage();

            if (!_isStarted)
            {
                Start();
            }

            Logger.Info("时长限制：配置已重新加载，启用=" + _isEnabled + "，规则数=" + (_rules?.Count ?? 0));
        }

        public void ResyncToday()
        {
            lock (_syncRoot)
            {
                ResetIfNewDay();
            }

            RestoreTodayUsage();
            Logger.Info("时长限制：已重新同步今日应用时长（跨天检查 + 数据库同步）");
        }

        private void LoadSettings()
        {
            var settings = _appConfig.GetConfig()?.TimeLimit;
            _isEnabled = settings?.IsEnabled ?? false;
            _rules = settings?.Rules?
                .Where(m => m != null)
                .ToList() ?? new List<TimeLimitRuleModel>();

            //  清理已删除规则的残留状态
            lock (_syncRoot)
            {
                var validKeys = new HashSet<string>(
                    _rules.Where(m => m != null).Select(GetRuleKey),
                    StringComparer.OrdinalIgnoreCase);

                foreach (string staleBan in _banRestartSet.Keys.Where(p => !_rules.Any(m =>
                    string.Equals(m.ProcessName, p, StringComparison.OrdinalIgnoreCase))).ToList())
                {
                    _banRestartSet.Remove(staleBan);
                }
                _lockedRules.RemoveWhere(k => !validKeys.Contains(k));
                _triggeredToday.RemoveWhere(k => !validKeys.Contains(k));
                _actingRules.RemoveWhere(k => !validKeys.Contains(k));
                _reArmedToday.RemoveWhere(k => !validKeys.Contains(k));

                foreach (string staleGrace in _unlockGraceUntil.Keys.Where(k => !validKeys.Contains(k)).ToList())
                {
                    _unlockGraceUntil.Remove(staleGrace);
                }

                var staleKeys = _todayUsageSeconds.Keys.Where(k => !validKeys.Contains(k)).ToList();
                foreach (string staleKey in staleKeys)
                {
                    _todayUsageSeconds.Remove(staleKey);
                }
            }
        }

        /// <summary>
        /// 从数据库恢复今日已用时长与当日触发标记（程序启动时调用），
        /// 防止通过重启软件重置计时绕过限制。
        /// </summary>
        private void RestoreTodayUsage()
        {
            if (!_isEnabled || _rules == null || _rules.Count == 0)
            {
                return;
            }

            try
            {
                var usageMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (TimeLimitRuleModel rule in _rules)
                {
                    if (rule == null || !rule.Enabled || string.IsNullOrEmpty(rule.ProcessName))
                    {
                        continue;
                    }

                    if (!usageMap.ContainsKey(rule.ProcessName))
                    {
                        usageMap[rule.ProcessName] = _data.GetProcessTodayTime(rule.ProcessName);
                    }
                }

                lock (_syncRoot)
                {
                    _todayUsageSeconds.Clear();
                    _triggeredToday.Clear();
                    _reArmedToday.Clear();
                    _actingRules.Clear();

                    foreach (TimeLimitRuleModel rule in _rules)
                    {
                        if (rule == null || !rule.Enabled || string.IsNullOrEmpty(rule.ProcessName))
                        {
                            continue;
                        }

                        string ruleKey = GetRuleKey(rule);
                        int usage = usageMap.TryGetValue(rule.ProcessName, out int restored) ? restored : 0;

                        //  扣掉当日已延长的部分：否则重启后按数据库原始用量重新判超限，
                        //  用户点过的"延长"等于白点（随机密码还可能已经被用掉）
                        usage = Math.Max(0, usage - GetTodayExtensionSeconds(ruleKey));

                        _todayUsageSeconds[ruleKey] = usage;

                        int limitSeconds = Math.Max(1, rule.LimitMinutes) * 60;
                        if (usage >= limitSeconds)
                        {
                            _triggeredToday.Add(ruleKey);

                            //  锁定遮罩类规则恢复为"锁定"状态：目标应用下次前台激活立即重新遮罩，
                            //  否则恢复的标记只会让判定直接跳过——重启反而绕过了当日限制
                            if ((TimeLimitAction)rule.Action == TimeLimitAction.LockOverlay)
                            {
                                _lockedRules.Add(ruleKey);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("恢复今日时长限制用量失败：" + ex.Message);
            }
        }

        private void AppObserver_OnAppActiveChanged(object sender, AppActiveChangedEventArgs e)
        {
            if (e?.App == null || e.App.Type == AppType.SystemComponent)
            {
                _activeApp = AppInfo.Empty;
                return;
            }
            // guage self foreground: keep previous app so time-limit keeps working across page switches
            if (string.Equals(e.App.Process, "guage", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _activeApp = e.App;
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (!_isEnabled || _isSleeping)
            {
                return;
            }

            // 会话级禁止重启：被禁进程若被重新启动，立即再次结束
            EnforceBanRestart();

            TimeLimitRuleModel triggerRule = null;
            AppInfo triggerApp = AppInfo.Empty;

            lock (_syncRoot)
            {
                ResetIfNewDay();

                if (string.IsNullOrEmpty(_activeApp.Process))
                {
                    return;
                }

                TimeLimitRuleModel rule = FindRule(_activeApp);
                if (rule == null)
                {
                    return;
                }

                string ruleKey = GetRuleKey(rule);

                // 解锁宽限期内不重锁（密码解锁成功后 RelockMinutes 分钟的宽限）
                if (_unlockGraceUntil.TryGetValue(ruleKey, out DateTime graceUntil) && DateTime.Now < graceUntil)
                {
                    return;
                }

                // 锁定遮罩重启立即锁定：处于锁定状态的规则，进程重新激活时立即重新遮罩
                if (_lockedRules.Contains(ruleKey))
                {
                    if (_actingRules.Contains(ruleKey))
                    {
                        return;
                    }
                    _actingRules.Add(ruleKey);
                    TriggerLockAction(rule, _activeApp);
                    return;
                }

                //  该规则的遮罩/倒计时正在处理：只跳过本规则计数，其它规则不受影响
                if (_actingRules.Contains(ruleKey))
                {
                    return;
                }

                int usage = GetUsageSeconds(ruleKey) + 1;
                _todayUsageSeconds[ruleKey] = usage;

                int limitSeconds = Math.Max(1, rule.LimitMinutes) * 60;
                if (usage < limitSeconds)
                {
                    return;
                }

                if (_triggeredToday.Contains(ruleKey))
                {
                    //  重启恢复：超限标记在但动作当日从未补执行——只补一次，
                    //  避免每秒重复弹窗；锁定类规则已在恢复时转入 _lockedRules
                    if (_reArmedToday.Contains(ruleKey))
                    {
                        return;
                    }
                    _reArmedToday.Add(ruleKey);
                }
                else
                {
                    _triggeredToday.Add(ruleKey);
                }

                _actingRules.Add(ruleKey);
                // 同锁内快照触发对象：避免两段锁之间前台切换导致状态被标记却无人处理
                triggerRule = rule;
                triggerApp = _activeApp;
            }

            if (triggerRule == null)
            {
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    await ExecuteActionAsync(triggerRule, triggerApp);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex.ToString());
                }
                finally
                {
                    lock (_syncRoot)
                    {
                        _actingRules.Remove(GetRuleKey(triggerRule));
                    }
                }
            });
        }

        /// <summary>
        /// 会话级禁止重启：banned 进程若重新出现则再次结束（有路径信息时精确匹配，避免误杀同名程序）
        /// </summary>
        private void EnforceBanRestart()
        {
            //  写入点都在 _syncRoot 内，这里也要在锁内做快照再遍历；否则并发修改抛出的异常
            //  会被外层 catch 吞掉，"禁止重启"偶发失效且日志里一个字都没有
            List<KeyValuePair<string, string>> bannedSnapshot;
            lock (_syncRoot)
            {
                if (_banRestartSet.Count == 0)
                {
                    return;
                }

                bannedSnapshot = _banRestartSet.ToList();
            }

            foreach (var banned in bannedSnapshot)
            {
                try
                {
                    var procs = Process.GetProcessesByName(banned.Key);
                    foreach (var proc in procs)
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(banned.Value) && !MatchesBannedPath(proc, banned.Value))
                            {
                                continue;
                            }
                            proc.Kill();
                            TimeLimitLogger.LogKill("(禁止重启)", banned.Key, proc.Id);
                        }
                        catch
                        {
                            // 进程可能已退出
                        }
                        finally
                        {
                            proc.Dispose();
                        }
                    }
                }
                catch
                {
                    // 忽略单个进程枚举失败
                }
            }
        }

        /// <summary>
        /// 校验进程可执行文件路径与封禁路径是否一致；
        /// 路径无法读取（权限不足/已退出）时视为不匹配——宁漏勿误杀
        /// </summary>
        private static bool MatchesBannedPath(Process proc, string normalizedBannedPath)
        {
            try
            {
                string procPath = proc.MainModule?.FileName;
                if (string.IsNullOrEmpty(procPath))
                {
                    return false;
                }
                return string.Equals(
                    TimeLimitLogic.NormalizePath(procPath),
                    normalizedBannedPath,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private void TriggerLockAction(TimeLimitRuleModel rule, AppInfo app)
        {
            Task.Run(async () =>
            {
                try
                {
                    await ExecuteActionAsync(rule, app);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex.ToString());
                }
                finally
                {
                    lock (_syncRoot)
                    {
                        _actingRules.Remove(GetRuleKey(rule));
                    }
                }
            });
        }

        private async Task ExecuteActionAsync(TimeLimitRuleModel rule, AppInfo app)
        {
            string appName = string.IsNullOrWhiteSpace(rule.Name) ? rule.ProcessName : rule.Name;
            string message = $"{appName} 已达今日限制 {rule.LimitMinutes} 分钟";
            string actionName = GetActionName(rule.Action);

            TimeLimitLogger.LogTriggered(appName, rule.ProcessName, rule.LimitMinutes, actionName);

            switch ((TimeLimitAction)rule.Action)
            {
                case TimeLimitAction.Warn:
                    _notifier.ShowWarning("时长限制提醒", message, rule.UseNativeMessageBox ? 1 : 0);
                    break;
                case TimeLimitAction.CountdownKill:
                    TimeLimitDialogResult countdownResult = await _notifier.ShowCountdownAsync(
                        "时长限制倒计时",
                        message + $"\n{Math.Max(5, rule.CountdownSeconds)} 秒后自动结束应用",
                        Math.Max(5, rule.CountdownSeconds),
                        rule.ProcessName,
                        rule.ExecutablePath,
                        rule.ShowFloatingCountdown,
                        rule.CountdownOpacity,
                        //  作用域：仅倒计时窗可三击隐藏；锁定遮罩（LockOverlay）不提供隐藏入口，安全优先
                        rule.HideByTripleClick);
                    if (countdownResult == TimeLimitDialogResult.Kill)
                    {
                        KillProcessTree(rule.ProcessName);
                        if (rule.BanRestart)
                        {
                            lock (_syncRoot)
                            {
                                _banRestartSet[rule.ProcessName] = NormalizePath(rule.ExecutablePath);
                            }
                        }
                    }
                    else if (countdownResult == TimeLimitDialogResult.Extend)
                    {
                        GrantExtension(rule.ProcessName, rule.ExecutablePath, 5);
                    }
                    else if (countdownResult == TimeLimitDialogResult.Cancelled)
                    {
                        //  取消=暂不处理但保持限制：规则转入锁定态，下次前台激活重新触发
                        //  （旧行为什么都不做，规则当日被彻底绕过）
                        lock (_syncRoot)
                        {
                            _lockedRules.Add(GetRuleKey(rule));
                        }
                    }
                    break;
                case TimeLimitAction.LockOverlay:
                    await ExecuteLockOverlayAsync(rule, app);
                    break;
                default:
                    _notifier.ShowWarning("时长限制提醒", message);
                    break;
            }
        }

        /// <summary>
        /// 锁定遮罩：随机密码管理 + 密码验证 + 再次锁定定时 + 重启立即锁定标记
        /// </summary>
        private async Task ExecuteLockOverlayAsync(TimeLimitRuleModel rule, AppInfo app)
        {
            string ruleKey = GetRuleKey(rule);

            // 无密码时默认随机生成：自定义密码为空自动切换为随机密码模式（6 位数字字母组合）
            if (rule.PasswordMode == 0 && string.IsNullOrEmpty(rule.CustomPasswordHash))
            {
                rule.PasswordMode = 1;
                _appConfig.Save();
            }

            // 随机密码准备：无密码时生成 6 位数字字母混合密码并持久化
            string randomPassword = string.Empty;
            bool randomViewed = false;
            var settings = _appConfig.GetConfig()?.TimeLimit;
            if (rule.PasswordMode == 1)
            {
                if (string.IsNullOrEmpty(settings?.RandomPassword))
                {
                    randomPassword = GenerateRandomPassword(6);
                    if (settings != null)
                    {
                        settings.RandomPassword = randomPassword;
                        settings.RandomPasswordViewed = false;
                        _appConfig.Save();
                    }
                }
                else
                {
                    randomPassword = settings.RandomPassword;
                    randomViewed = settings.RandomPasswordViewed;
                }
            }

            string message = $"{rule.Name} 已达今日限制，输入密码解锁";
            TimeLimitDialogResult lockResult = await _notifier.ShowLockOverlayAsync(
                "时长限制锁定",
                message,
                rule.ProcessName,
                rule.ExecutablePath,
                rule.PasswordMode,
                rule.CustomPasswordHash,
                randomPassword,
                randomViewed,
                rule.RelockMinutes);

            lock (_syncRoot)
            {
                if (lockResult == TimeLimitDialogResult.Extend)
                {
                    _triggeredToday.Remove(ruleKey);
                    _lockedRules.Remove(ruleKey);
                    GrantExtension(rule.ProcessName, rule.ExecutablePath, 5);
                }
                else if (lockResult == TimeLimitDialogResult.Confirmed)
                {
                    //  解锁成功：先解除锁定态（RelockMinutes=0 时不再重锁；
                    //  配置了重锁定时则由 ScheduleRelock 到点重新加入 _lockedRules）
                    _lockedRules.Remove(ruleKey);

                    // 解锁成功：进入 RelockMinutes 宽限期（宽限内不重锁，避免 1 秒重锁死循环）；
                    // 宽限结束由 ScheduleRelock 定时器把规则放回 _lockedRules（重启/再激活立即重锁）
                    if (rule.RelockMinutes > 0)
                    {
                        _unlockGraceUntil[ruleKey] = DateTime.Now.AddMinutes(rule.RelockMinutes);
                    }
                    ScheduleRelock(rule);

                    // 密码一次性：解锁成功后两个密码均作废（随机密码清空重新生成、自定义密码一并清除）
                    var cfg = _appConfig.GetConfig();
                    bool changed = false;
                    if (cfg?.TimeLimit != null && !string.IsNullOrEmpty(cfg.TimeLimit.RandomPassword))
                    {
                        cfg.TimeLimit.RandomPassword = string.Empty;
                        cfg.TimeLimit.RandomPasswordViewed = false;
                        changed = true;
                    }

                    TimeLimitRuleModel liveRule = _rules.FirstOrDefault(m => GetRuleKey(m) == ruleKey);
                    if (liveRule != null && !string.IsNullOrEmpty(liveRule.CustomPasswordHash))
                    {
                        liveRule.CustomPasswordHash = string.Empty;
                        changed = true;
                    }

                    if (changed)
                    {
                        _appConfig.Save();
                    }
                }
                else if (lockResult == TimeLimitDialogResult.ProcessExited)
                {
                    // 锁定期间应用被强制结束（如任务管理器）：禁止该应用再次启动
                    // 注意保留 _triggeredToday 标记，避免当日反复弹出遮罩
                    _banRestartSet[rule.ProcessName] = NormalizePath(rule.ExecutablePath);
                    TimeLimitLogger.LogKill("(锁定期间强制退出，禁止重启)", rule.ProcessName, 0);

                    //  接线 RelockAfterExit：开启时应用再次进入立即重锁；
                    //  关闭时解除锁定态（当日已触发标记保留，不再重复执行动作）
                    if (rule.RelockAfterExit)
                    {
                        _lockedRules.Add(ruleKey);
                    }
                    else
                    {
                        _lockedRules.Remove(ruleKey);
                    }
                }
            }
        }

        /// <summary>
        /// 解锁后按 RelockMinutes 定时再次锁定（0 = 不再次锁定）
        /// </summary>
        private void ScheduleRelock(TimeLimitRuleModel rule)
        {
            if (rule.RelockMinutes <= 0)
            {
                return;
            }

            string ruleKey = GetRuleKey(rule);
            var relockTimer = new Timer(rule.RelockMinutes * 60 * 1000);
            relockTimer.AutoReset = false;
            relockTimer.Elapsed += (s, e) =>
            {
                relockTimer.Stop();
                relockTimer.Dispose();
                lock (_syncRoot)
                {
                    _lockedRules.Add(ruleKey);
                    _unlockGraceUntil.Remove(ruleKey);
                    // 若该应用当前正在使用，立即重新锁定
                    if (!string.IsNullOrEmpty(_activeApp.Process)
                        && string.Equals(_activeApp.Process, rule.ProcessName, StringComparison.OrdinalIgnoreCase)
                        && !_actingRules.Contains(ruleKey))
                    {
                        _actingRules.Add(ruleKey);
                        TriggerLockAction(rule, _activeApp);
                    }
                }
            };
            relockTimer.Start();
        }

        /// <summary>
        /// 结束目标进程及其子进程
        /// </summary>
        private void KillProcessTree(string processName)
        {
            try
            {
                var procs = Process.GetProcessesByName(processName);
                foreach (var proc in procs)
                {
                    try
                    {
                        if (ProcessHelper.TryKillProcess(proc.Id))
                        {
                            TimeLimitLogger.LogKill(processName, processName, proc.Id);
                        }
                    }
                    catch
                    {
                        // 单个失败继续
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex.ToString());
            }
        }

        /// <summary>
        /// 生成 n 位数字字母混合随机密码（排除易混淆字符，保证至少 1 位数字 + 1 位字母）
        /// </summary>
        private static string GenerateRandomPassword(int length)
        {
            //  纯逻辑已抽取到 TimeLimitLogic（可单元测试）
            return TimeLimitLogic.GenerateRandomPassword(length);
        }

        private static string GetActionName(int action)
        {
            switch ((TimeLimitAction)action)
            {
                case TimeLimitAction.Warn: return "仅提示";
                case TimeLimitAction.CountdownKill: return "倒计时关闭";
                case TimeLimitAction.LockOverlay: return "锁定遮罩";
                default: return "仅提示";
            }
        }

        private static int TodayKey()
        {
            DateTime now = DateTime.Now;
            return now.Year * 10000 + now.Month * 100 + now.Day;
        }

        /// <summary>
        /// 当日对某条规则的延长秒数；跨天自动视为 0
        /// </summary>
        private int GetTodayExtensionSeconds(string ruleKey)
        {
            TimeLimitSettingsModel settings = _appConfig.GetConfig()?.TimeLimit;
            if (settings?.TodayExtensions == null || settings.TodayExtensionsDate != TodayKey())
            {
                return 0;
            }

            return settings.TodayExtensions.TryGetValue(ruleKey ?? string.Empty, out int minutes)
                ? Math.Max(0, minutes) * 60
                : 0;
        }

        private void AddTodayExtension(string ruleKey, int minutes)
        {
            TimeLimitSettingsModel settings = _appConfig.GetConfig()?.TimeLimit;
            if (settings == null || string.IsNullOrEmpty(ruleKey) || minutes <= 0)
            {
                return;
            }

            if (settings.TodayExtensions == null || settings.TodayExtensionsDate != TodayKey())
            {
                //  换天了：昨天的延长一律作废
                settings.TodayExtensions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                settings.TodayExtensionsDate = TodayKey();
            }

            settings.TodayExtensions.TryGetValue(ruleKey, out int current);
            settings.TodayExtensions[ruleKey] = current + Math.Max(1, minutes);
        }

        private string ResolveRuleKey(string processName, string executablePath)
        {
            //  纯逻辑已抽取到 TimeLimitLogic（可单元测试）
            return TimeLimitLogic.ResolveRuleKey(_rules, processName, executablePath);
        }

        private void ResetIfNewDay()
        {
            if (_trackDate == DateTime.Today)
            {
                return;
            }

            _trackDate = DateTime.Today;
            _todayUsageSeconds.Clear();
            _triggeredToday.Clear();
            _banRestartSet.Clear();
            _lockedRules.Clear();
            _unlockGraceUntil.Clear();
            _reArmedToday.Clear();
            _actingRules.Clear();
        }

        private TimeLimitRuleModel FindRule(AppInfo app)
        {
            //  纯逻辑已抽取到 TimeLimitLogic（可单元测试）
            return TimeLimitLogic.FindRule(_rules, app);
        }

        private static string NormalizePath(string path)
        {
            return TimeLimitLogic.NormalizePath(path);
        }

        private static string GetRuleKey(TimeLimitRuleModel rule)
        {
            return TimeLimitLogic.GetRuleKey(rule);
        }

        private int GetUsageSeconds(string ruleKey)
        {
            return _todayUsageSeconds.TryGetValue(ruleKey, out int seconds) ? seconds : 0;
        }
    }
}
