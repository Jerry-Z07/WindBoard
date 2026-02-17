using System;
using System.Collections.Generic;
using System.Linq;

namespace WindBoard.Settings
{
    /// <summary>
    /// 设置对象的深拷贝辅助。
    ///
    /// 说明：
    /// - 主要用于“保存快照”：避免并发保存时引用被外部修改。
    /// - 该拷贝不负责做归一化（例如修正非法 HEX），归一化由 Store/Service 统一处理。
    /// </summary>
    internal static class AppSettingsCloner
    {
        internal static AppSettings Clone(AppSettings settings)
        {
            if (settings is null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            return new AppSettings
            {
                General = new GeneralSettings
                {
                    LanguagePreference = settings.General?.LanguagePreference ?? AppLanguagePreferenceParser.SystemValue,
                    Camouflage = new CamouflageSettings
                    {
                        Enabled = settings.General?.Camouflage?.Enabled ?? false,
                        Title = settings.General?.Camouflage?.Title ?? string.Empty,
                        SourcePath = settings.General?.Camouflage?.SourcePath ?? string.Empty,
                        IconCachePath = settings.General?.Camouflage?.IconCachePath ?? string.Empty,
                        ShortcutLastGeneratedSignature = settings.General?.Camouflage?.ShortcutLastGeneratedSignature ?? string.Empty,
                        ShortcutLastGeneratedPath = settings.General?.Camouflage?.ShortcutLastGeneratedPath ?? string.Empty,
                    },
                    Updates = new UpdateSettings
                    {
                        AutoCheckInterval = settings.General?.Updates?.AutoCheckInterval ?? UpdateCheckIntervalParser.WeeklyValue,
                        LastCheckUtc = settings.General?.Updates?.LastCheckUtc,
                        LastNotifiedVersion = settings.General?.Updates?.LastNotifiedVersion ?? string.Empty,
                        DownloadSourcePolicy = settings.General?.Updates?.DownloadSourcePolicy ?? DownloadSourcePolicyParser.AutoValue,
                        DownloadSourceId = settings.General?.Updates?.DownloadSourceId ?? DownloadSourceIdParser.GithubValue,
                        DownloadSourceLastTestUtc = settings.General?.Updates?.DownloadSourceLastTestUtc,
                    },
                },
                Appearance = new AppearanceSettings
                {
                    CanvasBackgroundHex = settings.Appearance?.CanvasBackgroundHex ?? ColorHex.DefaultCanvasBackgroundHex,
                    ElementCardTheme = settings.Appearance?.ElementCardTheme ?? ElementCardThemeParser.DarkValue,
                },
                Dock = new DockSettings
                {
                    LeftOrder = new List<string>(settings.Dock?.LeftOrder ?? DockSettingsDefaults.LeftOrder),
                    ToolsOrder = new List<string>(settings.Dock?.ToolsOrder ?? DockSettingsDefaults.ToolsOrder),
                    UndoRedoOrder = new List<string>(settings.Dock?.UndoRedoOrder ?? DockSettingsDefaults.UndoRedoOrder),
                    PagesOrder = new List<string>(settings.Dock?.PagesOrder ?? DockSettingsDefaults.PagesOrder),
                    IsUndoRedoVisible = settings.Dock?.IsUndoRedoVisible ?? true,
                    IsShortcutDocksVisible = settings.Dock?.IsShortcutDocksVisible ?? false,
                    ShortcutItems = settings.Dock?.ShortcutItems is null
                        ? new List<ShortcutDockItemSettings>()
                        : settings.Dock.ShortcutItems.Select(i => new ShortcutDockItemSettings
                        {
                            Id = i.Id,
                            Side = i.Side,
                            Type = i.Type,
                            DisplayName = i.DisplayName,
                            Path = i.Path,
                            Arguments = i.Arguments,
                            IconSource = i.IconSource,
                            IconPath = i.IconPath,
                            IconSymbol = i.IconSymbol,
                        }).ToList(),
                },
                Writing = new WritingSettings
                {
                    Pen = new PenSettings
                    {
                        PaletteHexes = new List<string?>(
                            settings.Writing?.Pen?.PaletteHexes ?? PenSettingsDefaults.DefaultPaletteHexes),
                        ThicknessPresets = new List<float>(
                            settings.Writing?.Pen?.ThicknessPresets ?? PenSettingsDefaults.DefaultThicknessPresets),
                        UseThicknessSlider = settings.Writing?.Pen?.UseThicknessSlider ?? false,
                    },
                },
                KeyboardShortcuts = new KeyboardShortcutsSettings
                {
                    ConflictReminderEnabled = settings.KeyboardShortcuts?.ConflictReminderEnabled ?? true,
                    Undo = settings.KeyboardShortcuts?.Undo ?? KeyboardShortcutsDefaults.Undo,
                    Redo = settings.KeyboardShortcuts?.Redo ?? KeyboardShortcutsDefaults.Redo,
                },
                Diagnostics = new DiagnosticsSettings
                {
                    Logging = new LoggingSettings
                    {
                        FileEnabled = settings.Diagnostics?.Logging?.FileEnabled ?? true,
                        MinimumLevel = settings.Diagnostics?.Logging?.MinimumLevel ?? "Information",
                        RetentionDays = settings.Diagnostics?.Logging?.RetentionDays ?? 14,
                    },
                },
            };
        }
    }
}
