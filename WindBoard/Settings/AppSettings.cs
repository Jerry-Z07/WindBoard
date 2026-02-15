using System.Collections.Generic;

namespace WindBoard.Settings
{
    /// <summary>
    /// 应用设置根对象（JSON 文件的顶层结构）。
    /// </summary>
    internal sealed class AppSettings
    {
        public GeneralSettings General { get; set; } = new();

        public AppearanceSettings Appearance { get; set; } = new();

        public DockSettings Dock { get; set; } = new();

        public WritingSettings Writing { get; set; } = new();

        public KeyboardShortcutsSettings KeyboardShortcuts { get; set; } = new();

        public DiagnosticsSettings Diagnostics { get; set; } = new();
    }

    internal sealed class GeneralSettings
    {
        public CamouflageSettings Camouflage { get; set; } = new();

        public UpdateSettings Updates { get; set; } = new();
    }

    internal sealed class CamouflageSettings
    {
        /// <summary>
        /// 伪装：是否启用。
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// 伪装：自定义窗口标题。
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// 伪装：图标来源路径（.exe/.ico/.png/.jpg 等）。
        /// </summary>
        public string SourcePath { get; set; } = string.Empty;

        /// <summary>
        /// 伪装：缓存生成的 .ico 路径（供窗口/快捷方式复用）。
        /// </summary>
        public string IconCachePath { get; set; } = string.Empty;

        /// <summary>
        /// 伪装：桌面快捷方式“最后生成时”的签名（用于避免每次启动都自动刷新快捷方式）。
        /// </summary>
        public string ShortcutLastGeneratedSignature { get; set; } = string.Empty;

        /// <summary>
        /// 伪装：桌面快捷方式“最后生成时”的完整路径（用于标题变化时删除/重命名旧快捷方式）。
        /// </summary>
        public string ShortcutLastGeneratedPath { get; set; } = string.Empty;
    }

    /// <summary>
    /// 更新相关设置（仅存储偏好，更新逻辑后续接入）。
    /// </summary>
    internal sealed class UpdateSettings
    {
        /// <summary>
        /// 自动检查更新频率（weekly/biweekly/monthly/never）。
        /// </summary>
        public string AutoCheckInterval { get; set; } = UpdateCheckIntervalParser.WeeklyValue;
    }

    internal sealed class AppearanceSettings
    {
        /// <summary>
        /// 画布背景色（#RRGGBB 或 #AARRGGBB）。
        /// </summary>
        public string CanvasBackgroundHex { get; set; } = ColorHex.DefaultCanvasBackgroundHex;

        /// <summary>
        /// 元素卡片主题（深/浅）：用于导入的图片/文件/文本/链接等“卡片”外观。
        /// </summary>
        /// <remarks>
        /// 约定：持久化为字符串，便于用户手动编辑 settings.json。
        /// - dark：深色卡片（默认）
        /// - light：浅色卡片
        /// </remarks>
        public string ElementCardTheme { get; set; } = ElementCardThemeParser.DarkValue;
    }

    internal sealed class WritingSettings
    {
        public PenSettings Pen { get; set; } = new();
    }

    internal sealed class PenSettings
    {
        /// <summary>
        /// 画笔颜色面板的固定色板（长度即显示数量）。
        /// 
        /// 约定：
        /// - 支持 null：表示“空色块”（未配置颜色）
        /// - 非空时为 #RRGGBB 或 #AARRGGBB
        /// </summary>
        public List<string?> PaletteHexes { get; set; } = new(PenSettingsDefaults.DefaultPaletteHexes);

        /// <summary>
        /// 画笔粗细三档预设值（细/中/粗）。
        /// </summary>
        public List<float> ThicknessPresets { get; set; } = new(PenSettingsDefaults.DefaultThicknessPresets);

        /// <summary>
        /// 是否使用滑条替代“三档粗细”。
        /// </summary>
        public bool UseThicknessSlider { get; set; }
    }

    /// <summary>
    /// 诊断设置：日志、故障排查等。
    /// </summary>
    internal sealed class DiagnosticsSettings
    {
        public LoggingSettings Logging { get; set; } = new();
    }

    /// <summary>
    /// 日志设置（落盘到 settings.json，便于用户在无 UI 的情况下也能调整）。
    /// </summary>
    internal sealed class LoggingSettings
    {
        /// <summary>
        /// 是否启用写入到文件。
        /// </summary>
        public bool FileEnabled { get; set; } = true;

        /// <summary>
        /// 最低输出级别：Trace/Debug/Information/Warning/Error/Critical（大小写不敏感）。
        /// </summary>
        public string MinimumLevel { get; set; } = "Information";

        /// <summary>
        /// 日志文件保留天数（<=0 表示不清理）。
        /// </summary>
        public int RetentionDays { get; set; } = 14;
    }
}
