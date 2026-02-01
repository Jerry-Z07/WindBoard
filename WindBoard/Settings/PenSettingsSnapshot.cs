using System.Collections.Generic;

namespace WindBoard.Settings
{
    /// <summary>
    /// 画笔设置的只读快照（供 UI 使用，避免直接暴露可变引用）。
    /// </summary>
    internal sealed class PenSettingsSnapshot
    {
        public required List<string?> PaletteHexes { get; init; }

        public required float[] ThicknessPresets { get; init; }

        public required bool UseThicknessSlider { get; init; }
    }
}

