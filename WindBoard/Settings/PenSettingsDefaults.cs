using System;
using System.Collections.Generic;

namespace WindBoard.Settings
{
    /// <summary>
    /// 画笔设置的默认值与归一化规则。
    /// </summary>
    internal static class PenSettingsDefaults
    {
        internal const int MinPaletteCount = 3;
        internal const int MaxPaletteCount = 24;

        internal static readonly IReadOnlyList<string?> DefaultPaletteHexes =
        [
            "#FFFFFF", // 白
            "#FF3B30", // 红
            "#FFCC00", // 黄
            "#34C759", // 绿
            "#32ADE6", // 青
            "#0A84FF", // 蓝
            "#AF52DE", // 紫
            "#FF2D55", // 粉
            "#000000", // 黑
        ];

        internal static readonly IReadOnlyList<float> DefaultThicknessPresets =
        [
            2.0f,
            3.0f,
            5.0f,
        ];

        internal static List<string?> NormalizePalette(IReadOnlyList<string?>? palette)
        {
            if (palette is null)
            {
                return new List<string?>(DefaultPaletteHexes);
            }

            int count = Math.Clamp(palette.Count, MinPaletteCount, MaxPaletteCount);
            var normalized = new List<string?>(capacity: count);

            for (int i = 0; i < count; i++)
            {
                if (i >= palette.Count)
                {
                    normalized.Add(null);
                    continue;
                }

                string? hex = palette[i];
                if (string.IsNullOrWhiteSpace(hex))
                {
                    normalized.Add(null);
                    continue;
                }

                if (!ColorHex.TryParse(hex, out var color))
                {
                    // 无效颜色视为“空色块”，避免落盘脏数据导致 UI 崩溃。
                    normalized.Add(null);
                    continue;
                }

                // 统一落盘格式：#RRGGBB（不含透明度）。
                normalized.Add(ColorHex.ToHexRgb(color));
            }

            return normalized;
        }

        internal static List<float> NormalizeThicknessPresets(IReadOnlyList<float>? presets)
        {
            // 约定：必须恰好三档；异常情况回退默认值。
            if (presets is null || presets.Count != 3)
            {
                return new List<float>(DefaultThicknessPresets);
            }

            float a = presets[0];
            float b = presets[1];
            float c = presets[2];

            // 允许用户配置，但做基本兜底：必须为正数且不能过小。
            if (!IsValidThickness(a) || !IsValidThickness(b) || !IsValidThickness(c))
            {
                return new List<float>(DefaultThicknessPresets);
            }

            // 归一化为递增，避免 UI 上出现“细比中更粗”的困惑。
            float[] arr = [a, b, c];
            Array.Sort(arr);
            return new List<float>(arr);
        }

        private static bool IsValidThickness(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0.5f && value <= 64.0f;
        }
    }
}

