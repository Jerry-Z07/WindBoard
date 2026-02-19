using System;

namespace WindBoard.Settings
{
    /// <summary>
    /// 启动时窗口形态：用于决定应用启动后主窗口的初始显示模式。
    /// </summary>
    internal enum StartupWindowMode
    {
        Windowed,
        FullScreen,
    }

    /// <summary>
    /// 启动窗口形态解析与归一化（settings.json ⇄ 内存态）。
    /// </summary>
    internal static class StartupWindowModeParser
    {
        internal const string WindowedValue = "windowed";
        internal const string FullScreenValue = "fullscreen";

        internal static bool TryParse(string? text, out StartupWindowMode mode)
        {
            mode = StartupWindowMode.Windowed;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string value = text.Trim();

            if (value.Equals(WindowedValue, StringComparison.OrdinalIgnoreCase))
            {
                mode = StartupWindowMode.Windowed;
                return true;
            }

            if (value.Equals(FullScreenValue, StringComparison.OrdinalIgnoreCase))
            {
                mode = StartupWindowMode.FullScreen;
                return true;
            }

            return false;
        }

        internal static string ToSettingValue(StartupWindowMode mode)
        {
            return mode == StartupWindowMode.FullScreen ? FullScreenValue : WindowedValue;
        }
    }
}

