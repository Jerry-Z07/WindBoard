using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using WindBoard.Logging;

namespace WindBoard.Settings
{
    /// <summary>
    /// JSON 设置文件的读写与归一化。
    /// 
    /// 设计点：
    /// - 读取失败/JSON 损坏时回退默认值，避免影响启动
    /// - 保存使用临时文件替换，降低写入中断导致文件损坏的概率
    /// - 所有设置在加载/更新后都会做一次“归一化”，确保内存态与落盘态可用
    /// </summary>
    internal sealed class AppSettingsStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
        };

        internal string FilePath { get; }

        internal AppSettingsStore(string filePath)
        {
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        }

        internal static AppSettingsStore CreateDefault()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WindBoard");
            string path = Path.Combine(dir, "settings.json");
            return new AppSettingsStore(path);
        }

        internal AppSettings LoadOrDefault(SettingsNormalizationReport? report = null)
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return NormalizeInPlace(new AppSettings(), report);
                }

                string json = File.ReadAllText(FilePath);
                AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                return NormalizeInPlace(settings ?? new AppSettings(), report);
            }
            catch (Exception ex)
            {
                // 读取/解析失败时回退到默认值，避免启动崩溃。
                AppLog.Warn("Settings", $"读取/解析设置失败，已回退默认值：path='{FilePath}'", ex);
                return NormalizeInPlace(new AppSettings(), report);
            }
        }

        internal void Save(AppSettings settings)
        {
            if (settings is null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            AppSettings snapshot = CloneAndNormalize(settings);
            string json = JsonSerializer.Serialize(snapshot, JsonOptions);

            string? directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempPath = FilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, FilePath, overwrite: true);
        }

        /// <summary>
        /// 把设置对象归一化到“可用”的状态（补齐 null、修正非法值等）。
        /// </summary>
        internal static AppSettings NormalizeInPlace(AppSettings settings)
        {
            return NormalizeInPlace(settings, report: null);
        }

        /// <summary>
        /// 把设置对象归一化到“可用”的状态（补齐 null、修正非法值等），并可选输出报告。
        /// </summary>
        internal static AppSettings NormalizeInPlace(AppSettings settings, SettingsNormalizationReport? report)
        {
            if (settings is null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            settings.General ??= new GeneralSettings();
            settings.General.Camouflage ??= new CamouflageSettings();
            NormalizeCamouflageSettingsInPlace(settings.General.Camouflage);

            settings.Appearance ??= new AppearanceSettings();
            settings.Appearance.CanvasBackgroundHex = ColorHex.NormalizeToHexRgbOrDefault(
                settings.Appearance.CanvasBackgroundHex,
                ColorHex.DefaultCanvasBackgroundHex);
            if (!ElementCardThemeParser.TryParse(settings.Appearance.ElementCardTheme, out ElementCardTheme cardTheme))
            {
                cardTheme = ElementCardTheme.Dark;
            }
            settings.Appearance.ElementCardTheme = ElementCardThemeParser.ToSettingValue(cardTheme);

            settings.Dock ??= new DockSettings();
            NormalizeDockSettingsInPlace(settings.Dock);

            settings.Writing ??= new WritingSettings();
            settings.Writing.Pen ??= new PenSettings();
            NormalizePenSettingsInPlace(settings.Writing.Pen);

            settings.KeyboardShortcuts ??= new KeyboardShortcutsSettings();
            NormalizeKeyboardShortcutsInPlace(settings.KeyboardShortcuts, report);

            settings.Diagnostics ??= new DiagnosticsSettings();
            settings.Diagnostics.Logging ??= new LoggingSettings();
            NormalizeLoggingSettingsInPlace(settings.Diagnostics.Logging);
            return settings;
        }

        private static AppSettings CloneAndNormalize(AppSettings settings)
        {
            // 保存使用“快照 + 归一化”，避免并发保存时引用被外部修改，同时确保落盘数据可用。
            return NormalizeInPlace(AppSettingsCloner.Clone(settings));
        }

        private static void NormalizeDockSettingsInPlace(DockSettings settings)
        {
            settings.LeftOrder = NormalizeOrder(settings.LeftOrder, DockSettingsDefaults.LeftOrder);
            settings.ToolsOrder = NormalizeOrder(settings.ToolsOrder, DockSettingsDefaults.ToolsOrder);
            settings.UndoRedoOrder = NormalizeOrder(settings.UndoRedoOrder, DockSettingsDefaults.UndoRedoOrder);
            settings.PagesOrder = NormalizeOrder(settings.PagesOrder, DockSettingsDefaults.PagesOrder);

            NormalizeShortcutDockSettingsInPlace(settings);
        }

        private static void NormalizeShortcutDockSettingsInPlace(DockSettings settings)
        {
            // 快捷入口 Dock：
            // - 允许空 Path（便于设置页先“添加”，再补齐内容）
            // - 统一修正 Side/Type/IconSource 到合法值
            // - 保证 Id 稳定存在（用于编辑项定位）
            // - 限制最多 5 个
            settings.ShortcutItems ??= new List<ShortcutDockItemSettings>();

            var normalized = new List<ShortcutDockItemSettings>(capacity: 5);
            foreach (ShortcutDockItemSettings item in settings.ShortcutItems)
            {
                // 兼容 JSON 反序列化写入 null 元素的极端情况。
                if (item is null)
                {
                    continue;
                }

                if (normalized.Count >= 5)
                {
                    break;
                }

                string id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id.Trim();

                string side = (item.Side ?? string.Empty).Trim();
                if (!string.Equals(side, ShortcutDockSides.Left, StringComparison.Ordinal)
                    && !string.Equals(side, ShortcutDockSides.Right, StringComparison.Ordinal))
                {
                    side = ShortcutDockSides.Left;
                }

                string type = (item.Type ?? string.Empty).Trim();
                if (!string.Equals(type, ShortcutDockItemTypes.File, StringComparison.Ordinal)
                    && !string.Equals(type, ShortcutDockItemTypes.Link, StringComparison.Ordinal)
                    && !string.Equals(type, ShortcutDockItemTypes.Program, StringComparison.Ordinal))
                {
                    type = ShortcutDockItemTypes.File;
                }

                string? displayName = string.IsNullOrWhiteSpace(item.DisplayName) ? null : item.DisplayName.Trim();
                string path = (item.Path ?? string.Empty).Trim();

                string iconSource = (item.IconSource ?? string.Empty).Trim();
                if (!string.Equals(iconSource, ShortcutDockIconSources.Default, StringComparison.Ordinal)
                    && !string.Equals(iconSource, ShortcutDockIconSources.Icon, StringComparison.Ordinal)
                    && !string.Equals(iconSource, ShortcutDockIconSources.Font, StringComparison.Ordinal))
                {
                    iconSource = ShortcutDockIconSources.Default;
                }

                string? iconPath = string.IsNullOrWhiteSpace(item.IconPath) ? null : item.IconPath.Trim();
                string? iconSymbol = string.IsNullOrWhiteSpace(item.IconSymbol) ? null : item.IconSymbol.Trim();
                string? arguments = string.IsNullOrWhiteSpace(item.Arguments) ? null : item.Arguments;

                normalized.Add(new ShortcutDockItemSettings
                {
                    Id = id,
                    Side = side,
                    Type = type,
                    DisplayName = displayName,
                    Path = path,
                    Arguments = arguments,
                    IconSource = iconSource,
                    IconPath = iconPath,
                    IconSymbol = iconSymbol,
                });
            }

            settings.ShortcutItems = normalized;
        }

        private static void NormalizeCamouflageSettingsInPlace(CamouflageSettings settings)
        {
            // 伪装设置以“字符串清理”为主：空/null 一律归一化为 string.Empty，避免后续逻辑判空时 NRE。
            settings.Title = (settings.Title ?? string.Empty).Trim();
            settings.SourcePath = (settings.SourcePath ?? string.Empty).Trim();
            settings.IconCachePath = (settings.IconCachePath ?? string.Empty).Trim();
            settings.ShortcutLastGeneratedSignature = (settings.ShortcutLastGeneratedSignature ?? string.Empty).Trim();
            settings.ShortcutLastGeneratedPath = (settings.ShortcutLastGeneratedPath ?? string.Empty).Trim();
        }

        private static void NormalizePenSettingsInPlace(PenSettings settings)
        {
            // 色板：长度即数量，允许 null 表示“空色块”。
            settings.PaletteHexes = PenSettingsDefaults.NormalizePalette(settings.PaletteHexes);

            // 粗细：必须三档，且归一化为递增。
            settings.ThicknessPresets = PenSettingsDefaults.NormalizeThicknessPresets(settings.ThicknessPresets);
        }

        private static void NormalizeKeyboardShortcutsInPlace(KeyboardShortcutsSettings settings, SettingsNormalizationReport? report)
        {
            // 快捷键归一化：
            // - null -> string.Empty
            // - 允许空字符串表示禁用（不会自动回填）
            // - 非空则必须是“合法组合键”（并写回为规范格式）
            settings.Undo = NormalizeKeyboardShortcutOrDisable(slot: "Undo", settings.Undo, KeyboardShortcutsDefaults.Undo, report);
            settings.Redo = NormalizeKeyboardShortcutOrDisable(slot: "Redo", settings.Redo, KeyboardShortcutsDefaults.Redo, report);

            // 去重冲突：按 Undo -> Redo 的顺序处理。
            // 约定：同一组合键不允许绑定多个动作；后出现者自动清空（禁用）。
            if (!string.IsNullOrEmpty(settings.Undo))
            {
                if (string.Equals(settings.Redo, settings.Undo, StringComparison.Ordinal))
                {
                    report?.KeyboardShortcutIssues.Add(
                        new KeyboardShortcutNormalizationIssue
                        {
                            Slot = "Redo",
                            OldValue = settings.Redo,
                            NewValue = string.Empty,
                            Kind = KeyboardShortcutNormalizationIssueKind.ConflictDisabled,
                            ConflictWithSlot = "Undo",
                        });
                    settings.Redo = string.Empty;
                }
            }
        }

        private static string NormalizeKeyboardShortcutOrDisable(
            string slot,
            string? value,
            string defaultValue,
            SettingsNormalizationReport? report)
        {
            string text = (value ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            if (!KeyboardShortcutGesture.TryParse(text, out KeyboardShortcutGesture gesture) || !gesture.IsValidForApp())
            {
                // 非法值回退默认值，并写回规范格式，避免落盘脏数据影响后续 UI 展示与解析。
                if (KeyboardShortcutGesture.TryParse(defaultValue, out KeyboardShortcutGesture defaultGesture))
                {
                    string normalized = defaultGesture.ToSettingString();
                    report?.KeyboardShortcutIssues.Add(
                        new KeyboardShortcutNormalizationIssue
                        {
                            Slot = slot,
                            OldValue = text,
                            NewValue = normalized,
                            Kind = KeyboardShortcutNormalizationIssueKind.InvalidRevertedToDefault,
                        });
                    return normalized;
                }

                report?.KeyboardShortcutIssues.Add(
                    new KeyboardShortcutNormalizationIssue
                    {
                        Slot = slot,
                        OldValue = text,
                        NewValue = defaultValue,
                        Kind = KeyboardShortcutNormalizationIssueKind.InvalidRevertedToDefault,
                    });
                return defaultValue;
            }

            return gesture.ToSettingString();
        }

        private static void NormalizeLoggingSettingsInPlace(LoggingSettings settings)
        {
            // 说明：日志设置大多会被用户手工编辑，因此这里做“宽松解析 + 强归一化”。
            settings.MinimumLevel = (settings.MinimumLevel ?? string.Empty).Trim();
            if (!AppLogLevelParser.TryParse(settings.MinimumLevel, out AppLogLevel level))
            {
                level = AppLogLevel.Information;
            }

            // 统一落盘为枚举名称（Information/Warning/...），便于支持更多别名而不污染文件。
            settings.MinimumLevel = level.ToString();

            // RetentionDays：<=0 表示不清理；正数则做上限保护，避免误填极大值导致清理逻辑变慢。
            if (settings.RetentionDays > 365)
            {
                settings.RetentionDays = 365;
            }
        }

        private static List<string> NormalizeOrder(IEnumerable<string>? order, IReadOnlyList<string> defaults)
        {
            // 目标：
            // - 过滤未知项
            // - 去重（保留首次出现）
            // - 补齐缺失项（按默认顺序追加）
            HashSet<string> allowed = new(defaults, StringComparer.Ordinal);
            HashSet<string> seen = new(StringComparer.Ordinal);
            List<string> normalized = new();

            if (order is not null)
            {
                foreach (string? id in order)
                {
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    if (!allowed.Contains(id))
                    {
                        continue;
                    }

                    if (!seen.Add(id))
                    {
                        continue;
                    }

                    normalized.Add(id);
                }
            }

            foreach (string id in defaults)
            {
                if (seen.Add(id))
                {
                    normalized.Add(id);
                }
            }

            return normalized;
        }
    }
}
