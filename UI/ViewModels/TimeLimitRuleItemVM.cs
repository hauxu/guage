using Core.Models.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UI.Controls.Select;
using UI.Models;

namespace UI.ViewModels
{
    public class TimeLimitRuleItemVM : UINotifyPropertyChanged
    {
        private readonly TimeLimitRuleModel _model;
        private readonly Action _onChanged;
        private readonly List<SelectItemModel> _actionOptions;
        private readonly List<SelectItemModel> _passwordModeOptions;
        private SelectItemModel _selectedAction;
        private SelectItemModel _selectedPasswordMode;
        private bool _isSelected;
        private bool _isChecked;

        public TimeLimitRuleItemVM(TimeLimitRuleModel model, List<SelectItemModel> actionOptions, Action onChanged)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _actionOptions = actionOptions;
            _onChanged = onChanged;
            _selectedAction = _actionOptions.FirstOrDefault(m => m.Id == _model.Action) ?? _actionOptions[0];

            _passwordModeOptions = new List<SelectItemModel>
            {
                new SelectItemModel { Id = 1, Name = "随机 6 位密码" },
                new SelectItemModel { Id = 0, Name = "自定义密码" }
            };
            _selectedPasswordMode = _passwordModeOptions.FirstOrDefault(m => m.Id == _model.PasswordMode) ?? _passwordModeOptions[0];
        }

        public TimeLimitRuleModel Model => _model;

        public string Name
        {
            get => _model.Name;
            set
            {
                if (_model.Name == value) return;
                _model.Name = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IconLetter));
                _onChanged?.Invoke();
            }
        }

        public string ProcessName => _model.ProcessName;

        public string IconLetter
        {
            get
            {
                string text = string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim();
                return text.Substring(0, 1).ToUpperInvariant();
            }
        }

        public bool Enabled
        {
            get => _model.Enabled;
            set
            {
                if (_model.Enabled == value) return;
                _model.Enabled = value;
                OnPropertyChanged();
                _onChanged?.Invoke();
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value) return;
                _isChecked = value;
                OnPropertyChanged();
            }
        }

        public string LimitMinutesText
        {
            get => _model.LimitMinutes.ToString();
            set
            {
                if (int.TryParse(value, out int minutes) && minutes > 0)
                {
                    if (_model.LimitMinutes == minutes) return;
                    _model.LimitMinutes = minutes;
                    _onChanged?.Invoke();
                }
                OnPropertyChanged();
            }
        }

        public string CountdownSecondsText
        {
            get => _model.CountdownSeconds.ToString();
            set
            {
                if (int.TryParse(value, out int seconds) && seconds >= 5)
                {
                    if (_model.CountdownSeconds == seconds) return;
                    _model.CountdownSeconds = seconds;
                    _onChanged?.Invoke();
                }
                OnPropertyChanged();
            }
        }

        public List<SelectItemModel> ActionOptions => _actionOptions;

        public SelectItemModel SelectedAction
        {
            get => _selectedAction;
            set
            {
                if (value == null || _selectedAction == value) return;
                _selectedAction = value;
                _model.Action = value.Id;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowCountdownField));
                OnPropertyChanged(nameof(ShowWarnOptions));
                OnPropertyChanged(nameof(ShowCountdownOptions));
                OnPropertyChanged(nameof(ShowLockOptions));
                OnPropertyChanged(nameof(ActionBadge));
                _onChanged?.Invoke();
            }
        }

        public bool ShowCountdownField => _model.Action == 1;

        public string ActionBadge => SelectedAction?.Name ?? "未命名";

        // ============ 高级选项 ============

        public bool ShowWarnOptions => _model.Action == 0;

        public bool ShowCountdownOptions => _model.Action == 1;

        public bool ShowLockOptions => _model.Action == 2;

        public bool UseNativeMessageBox
        {
            get => _model.UseNativeMessageBox;
            set
            {
                if (_model.UseNativeMessageBox == value) return;
                _model.UseNativeMessageBox = value;
                OnPropertyChanged();
                _onChanged?.Invoke();
            }
        }

        public bool ShowFloatingCountdown
        {
            get => _model.ShowFloatingCountdown;
            set
            {
                if (_model.ShowFloatingCountdown == value) return;
                _model.ShowFloatingCountdown = value;
                OnPropertyChanged();
                _onChanged?.Invoke();
            }
        }

        public string CountdownOpacityText
        {
            get => _model.CountdownOpacity.ToString("0.0");
            set
            {
                if (double.TryParse(value, out double opacity))
                {
                    opacity = Math.Max(0.1, Math.Min(0.95, opacity));
                    if (Math.Abs(_model.CountdownOpacity - opacity) < 0.001) return;
                    _model.CountdownOpacity = opacity;
                    _onChanged?.Invoke();
                }
                OnPropertyChanged();
            }
        }

        public bool HideByTripleClick
        {
            get => _model.HideByTripleClick;
            set
            {
                if (_model.HideByTripleClick == value) return;
                _model.HideByTripleClick = value;
                OnPropertyChanged();
                _onChanged?.Invoke();
            }
        }

        public bool BanRestart
        {
            get => _model.BanRestart;
            set
            {
                if (_model.BanRestart == value) return;
                _model.BanRestart = value;
                OnPropertyChanged();
                _onChanged?.Invoke();
            }
        }

        public List<SelectItemModel> PasswordModeOptions => _passwordModeOptions;

        public SelectItemModel SelectedPasswordMode
        {
            get => _selectedPasswordMode;
            set
            {
                if (value == null || _selectedPasswordMode == value) return;
                _selectedPasswordMode = value;
                _model.PasswordMode = value.Id;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowCustomPasswordField));
                _onChanged?.Invoke();
            }
        }

        public bool ShowCustomPasswordField => _model.PasswordMode == 0;

        public string CustomPasswordText
        {
            get => string.Empty;
            set
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                _model.CustomPasswordHash = Sha256(value);
                OnPropertyChanged();
                _onChanged?.Invoke();
            }
        }

        public string RelockMinutesText
        {
            get => _model.RelockMinutes.ToString();
            set
            {
                if (int.TryParse(value, out int minutes) && minutes >= 0)
                {
                    if (_model.RelockMinutes == minutes) return;
                    _model.RelockMinutes = minutes;
                    _onChanged?.Invoke();
                }
                OnPropertyChanged();
            }
        }

        public bool RelockAfterExit
        {
            get => _model.RelockAfterExit;
            set
            {
                if (_model.RelockAfterExit == value) return;
                _model.RelockAfterExit = value;
                OnPropertyChanged();
                _onChanged?.Invoke();
            }
        }

        private static string Sha256(string text)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}
