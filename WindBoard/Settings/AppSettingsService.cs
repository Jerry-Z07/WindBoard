using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI;
using WindBoard.Features.Camouflage.Models;
using WindBoard.Features.Dock.Models;
using WindBoard.Features.Shortcuts.Models;
using WindBoard.Logging;

namespace WindBoard.Settings
{
    /// <summary>
    /// 应用级设置服务（单例）。
    /// 
    /// 职责：
    /// - 启动时加载设置
    /// - 运行时提供更新入口并广播变更事件
    /// - 对高频更新做保存防抖，减少频繁写磁盘
    /// </summary>
    internal sealed class AppSettingsService
    {
        private static readonly TimeSpan SaveDebounceDelay = TimeSpan.FromMilliseconds(350);

        internal static AppSettingsService Instance { get; } = new(AppSettingsStore.CreateDefault());

        private readonly object _gate = new();
        private readonly AppSettingsStore _store;
        private readonly SemaphoreSlim _ioGate = new(1, 1);
        private Timer? _saveDebounceTimer;
        private readonly List<KeyboardShortcutNormalizationIssue> _pendingKeyboardShortcutIssues = new();

        internal AppSettings Current { get; private set; } = new();

        /// <summary>
        /// settings.json 的完整路径（只读），供调试工具/故障排查使用。
        /// </summary>
        internal string SettingsFilePath => _store.FilePath;

        internal event EventHandler? Changed;

