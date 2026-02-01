using System.Collections.Generic;

namespace WindBoard.Settings
{
    /// <summary>
    /// 应用设置根对象（JSON 文件的顶层结构）。
    /// </summary>
    internal sealed class AppSettings
    {
        public AppearanceSettings Appearance { get; set; } = new();

        public DockSettings Dock { get; set; } = new();

        public WritingSettings Writing { get; set; } = new();
    }

    internal sealed class AppearanceSettings
    {
        /// <summary>
        /// 画布背景色（#RRGGBB 或 #AARRGGBB）。
        /// </summary>
        public string CanvasBackgroundHex { get; set; } = ColorHex.DefaultCanvasBackgroundHex;
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
}
