using Core.Models;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Core.Librarys
{
    /// <summary>
    /// 扫描本机已安装应用，优先使用开始菜单/桌面快捷方式名称。
    /// </summary>
    public static class InstalledAppScanner
    {
        private static List<AppModel> _cache;
        private static DateTime _cacheTime;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

        private sealed class ScanEntry
        {
            public string ProcessName { get; set; }
            public string ExePath { get; set; }
            public string DisplayName { get; set; }
            public int SourcePriority { get; set; }
        }

        public static List<AppModel> Scan(bool forceRefresh = false)
        {
            if (!forceRefresh && _cache != null && DateTime.Now - _cacheTime < CacheDuration)
            {
                return _cache;
            }

            var entries = new Dictionary<string, ScanEntry>(StringComparer.OrdinalIgnoreCase);

            ScanStartMenu(entries);
            ScanRegistry(entries);

            _cache = FinalizeEntries(entries.Values)
                .OrderBy(m => m.Description)
                .ToList();
            _cacheTime = DateTime.Now;
            return _cache;
        }

        public static List<AppModel> MergeWithTracked(IEnumerable<AppModel> tracked, IEnumerable<AppModel> scanned)
        {
            var merged = new Dictionary<string, AppModel>(StringComparer.OrdinalIgnoreCase);

            if (scanned != null)
            {
                foreach (AppModel app in scanned)
                {
                    if (string.IsNullOrWhiteSpace(app?.Name))
                    {
                        continue;
                    }

                    merged[app.Name] = CloneApp(app);
                }
            }

            if (tracked != null)
            {
                foreach (AppModel app in tracked)
                {
                    if (string.IsNullOrWhiteSpace(app?.Name))
                    {
                        continue;
                    }

                    if (merged.TryGetValue(app.Name, out AppModel existing))
                    {
                        if (IsBetterDescription(existing.Description, app.Description, app.Name))
                        {
                            existing.Description = app.Description;
                        }

                        if (string.IsNullOrWhiteSpace(existing.File) && !string.IsNullOrWhiteSpace(app.File))
                        {
                            existing.File = app.File;
                        }

                        if (string.IsNullOrWhiteSpace(existing.IconFile) && !string.IsNullOrWhiteSpace(app.IconFile))
                        {
                            existing.IconFile = app.IconFile;
                        }
                    }
                    else
                    {
                        merged[app.Name] = CloneApp(app);
                    }
                }
            }

            return merged.Values.OrderBy(m => m.Description).ToList();
        }

        public static void EnrichDisplayName(AppModel app)
        {
            if (app == null || string.IsNullOrWhiteSpace(app.Name))
            {
                return;
            }

            if (IsUsableDisplayName(app.Description, app.Name))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(app.File) && File.Exists(app.File))
            {
                app.Description = ResolveFriendlyName(app.File, app.Name, app.Description, preferVersionInfo: false);
            }
        }

        private static List<AppModel> FinalizeEntries(IEnumerable<ScanEntry> entries)
        {
            var result = entries
                .Where(m => !string.IsNullOrWhiteSpace(m.ProcessName))
                .Select(m => new AppModel
                {
                    Name = m.ProcessName,
                    Description = m.DisplayName,
                    File = m.ExePath
                })
                .ToList();

            var groupedByDescription = result
                .GroupBy(m => m.Description ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1);

            foreach (IGrouping<string, AppModel> group in groupedByDescription)
            {
                foreach (AppModel app in group)
                {
                    app.Description = $"{group.Key} ({app.Name})";
                }
            }

            return result;
        }

        private static void ScanStartMenu(Dictionary<string, ScanEntry> entries)
        {
            string[] roots =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            foreach (string root in roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    foreach (string lnkFile in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                    {
                        TryAddFromShortcut(entries, lnkFile);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("扫描开始菜单失败: " + root + " -> " + ex.Message);
                }
            }
        }

        private static void ScanRegistry(Dictionary<string, ScanEntry> entries)
        {
            string[] uninstallRoots =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (string root in uninstallRoots)
            {
                ScanRegistryRoot(entries, Registry.LocalMachine, root);
                ScanRegistryRoot(entries, Registry.CurrentUser, root);
            }
        }

        private static void ScanRegistryRoot(Dictionary<string, ScanEntry> entries, RegistryKey hive, string subKeyPath)
        {
            using (RegistryKey uninstallKey = hive.OpenSubKey(subKeyPath))
            {
                if (uninstallKey == null)
                {
                    return;
                }

                foreach (string subKeyName in uninstallKey.GetSubKeyNames())
                {
                    try
                    {
                        using (RegistryKey appKey = uninstallKey.OpenSubKey(subKeyName))
                        {
                            if (appKey == null)
                            {
                                continue;
                            }

                            string displayName = appKey.GetValue("DisplayName") as string;
                            if (string.IsNullOrWhiteSpace(displayName) || ShouldSkipDisplayName(displayName))
                            {
                                continue;
                            }

                            string installLocation = appKey.GetValue("InstallLocation") as string;
                            string displayIcon = appKey.GetValue("DisplayIcon") as string;
                            string exePath = ResolveExecutablePath(displayIcon, installLocation);
                            if (string.IsNullOrWhiteSpace(exePath))
                            {
                                continue;
                            }

                            TryAddEntry(entries, exePath, displayName.Trim(), sourcePriority: 1);
                        }
                    }
                    catch
                    {
                        // 忽略单个注册表项异常
                    }
                }
            }
        }

        private static void TryAddFromShortcut(Dictionary<string, ScanEntry> entries, string lnkFile)
        {
            try
            {
                string shortcutName = CleanShortcutName(Path.GetFileNameWithoutExtension(lnkFile));
                if (ShouldSkipShortcutName(shortcutName))
                {
                    return;
                }

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    return;
                }

                dynamic shell = Activator.CreateInstance(shellType);
                dynamic shortcut = shell.CreateShortcut(lnkFile);
                string targetPath = shortcut.TargetPath as string;
                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    return;
                }

                TryAddEntry(entries, targetPath, shortcutName, sourcePriority: 0);
            }
            catch
            {
                // 忽略损坏的快捷方式
            }
        }

        private static void TryAddEntry(Dictionary<string, ScanEntry> entries, string filePath, string displayName, int sourcePriority)
        {
            string normalizedPath = NormalizeExecutablePath(filePath);
            if (string.IsNullOrWhiteSpace(normalizedPath) || !File.Exists(normalizedPath))
            {
                return;
            }

            if (!normalizedPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (ShouldSkipExecutable(normalizedPath))
            {
                return;
            }

            string processName = Path.GetFileNameWithoutExtension(normalizedPath);
            if (string.IsNullOrWhiteSpace(processName))
            {
                return;
            }

            string friendlyName = ResolveFriendlyName(normalizedPath, processName, displayName, preferVersionInfo: sourcePriority > 0);
            if (!IsUsableDisplayName(friendlyName, processName))
            {
                friendlyName = displayName?.Trim();
            }

            if (string.IsNullOrWhiteSpace(friendlyName))
            {
                return;
            }

            if (entries.TryGetValue(processName, out ScanEntry existing))
            {
                if (ShouldReplaceEntry(existing, sourcePriority, friendlyName, normalizedPath))
                {
                    existing.ExePath = normalizedPath;
                    existing.DisplayName = friendlyName;
                    existing.SourcePriority = sourcePriority;
                }

                return;
            }

            entries[processName] = new ScanEntry
            {
                ProcessName = processName,
                ExePath = normalizedPath,
                DisplayName = friendlyName,
                SourcePriority = sourcePriority
            };
        }

        private static bool ShouldReplaceEntry(ScanEntry existing, int sourcePriority, string displayName, string exePath)
        {
            if (sourcePriority < existing.SourcePriority)
            {
                return true;
            }

            if (sourcePriority > existing.SourcePriority)
            {
                return false;
            }

            int newScore = ScoreDisplayName(displayName);
            int oldScore = ScoreDisplayName(existing.DisplayName);
            if (newScore != oldScore)
            {
                return newScore > oldScore;
            }

            return ScoreExecutablePath(exePath) > ScoreExecutablePath(existing.ExePath);
        }

        private static int ScoreDisplayName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return -100;
            }

            string lower = name.ToLowerInvariant();
            if (ShouldSkipShortcutName(name))
            {
                return -100;
            }

            if (lower.Contains("uninstall") || lower.Contains("卸载"))
            {
                return -50;
            }

            if (lower.Contains("64-bit") || lower.Contains("32-bit") || lower.Contains("x64") || lower.Contains("x86"))
            {
                return 10;
            }

            if (lower.Contains("setup") || lower.Contains("installer") || lower.Contains("update"))
            {
                return 0;
            }

            return 20;
        }

        private static int ScoreExecutablePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return 0;
            }

            string lower = path.ToLowerInvariant();
            if (lower.Contains("\\windows\\"))
            {
                return -20;
            }

            if (lower.Contains("\\program files"))
            {
                return 10;
            }

            if (lower.Contains("\\appdata\\local\\"))
            {
                return 5;
            }

            return 0;
        }

        private static string CleanShortcutName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            return name.Trim();
        }

        private static bool ShouldSkipShortcutName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return true;
            }

            string lower = name.ToLowerInvariant();
            string[] blocked =
            {
                "uninstall", "卸载", "remove", "help", "帮助", "documentation", "文档",
                "readme", "license", "website", "网站", "repair", "修复", "maintenance",
                "debug", "console", "command prompt", "命令提示符", "powershell",
                "windows powershell", "运行", "run"
            };

            foreach (string token in blocked)
            {
                if (lower.Contains(token))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ResolveFriendlyName(string exePath, string processName, string preferredName, bool preferVersionInfo)
        {
            if (IsUsableDisplayName(preferredName, processName))
            {
                return preferredName.Trim();
            }

            if (preferVersionInfo)
            {
                try
                {
                    FileVersionInfo info = FileVersionInfo.GetVersionInfo(exePath);
                    if (IsUsableDisplayName(info.FileDescription, processName))
                    {
                        return info.FileDescription.Trim();
                    }
                }
                catch
                {
                    // 忽略
                }
            }

            return processName;
        }

        private static bool IsBetterDescription(string current, string candidate, string processName)
        {
            if (!IsUsableDisplayName(candidate, processName))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(current))
            {
                return true;
            }

            if (string.Equals(current, processName, StringComparison.OrdinalIgnoreCase))
            {
                return !string.Equals(candidate, processName, StringComparison.OrdinalIgnoreCase);
            }

            return ScoreDisplayName(candidate) > ScoreDisplayName(current);
        }

        private static bool IsUsableDisplayName(string name, string processName)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            name = name.Trim();
            if (string.Equals(name, processName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (name.IndexOf('_') >= 0 && name.IndexOf('!') >= 0)
            {
                return false;
            }

            string lower = name.ToLowerInvariant();
            if (lower == "application"
                || lower == "program"
                || lower == "launcher"
                || lower == "client"
                || lower == "main"
                || lower == "app"
                || lower == "service"
                || lower == "agent"
                || lower == "manager")
            {
                return false;
            }

            return true;
        }

        private static string ResolveExecutablePath(string displayIcon, string installLocation)
        {
            string candidate = NormalizeExecutablePath(displayIcon);
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }

            if (!string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
            {
                try
                {
                    string[] preferredNames = { "launcher.exe", "main.exe", processLikeName(installLocation) };
                    foreach (string preferred in preferredNames)
                    {
                        if (string.IsNullOrWhiteSpace(preferred))
                        {
                            continue;
                        }

                        string preferredPath = Path.Combine(installLocation, preferred);
                        if (File.Exists(preferredPath) && !ShouldSkipExecutable(preferredPath))
                        {
                            return preferredPath;
                        }
                    }

                    return Directory.EnumerateFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly)
                        .Where(path => !ShouldSkipExecutable(path))
                        .OrderByDescending(ScoreExecutablePath)
                        .FirstOrDefault();
                }
                catch
                {
                    // 忽略
                }
            }

            return null;

            string processLikeName(string location)
            {
                string folder = Path.GetFileName(location.TrimEnd('\\'));
                return string.IsNullOrWhiteSpace(folder) ? null : folder + ".exe";
            }
        }

        private static string NormalizeExecutablePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string normalized = path.Trim().Trim('"');
            int commaIndex = normalized.IndexOf(',');
            if (commaIndex > 0)
            {
                normalized = normalized.Substring(0, commaIndex).Trim().Trim('"');
            }

            if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            if (Directory.Exists(normalized))
            {
                try
                {
                    return Directory.EnumerateFiles(normalized, "*.exe", SearchOption.TopDirectoryOnly)
                        .FirstOrDefault(item => !ShouldSkipExecutable(item));
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        private static bool ShouldSkipDisplayName(string displayName)
        {
            if (displayName.IndexOf("update", StringComparison.OrdinalIgnoreCase) >= 0
                && displayName.IndexOf("for", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return displayName.StartsWith("Microsoft Visual C++", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldSkipExecutable(string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return true;
            }

            string lower = fileName.ToLowerInvariant();
            string processName = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();

            string[] blockedExeNames =
            {
                "uninstall.exe", "unins000.exe", "setup.exe", "install.exe", "update.exe",
                "helper.exe", "launcher.exe", "elevated.exe", "crashhandler.exe",
                "notification_helper.exe", "squirrel.exe", "updateproxy.exe"
            };

            if (blockedExeNames.Contains(lower))
            {
                return true;
            }

            string[] blockedTokens =
            {
                "uninstall", "setup", "installer", "update", "updater", "helper", "crash",
                "service", "broker", "agent", "worker", "renderer", "gpu", "proxy",
                "maintenance", "repair", "redist", "vcredist", "dxsetup"
            };

            foreach (string token in blockedTokens)
            {
                if (processName.Contains(token))
                {
                    return true;
                }
            }

            string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
            if (directory.IndexOf("\\Windows\\", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            string[] blockedTargets =
            {
                "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe",
                "mshta.exe", "control.exe", "mmc.exe", "explorer.exe", "notepad.exe"
            };

            if (blockedTargets.Contains(lower))
            {
                return true;
            }

            return false;
        }

        private static AppModel CloneApp(AppModel app)
        {
            return new AppModel
            {
                ID = app.ID,
                Name = app.Name,
                Alias = app.Alias,
                Description = app.Description,
                File = app.File,
                CategoryID = app.CategoryID,
                IconFile = app.IconFile,
                TotalTime = app.TotalTime
            };
        }
    }
}