        internal AppSettingsService(AppSettingsStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        internal void Load()
        {
            var report = new SettingsNormalizationReport();
            lock (_gate)
            {
                Current = _store.LoadOrDefault(report);

                // 启动加载阶段的归一化问题先缓存起来：由主窗口在首次应用设置时统一提示一次。
                _pendingKeyboardShortcutIssues.Clear();
                if (report.KeyboardShortcutIssues.Count > 0)
                {
                    _pendingKeyboardShortcutIssues.AddRange(report.KeyboardShortcutIssues);
                }
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }

        internal Color GetCanvasBackgroundColor()
        {
            string? hex;
            lock (_gate)
            {
                hex = Current.Appearance?.CanvasBackgroundHex;
            }

            return ColorHex.ParseOrDefault(hex, ColorHex.DefaultCanvasBackgroundColor);
        }

        internal ElementCardTheme GetElementCardTheme()
        {
            string? value;
            lock (_gate)
            {
                value = Current.Appearance?.ElementCardTheme;
            }

            return ElementCardThemeParser.TryParse(value, out ElementCardTheme theme)
                ? theme
                : ElementCardTheme.Dark;
        }

        internal CamouflageSettingsSnapshot GetCamouflageSettingsSnapshot()
        {
            lock (_gate)
            {
                CamouflageSettings? camo = Current.General?.Camouflage;
                return new CamouflageSettingsSnapshot
                {
                    Enabled = camo?.Enabled ?? false,
                    Title = camo?.Title ?? string.Empty,
                    SourcePath = camo?.SourcePath ?? string.Empty,
                    IconCachePath = camo?.IconCachePath ?? string.Empty,
                    ShortcutLastGeneratedSignature = camo?.ShortcutLastGeneratedSignature ?? string.Empty,
                    ShortcutLastGeneratedPath = camo?.ShortcutLastGeneratedPath ?? string.Empty,
                };
            }
        }

        internal string GetLanguagePreference()
        {
            string? value;
            lock (_gate)
            {
                value = Current.General?.LanguagePreference;
            }

            return AppLanguagePreferenceParser.NormalizeOrDefault(value);
        }

        internal StartupWindowMode GetStartupWindowMode()
        {
            string? value;
            lock (_gate)
            {
                value = Current.General?.StartupWindowMode;
            }

            return StartupWindowModeParser.TryParse(value, out StartupWindowMode mode)
                ? mode
                : StartupWindowMode.Windowed;
        }

        internal bool GetEnterScreenAnnotationWhenMinimized()
        {
            lock (_gate)
            {
                // 历史配置缺失该字段时按默认开启处理，避免升级后行为被意外关闭。
                return Current.General?.EnterScreenAnnotationWhenMinimized ?? true;
            }
        }

        internal void SetLanguagePreference(string? settingValue)
        {
            string normalized = AppLanguagePreferenceParser.NormalizeOrDefault(settingValue);
            Update(s =>
            {
                s.General ??= new GeneralSettings();
                s.General.LanguagePreference = normalized;
            });
        }

        internal void SetStartupWindowMode(StartupWindowMode mode)
        {
            Update(s =>
            {
                s.General ??= new GeneralSettings();
                s.General.StartupWindowMode = StartupWindowModeParser.ToSettingValue(mode);
            });
        }

        internal void SetEnterScreenAnnotationWhenMinimized(bool enabled)
        {
            Update(s =>
            {
                s.General ??= new GeneralSettings();
                s.General.EnterScreenAnnotationWhenMinimized = enabled;
            });
        }

        internal UpdateCheckInterval GetUpdateCheckInterval()
        {
            string? value;
            lock (_gate)
            {
                value = Current.General?.Updates?.AutoCheckInterval;
            }

            return UpdateCheckIntervalParser.TryParse(value, out UpdateCheckInterval interval)
                ? interval
                : UpdateCheckInterval.Weekly;
        }

        internal UpdatePreferencesSnapshot GetUpdatePreferencesSnapshot()
        {
            lock (_gate)
            {
                UpdateSettings? updates = Current.General?.Updates;

                string? intervalValue = updates?.AutoCheckInterval;
                UpdateCheckInterval interval = UpdateCheckIntervalParser.TryParse(intervalValue, out UpdateCheckInterval parsed)
                    ? parsed
                    : UpdateCheckInterval.Weekly;

                return new UpdatePreferencesSnapshot
                {
                    AutoCheckInterval = interval,
                    LastCheckUtc = updates?.LastCheckUtc,
                    LastNotifiedVersion = (updates?.LastNotifiedVersion ?? string.Empty).Trim(),
                };
            }
        }

        internal DownloadSourcePreferencesSnapshot GetUpdateDownloadSourcePreferencesSnapshot()
        {
            lock (_gate)
            {
                UpdateSettings? updates = Current.General?.Updates;

                string? policyValue = updates?.DownloadSourcePolicy;
                DownloadSourcePolicy policy = DownloadSourcePolicyParser.TryParse(policyValue, out DownloadSourcePolicy parsedPolicy)
                    ? parsedPolicy
                    : DownloadSourcePolicy.Auto;

                string? idValue = updates?.DownloadSourceId;
                DownloadSourceId id = DownloadSourceIdParser.TryParse(idValue, out DownloadSourceId parsedId)
                    ? parsedId
                    : DownloadSourceId.Github;

                return new DownloadSourcePreferencesSnapshot
                {
                    Policy = policy,
                    SourceId = id,
                    LastTestUtc = updates?.DownloadSourceLastTestUtc,
                };
            }
        }

        internal void SetUpdateCheckInterval(UpdateCheckInterval interval)
        {
            Update(s =>
            {
                s.General ??= new GeneralSettings();
                s.General.Updates ??= new UpdateSettings();
                s.General.Updates.AutoCheckInterval = UpdateCheckIntervalParser.ToSettingValue(interval);
            });
        }

        internal void SetUpdateLastCheckUtc(DateTimeOffset utc)
        {
            Update(s =>
            {
                s.General ??= new GeneralSettings();
                s.General.Updates ??= new UpdateSettings();
                s.General.Updates.LastCheckUtc = utc;
            });
        }

        internal void SetUpdateLastNotifiedVersion(string version)
        {
            string v = (version ?? string.Empty).Trim();
            Update(s =>
            {
                s.General ??= new GeneralSettings();
                s.General.Updates ??= new UpdateSettings();
                s.General.Updates.LastNotifiedVersion = v;
            });
        }

        internal void SetUpdateDownloadSourcePolicy(DownloadSourcePolicy policy)
        {
            Update(s =>
            {
                s.General ??= new GeneralSettings();
                s.General.Updates ??= new UpdateSettings();
                s.General.Updates.DownloadSourcePolicy = DownloadSourcePolicyParser.ToSettingValue(policy);
            });
        }

        internal void SetUpdateDownloadSourceId(DownloadSourceId id)
        {
            Update(s =>
            {
                s.General ??= new GeneralSettings();
                s.General.Updates ??= new UpdateSettings();
                s.General.Updates.DownloadSourceId = DownloadSourceIdParser.ToSettingValue(id);
            });
        }

        internal void SetUpdateDownloadSourceLastTestUtc(DateTimeOffset? utc)
        {
            Update(s =>
            {
                s.General ??= new GeneralSettings();
                s.General.Updates ??= new UpdateSettings();
                s.General.Updates.DownloadSourceLastTestUtc = utc;
            });
        }

        internal DockSettings GetDockSettingsSnapshot()
        {
            lock (_gate)
            {
                // 注意：DockSettings 内包含引用类型列表，这里返回“深拷贝快照”，避免外部误改内部状态。
                List<ShortcutDockItemSettings> shortcutItems = new();
                if (Current.Dock?.ShortcutItems is not null)
                {
                    foreach (ShortcutDockItemSettings item in Current.Dock.ShortcutItems)
                    {
                        shortcutItems.Add(new ShortcutDockItemSettings
                        {
                            Id = item.Id,
                            Side = item.Side,
                            Type = item.Type,
                            DisplayName = item.DisplayName,
                            Path = item.Path,
                            Arguments = item.Arguments,
                            IconSource = item.IconSource,
                            IconPath = item.IconPath,
                            IconSymbol = item.IconSymbol,
                        });
                    }
                }

                return new DockSettings
                {
                    LeftOrder = new List<string>(Current.Dock?.LeftOrder ?? DockSettingsDefaults.LeftOrder),
                    ToolsOrder = new List<string>(Current.Dock?.ToolsOrder ?? DockSettingsDefaults.ToolsOrder),
                    UndoRedoOrder = new List<string>(Current.Dock?.UndoRedoOrder ?? DockSettingsDefaults.UndoRedoOrder),
                    PagesOrder = new List<string>(Current.Dock?.PagesOrder ?? DockSettingsDefaults.PagesOrder),
                    IsUndoRedoVisible = Current.Dock?.IsUndoRedoVisible ?? true,
                    IsShortcutDocksVisible = Current.Dock?.IsShortcutDocksVisible ?? false,
                    ShortcutItems = shortcutItems,
                };
            }
        }

        internal PenSettingsSnapshot GetPenSettingsSnapshot()
        {
            lock (_gate)
            {
                PenSettings? pen = Current.Writing?.Pen;

                List<string?> palette = pen?.PaletteHexes is null
                    ? new List<string?>(PenSettingsDefaults.DefaultPaletteHexes)
                    : new List<string?>(pen.PaletteHexes);

                float[] thicknessPresets;
                if (pen?.ThicknessPresets is { Count: 3 } presets)
                {
                    thicknessPresets = [presets[0], presets[1], presets[2]];
                }
                else
                {
                    thicknessPresets =
                    [
                        PenSettingsDefaults.DefaultThicknessPresets[0],
                        PenSettingsDefaults.DefaultThicknessPresets[1],
                        PenSettingsDefaults.DefaultThicknessPresets[2],
                    ];
                }

                return new PenSettingsSnapshot
                {
                    PaletteHexes = palette,
                    ThicknessPresets = thicknessPresets,
                    UseThicknessSlider = pen?.UseThicknessSlider ?? false,
                };
            }
        }

        internal KeyboardShortcutsSnapshot GetKeyboardShortcutsSnapshot()
        {
            lock (_gate)
            {
                KeyboardShortcutsSettings? shortcuts = Current.KeyboardShortcuts;
                return new KeyboardShortcutsSnapshot
                {
                    Undo = shortcuts?.Undo ?? KeyboardShortcutsDefaults.Undo,
                    Redo = shortcuts?.Redo ?? KeyboardShortcutsDefaults.Redo,
                };
            }
        }

        internal bool GetShortcutConflictReminderEnabled()
        {
            lock (_gate)
            {
                return Current.KeyboardShortcuts?.ConflictReminderEnabled ?? true;
            }
        }

        internal IReadOnlyList<KeyboardShortcutNormalizationIssue> ConsumeKeyboardShortcutIssues()
        {
            lock (_gate)
            {
                if (_pendingKeyboardShortcutIssues.Count == 0)
                {
                    return Array.Empty<KeyboardShortcutNormalizationIssue>();
                }

                // 返回快照并清空：确保“提醒一次”。
                var snapshot = new List<KeyboardShortcutNormalizationIssue>(_pendingKeyboardShortcutIssues);
                _pendingKeyboardShortcutIssues.Clear();
                return snapshot;
            }
        }

        internal LoggingSettingsSnapshot GetLoggingSettingsSnapshot()
        {
            lock (_gate)
            {
                LoggingSettings? logging = Current.Diagnostics?.Logging;

                if (!AppLogLevelParser.TryParse(logging?.MinimumLevel, out AppLogLevel level))
                {
                    level = AppLogLevel.Information;
                }

                return new LoggingSettingsSnapshot
                {
                    FileEnabled = logging?.FileEnabled ?? true,
                    MinimumLevel = level,
                    RetentionDays = logging?.RetentionDays ?? 14,
                };
            }
        }

        internal void Update(Action<AppSettings> update)
        {
            if (update is null)
            {
                throw new ArgumentNullException(nameof(update));
            }

            var report = new SettingsNormalizationReport();
            lock (_gate)
            {
                update(Current);
                AppSettingsStore.NormalizeInPlace(Current, report);

                // 记录归一化产生的问题：由 UI 统一提示（可在设置中关闭）。
                if (report.KeyboardShortcutIssues.Count > 0)
                {
                    _pendingKeyboardShortcutIssues.AddRange(report.KeyboardShortcutIssues);
                }
            }

            Changed?.Invoke(this, EventArgs.Empty);
            RequestSaveDebounced();
        }

        internal void SetShortcutConflictReminderEnabled(bool enabled)
        {
            Update(s =>
            {
                s.KeyboardShortcuts ??= new KeyboardShortcutsSettings();
                s.KeyboardShortcuts.ConflictReminderEnabled = enabled;
            });
        }

        internal async Task ExportToFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            string path = NormalizeExternalFilePath(filePath);

            AppSettings snapshot;
            lock (_gate)
            {
                snapshot = AppSettingsCloner.Clone(Current);
            }

            await SaveInternalAsync(snapshot, new AppSettingsStore(path), cancellationToken).ConfigureAwait(false);
        }

        internal async Task ImportFromFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            string path = NormalizeExternalFilePath(filePath);
            string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);

