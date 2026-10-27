using Core.Librarys;
using Core.Models;
using Core.Models.Config;
using Core.Servicers.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using UI.Controls;
using UI.Controls.Select;
using UI.Models;
using UI.Servicers;

namespace UI.ViewModels
{
    public class TimeLimitPageVM : UINotifyPropertyChanged
    {
        private readonly IAppConfig _appConfig;
        private readonly IAppData _appData;
        private readonly ITimeLimitServicer _timeLimitServicer;
        private readonly IUIServicer _uiServicer;
        private ConfigModel _config;
        private readonly DispatcherTimer _filterTimer;
        private List<AppModel> _allAppsCache;
        private TimeLimitRuleItemVM _selectedRuleItem;
        private int _scanToken;

        public List<SelectItemModel> ActionOptions { get; } = new List<SelectItemModel>
        {
            new SelectItemModel { Id = 0, Name = "提醒" },
            new SelectItemModel { Id = 1, Name = "倒计时关闭" },
            new SelectItemModel { Id = 2, Name = "锁定遮罩" }
        };

        public Command AddFromTrackedAppCommand { get; }
        public Command BrowseExeCommand { get; }
        public Command PickerBrowseExeCommand { get; }
        public Command RemoveRuleCommand { get; }
        public Command SelectRuleCommand { get; }
        public Command ClosePickerCommand { get; }
        public Command JumpToLetterCommand { get; }
        public Command ResyncTodayCommand { get; }

        public ObservableCollection<TimeLimitRuleItemVM> RuleItems { get; } = new ObservableCollection<TimeLimitRuleItemVM>();
        public ObservableCollection<PickerListItemVM> FilteredPickerItems { get; } = new ObservableCollection<PickerListItemVM>();
        public ObservableCollection<PickerLetterVM> PickerLetters { get; } = new ObservableCollection<PickerLetterVM>();

        private static readonly string[] PickerIndexLetterOrder =
        {
            "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
            "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z", "#"
        };

