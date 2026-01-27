using System;
using System.Globalization;
using Windows.UI;

namespace WindBoard.Settings
{
    /// <summary>
    /// HEX 颜色字符串与 <see cref="Color"/> 之间的转换工具。
    /// 
    /// 约定：
    /// - 支持 #RRGGBB / RRGGBB
    /// - 兼容 #AARRGGBB / AARRGGBB（与 WinUI/XAML 常见格式一致）
    /// - 输出统一为 #RRGGBB（不含透明度）
    /// </summary>
    internal static class ColorHex
    {
        internal const string DefaultCanvasBackgroundHex = "#2E2F33";

        internal static readonly Color DefaultCanvasBackgroundColor = Color.FromArgb(0xFF, 0x2E, 0x2F, 0x33);

        internal static bool TryParse(string? text, out Color color)
        {
            color = default;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string hex = text.Trim();
            if (hex.StartsWith('#'))
            {
                hex = hex[1..];
            }

            byte a;
            byte r;
            byte g;
            byte b;

            if (hex.Length == 6)
            {
                a = 0xFF;
                if (!TryParseHexByte(hex, 0, out r)
                    || !TryParseHexByte(hex, 2, out g)
                    || !TryParseHexByte(hex, 4, out b))
                {
                    return false;
                }
            }
            else if (hex.Length == 8)
            {
                // #AARRGGBB
                if (!TryParseHexByte(hex, 0, out a)
                    || !TryParseHexByte(hex, 2, out r)
                    || !TryParseHexByte(hex, 4, out g)
                    || !TryParseHexByte(hex, 6, out b))
                {
                    return false;
                }
            }
            else
            {
                return false;
            }

            color = Color.FromArgb(a, r, g, b);
            return true;
        }

        internal static Color ParseOrDefault(string? text, Color defaultColor)
        {
            return TryParse(text, out Color color) ? color : defaultColor;
        }

        internal static string ToHexRgb(Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        internal static string NormalizeToHexRgbOrDefault(string? text, string defaultHexRgb)
        {
            if (TryParse(text, out Color color))
            {
                return ToHexRgb(color);
            }

            if (TryParse(defaultHexRgb, out Color defaultColor))
            {
                return ToHexRgb(defaultColor);
            }

            // 极端情况下 defaultHexRgb 也不可解析时，给一个保底值避免抛异常。
            return "#000000";
        }

        private static bool TryParseHexByte(string hex, int startIndex, out byte value)
        {
            value = 0;

            if (startIndex < 0 || startIndex + 2 > hex.Length)
            {
                return false;
            }

            return byte.TryParse(
                hex.AsSpan(startIndex, 2),
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out value);
        }
    }
}