            var report = new SettingsNormalizationReport();
            AppSettings imported = AppSettingsStore.Deserialize(json, report);
            ReplaceAllCore(imported, report, requestSaveDebounced: false);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        internal async Task ResetToDefaultsAsync(CancellationToken cancellationToken = default)
        {
            var report = new SettingsNormalizationReport();
            ReplaceAllCore(new AppSettings(), report, requestSaveDebounced: false);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        internal Task SaveAsync(CancellationToken cancellationToken = default)
        {
            AppSettings snapshot;
            lock (_gate)
            {
                // 这里只做“快照”，不在这里做归一化：
                // - 归一化由 Update/Load 保证；
                // - 落盘前 Store.Save 会再次归一化兜底。
                snapshot = AppSettingsCloner.Clone(Current);
            }

            return SaveInternalAsync(snapshot, _store, cancellationToken);
        }

        private void RequestSaveDebounced()
        {
            lock (_gate)
            {
                // 高频 Update 如果用 CancellationToken + Task.Delay 做防抖，
                // 频繁取消会抛出 TaskCanceledException（即使被 catch，调试输出仍会提示“异常已引发”）。
                // 这里改为 Timer 防抖：仅在一段时间内没有新的更新时才触发保存，不产生取消异常噪音。
                _saveDebounceTimer ??= new Timer(
                    static state => ((AppSettingsService)state!).OnSaveDebounceTimerTick(),
                    this,
                    Timeout.InfiniteTimeSpan,
                    Timeout.InfiniteTimeSpan);

                _saveDebounceTimer.Change(SaveDebounceDelay, Timeout.InfiniteTimeSpan);
            }
        }

        private void OnSaveDebounceTimerTick()
        {
            // Timer 回调不能直接 async/await：转为后台任务，并确保异常被观察到（避免影响主流程）。
            _ = Task.Run(async () =>
            {
                try
                {
                    await SaveAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // 设置保存失败不应影响应用主流程，但必须记录日志，便于排查用户环境问题（权限/磁盘/JSON 等）。
                    AppLog.Error("Settings", $"设置保存失败：path='{_store.FilePath}'", ex);
                }
            });
        }

        private void ReplaceAllCore(AppSettings settings, SettingsNormalizationReport report, bool requestSaveDebounced)
        {
            if (settings is null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (report is null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            // 先复制再归一化，避免外部继续持有引用并修改内部状态。
            AppSettings next = AppSettingsCloner.Clone(settings);
            AppSettingsStore.NormalizeInPlace(next, report);
            string languagePreference = AppLanguagePreferenceParser.NormalizeOrDefault(next.General?.LanguagePreference);

            lock (_gate)
            {
                Current = next;

                // 导入/恢复默认后，旧的快捷键归一化问题不再相关，这里整体替换为新快照对应的问题列表。
                _pendingKeyboardShortcutIssues.Clear();
                if (report.KeyboardShortcutIssues.Count > 0)
                {
                    _pendingKeyboardShortcutIssues.AddRange(report.KeyboardShortcutIssues);
                }
            }

            // 导入/恢复默认属于整包替换，这里同步更新当前进程语言，避免设置值与运行时文化不一致。
            AppLanguageService.Apply(languagePreference);
            Changed?.Invoke(this, EventArgs.Empty);
            if (requestSaveDebounced)
            {
                RequestSaveDebounced();
            }
        }

        private static string NormalizeExternalFilePath(string filePath)
        {
            string path = (filePath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("文件路径不能为空。", nameof(filePath));
            }

            return path;
        }

        private async Task SaveInternalAsync(AppSettings snapshot, AppSettingsStore targetStore, CancellationToken cancellationToken)
        {
            await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                targetStore.Save(snapshot);
            }
            finally
            {
                _ioGate.Release();
            }
        }

    }
}