        private bool _isEnabled;
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(); SaveConfig(); }
        }

        private bool _isAppPickerVisible;
        public bool IsAppPickerVisible
        {
            get => _isAppPickerVisible;
            private set
            {
                if (_isAppPickerVisible == value) return;
                _isAppPickerVisible = value;
                OnPropertyChanged();
            }
        }

        private bool _isScanningApps;
        public bool IsScanningApps
        {
            get => _isScanningApps;
            private set
            {
                if (_isScanningApps == value) return;
                _isScanningApps = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PickerHintText));
                OnPropertyChanged(nameof(PickerLoadingVisibility));
                OnPropertyChanged(nameof(PickerListVisibility));
                OnPropertyChanged(nameof(PickerFooterVisibility));
            }
        }

        public System.Windows.Visibility PickerFooterVisibility =>
            IsScanningApps ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

        public bool IsEmpty => RuleItems.Count == 0;
        public bool HasSelection => RuleItems.Any(m => m.IsChecked);

        public string DeleteButtonText
        {
            get
            {
                int count = RuleItems.Count(m => m.IsChecked);
                return count > 0 ? $"删除 ({count})" : "删除";
            }
        }

        public System.Windows.Visibility EmptyVisibility =>
            IsEmpty ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        public System.Windows.Visibility RulesVisibility =>
            IsEmpty ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

        public System.Windows.Visibility PickerLoadingVisibility =>
            IsScanningApps ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        public System.Windows.Visibility PickerListVisibility =>
            IsScanningApps ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

        public string PickerHintText =>
            "单击应用即可添加为限制规则，或使用「浏览 exe」手动选择";

        private string _searchKeyword = string.Empty;
        public string SearchKeyword
        {
            get => _searchKeyword;
            set { _searchKeyword = value; OnPropertyChanged(); FilterApps(); }
        }

        public ObservableCollection<AppModel> FilteredApps { get; } = new ObservableCollection<AppModel>();

        public event EventHandler<string> PickerLetterJumpRequested;

        public TimeLimitRuleItemVM SelectedRuleItem
        {
            get => _selectedRuleItem;
            set
            {
                if (_selectedRuleItem == value) return;
                if (_selectedRuleItem != null)
                {
                    _selectedRuleItem.IsSelected = false;
                }

                _selectedRuleItem = value;
                if (_selectedRuleItem != null)
                {
                    _selectedRuleItem.IsSelected = true;
                }

                OnPropertyChanged();
            }
        }

        private void UpdateSelectionState()
        {
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(DeleteButtonText));
        }

        public void SelectRuleSingle(TimeLimitRuleItemVM item)
        {
            if (item == null)
            {
                return;
            }

            foreach (TimeLimitRuleItemVM rule in RuleItems)
            {
                rule.IsChecked = ReferenceEquals(rule, item);
            }

            SelectedRuleItem = item;
            UpdateSelectionState();
        }

        public void ToggleRuleChecked(TimeLimitRuleItemVM item)
        {
            if (item == null)
            {
                return;
            }

            item.IsChecked = !item.IsChecked;
            UpdateSelectionState();
        }

        public void RequestJumpToLetter(string letter)
        {
            if (string.IsNullOrWhiteSpace(letter))
            {
                return;
            }

            PickerLetterJumpRequested?.Invoke(this, letter);
        }

        private void InitializePickerLetters()
        {
            PickerLetters.Clear();
            foreach (string letter in PickerIndexLetterOrder)
            {
                PickerLetters.Add(new PickerLetterVM { Letter = letter, IsAvailable = false });
            }
        }

        public event EventHandler AppPickerOpening;
        public event EventHandler AppPickerClosing;

        public TimeLimitPageVM(IAppConfig appConfig, IAppData appData, ITimeLimitServicer timeLimitServicer, IUIServicer uiServicer)
        {
            _appConfig = appConfig;
            _appData = appData;
            _timeLimitServicer = timeLimitServicer;
            _uiServicer = uiServicer;

            AddFromTrackedAppCommand = new Command(_ => ShowAppPicker());
            BrowseExeCommand = new Command(_ => BrowseExe());
            PickerBrowseExeCommand = new Command(_ => BrowseExeFromPicker());
            RemoveRuleCommand = new Command(_ => RemoveCheckedRules());
            SelectRuleCommand = new Command(obj => SelectedRuleItem = obj as TimeLimitRuleItemVM);
            ClosePickerCommand = new Command(_ => RequestClosePicker());
            JumpToLetterCommand = new Command(obj => RequestJumpToLetter(obj?.ToString()));
            ResyncTodayCommand = new Command(_ => ResyncTodayState());

            InitializePickerLetters();

            _filterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
            _filterTimer.Tick += (s, e) =>
            {
                _filterTimer.Stop();
                ApplyAppFilter();
            };

            RuleItems.CollectionChanged += (s, e) => UpdateEmptyState();
            Load();
        }

        public void Load()
        {
            _config = _appConfig.GetConfig();
            if (_config.TimeLimit == null)
            {
                _config.TimeLimit = new TimeLimitSettingsModel();
            }
            if (_config.TimeLimit.Rules == null)
            {
                _config.TimeLimit.Rules = new List<TimeLimitRuleModel>();
            }

            _isEnabled = _config.TimeLimit.IsEnabled;
            OnPropertyChanged(nameof(IsEnabled));

            RuleItems.Clear();
            SelectedRuleItem = null;
            foreach (TimeLimitRuleModel rule in _config.TimeLimit.Rules)
            {
                RuleItems.Add(CreateRuleItem(CloneRule(rule)));
            }
            UpdateEmptyState();
            UpdateSelectionState();
        }

        public void ShowAppPicker()
        {
            IsAppPickerVisible = true;
            IsScanningApps = true;
            FilteredApps.Clear();
            OnPropertyChanged(nameof(PickerLoadingVisibility));
            OnPropertyChanged(nameof(PickerListVisibility));
            AppPickerOpening?.Invoke(this, EventArgs.Empty);

            int scanToken = ++_scanToken;
            Task.Run(() =>
            {
                List<AppModel> scanned = InstalledAppScanner.Scan(forceRefresh: true);
                List<AppModel> tracked = _appData.GetAllApps() ?? new List<AppModel>();
                List<AppModel> merged = InstalledAppScanner.MergeWithTracked(tracked, scanned);
                return merged;
            }).ContinueWith(task =>
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    return;
                }

                dispatcher.BeginInvoke(new Action(() =>
                {
                    if (scanToken != _scanToken || !IsAppPickerVisible)
                    {
                        return;
                    }

                    if (task.IsFaulted)
                    {
                        Logger.Error("扫描本机应用失败: " + task.Exception?.GetBaseException()?.Message);
                        _allAppsCache = _appData.GetAllApps() ?? new List<AppModel>();
                    }
                    else
                    {
                        _allAppsCache = task.Result ?? new List<AppModel>();
                    }

                    IsScanningApps = false;
                    OnPropertyChanged(nameof(PickerLoadingVisibility));
                    OnPropertyChanged(nameof(PickerListVisibility));
                    ApplyAppFilter();
                }), DispatcherPriority.Background);
            });
        }

        public void RequestClosePicker()
        {
            _scanToken++;
            AppPickerClosing?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 重新同步应用时长：自动检查是否已是新的一天，并把今日应用时长
        /// 从数据库同步到各功能，保证时长限制正常计数。
        /// </summary>
        private void ResyncTodayState()
        {
            _timeLimitServicer.ResyncToday();
            System.Windows.MessageBox.Show(
                "已自动检查新的一天，并重新同步今日应用时长，时长限制功能已按最新数据工作。",
                "重新同步完成",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }

        public void HideAppPicker()
        {
            IsAppPickerVisible = false;
            IsScanningApps = false;
            SearchKeyword = string.Empty;
            OnPropertyChanged(nameof(PickerLoadingVisibility));
            OnPropertyChanged(nameof(PickerListVisibility));
        }

        public void SelectApp(AppModel app)
        {
            if (app == null)
            {
                return;
            }

            var rule = new TimeLimitRuleModel
            {
                Name = string.IsNullOrWhiteSpace(app.Description) ? app.Name : app.Description,
                ProcessName = app.Name,
                ExecutablePath = app.File ?? string.Empty,
                LimitMinutes = 60,
                Action = 0,
                CountdownSeconds = 60,
                Enabled = true
            };
            RuleItems.Add(CreateRuleItem(rule));
            SelectedRuleItem = RuleItems.LastOrDefault();
            SaveConfig();
        }

        private void BrowseExe()
        {
            AddRuleFromExeDialog(false);
        }

        private void BrowseExeFromPicker()
        {
            AddRuleFromExeDialog(true);
        }

        private void AddRuleFromExeDialog(bool closePickerAfterAdd)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "可执行文件 (*.exe)|*.exe",
                Title = "选择要限制的应用程序"
            };

            if (dialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            string processName = Path.GetFileNameWithoutExtension(dialog.FileName);
            var rule = new TimeLimitRuleModel
            {
                Name = processName,
                ProcessName = processName,
                ExecutablePath = dialog.FileName,
                LimitMinutes = 60,
                Action = 0,
                CountdownSeconds = 60,
                Enabled = true
            };
            RuleItems.Add(CreateRuleItem(rule));
            SelectedRuleItem = RuleItems.LastOrDefault();
            SaveConfig();

            if (closePickerAfterAdd)
            {
                RequestClosePicker();
            }
        }

        private async void RemoveCheckedRules()
        {
            var toRemove = RuleItems.Where(m => m.IsChecked).ToList();
            if (toRemove.Count == 0)
            {
                return;
            }

            //  破坏性操作二次确认（与其他页面删除行为一致）
            var ui = _uiServicer;
            if (ui != null)
            {
                bool confirmed = await ui.ShowConfirmDialogAsync("删除确认", $"确定删除选中的 {toRemove.Count} 条限制规则？");
                if (!confirmed)
                {
                    return;
                }
            }

            foreach (TimeLimitRuleItemVM item in toRemove)
            {
                RuleItems.Remove(item);
            }

            SelectedRuleItem = null;
            SaveConfig();
            UpdateSelectionState();
        }

        private void FilterApps()
        {
            _filterTimer.Stop();
            _filterTimer.Start();
        }

        private void ApplyAppFilter()
        {
            var apps = _allAppsCache ?? _appData.GetAllApps() ?? new List<AppModel>();
            IEnumerable<AppModel> query = apps.Where(m => !string.IsNullOrEmpty(m.Name));

            if (!string.IsNullOrWhiteSpace(SearchKeyword))
            {
                query = query.Where(m =>
                    (m.Name != null && m.Name.IndexOf(SearchKeyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (m.Description != null && m.Description.IndexOf(SearchKeyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (m.File != null && m.File.IndexOf(SearchKeyword, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            FilteredApps.Clear();
            FilteredPickerItems.Clear();
            var availableLetters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string lastLetter = null;

            foreach (AppModel app in query.OrderBy(m => GetPickerSortName(m)).ThenBy(m => m.Name))
            {
                if (!seen.Add(app.Name))
                {
                    continue;
                }

                InstalledAppScanner.EnrichDisplayName(app);
                EnsureAppIcon(app);

                string letter = GetPickerIndexLetter(app);
                if (!string.Equals(lastLetter, letter, StringComparison.OrdinalIgnoreCase))
                {
                    FilteredPickerItems.Add(new PickerListItemVM
                    {
                        IsSectionHeader = true,
                        SectionLetter = letter
                    });
                    availableLetters.Add(letter);
                    lastLetter = letter;
                }

                FilteredPickerItems.Add(new PickerListItemVM { App = app });
                FilteredApps.Add(app);

                if (FilteredApps.Count >= 150)
                {
                    break;
                }
            }

            foreach (PickerLetterVM pickerLetter in PickerLetters)
            {
                pickerLetter.IsAvailable = availableLetters.Contains(pickerLetter.Letter);
            }
        }

        private static string GetPickerSortName(AppModel app)
        {
            return string.IsNullOrWhiteSpace(app.Description) ? app.Name : app.Description;
        }

        private static string GetPickerIndexLetter(AppModel app)
        {
            string name = GetPickerSortName(app);
            if (string.IsNullOrWhiteSpace(name))
            {
                return "#";
            }

            char first = char.ToUpperInvariant(name.Trim()[0]);
            return first >= 'A' && first <= 'Z' ? first.ToString() : "#";
        }

        private static void EnsureAppIcon(AppModel app)
        {
            if (app == null || !string.IsNullOrWhiteSpace(app.IconFile))
            {
                return;
            }

            app.IconFile = Iconer.Get(app.Name, app.Description ?? app.Name);
        }

        private void SaveConfig()
        {
            if (_config?.TimeLimit == null)
            {
                return;
            }

            _config.TimeLimit.IsEnabled = IsEnabled;
            _config.TimeLimit.Rules = RuleItems.Select(m => CloneRule(m.Model)).ToList();
            _appConfig.Save();

            //  服务只在 Start() 时读过一次配置：这里不通知就永远不生效，
            //  表现就是「启用了、也加了规则，但三个动作都没反应」
            _timeLimitServicer.Reload();
        }

        private TimeLimitRuleItemVM CreateRuleItem(TimeLimitRuleModel rule)
        {
            var item = new TimeLimitRuleItemVM(rule, ActionOptions, SaveConfig);
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(TimeLimitRuleItemVM.IsChecked))
                {
                    UpdateSelectionState();
                }
            };
            return item;
        }

        private void UpdateEmptyState()
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(EmptyVisibility));
            OnPropertyChanged(nameof(RulesVisibility));
            UpdateSelectionState();
        }

        private static TimeLimitRuleModel CloneRule(TimeLimitRuleModel rule)
        {
            if (rule == null)
            {
                return new TimeLimitRuleModel();
            }

            //  必须与 TimeLimitRuleModel 的全部字段一一对应，遗漏会导致高级选项被静默重置
            return new TimeLimitRuleModel
            {
                Name = rule.Name,
                ProcessName = rule.ProcessName,
                ExecutablePath = rule.ExecutablePath,
                LimitMinutes = rule.LimitMinutes,
                Action = rule.Action,
                CountdownSeconds = rule.CountdownSeconds,
                Enabled = rule.Enabled,
                UseNativeMessageBox = rule.UseNativeMessageBox,
                ShowFloatingCountdown = rule.ShowFloatingCountdown,
                CountdownOpacity = rule.CountdownOpacity,
                HideByTripleClick = rule.HideByTripleClick,
                BanRestart = rule.BanRestart,
                PasswordMode = rule.PasswordMode,
                CustomPasswordHash = rule.CustomPasswordHash,
                RelockMinutes = rule.RelockMinutes,
                RelockAfterExit = rule.RelockAfterExit,
            };
        }
    }
}
