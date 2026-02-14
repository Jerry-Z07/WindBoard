using System;

namespace WindBoard.Settings
{
    /// <summary>
    /// 元素卡片主题：用于导入的图片/文件/文本/链接等“卡片”外观。
    /// </summary>
    internal enum ElementCardTheme
    {
        Dark,
        Light,
    }

    /// <summary>
    /// 元素卡片主题解析与归一化（settings.json ⇄ 内存态）。
    /// </summary>
    internal static class ElementCardThemeParser
    {
        internal const string DarkValue = "dark";
        internal const string LightValue = "light";

        internal static bool TryParse(string? text, out ElementCardTheme theme)
        {
            theme = ElementCardTheme.Dark;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string value = text.Trim();

            if (value.Equals(DarkValue, StringComparison.OrdinalIgnoreCase))
            {
                theme = ElementCardTheme.Dark;
                return true;
            }

            if (value.Equals(LightValue, StringComparison.OrdinalIgnoreCase))
            {
                theme = ElementCardTheme.Light;
                return true;
            }

            return false;
        }

        internal static string ToSettingValue(ElementCardTheme theme)
        {
            return theme == ElementCardTheme.Light ? LightValue : DarkValue;
        }
    }
}

