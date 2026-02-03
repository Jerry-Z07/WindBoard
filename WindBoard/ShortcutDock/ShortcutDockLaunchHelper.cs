using System;

namespace WindBoard.ShortcutDock
{
    /// <summary>
    /// 快捷入口 Dock：启动目标的解析与归一化（纯字符串逻辑，便于单元测试覆盖）。
    /// </summary>
    internal static class ShortcutDockLaunchHelper
    {
        internal static string NormalizeInput(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            string value = input.Trim();

            // 兼容用户手动输入带引号的路径，例如："C:\Program Files\App\app.exe"
            if (value.Length >= 2)
            {
                if ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
                {
                    value = value[1..^1].Trim();
                }
            }

            // 展开环境变量，例如：%USERPROFILE%\Desktop
            value = Environment.ExpandEnvironmentVariables(value);
            return value.Trim();
        }

        internal static string NormalizeArguments(string? arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments))
            {
                return string.Empty;
            }

            // 参数一般不需要去引号（参数本身可能含引号），这里只做环境变量展开与首尾空白处理。
            string value = Environment.ExpandEnvironmentVariables(arguments);
            return value.Trim();
        }

        internal static bool TryNormalizeLinkUri(string? input, out Uri? uri)
        {
            uri = null;

            string value = NormalizeInput(input);
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            // 先尝试按“完整 URI”解析（支持 http/https/mailto 等）。
            if (Uri.TryCreate(value, UriKind.Absolute, out Uri? direct))
            {
                uri = direct;
                return true;
            }

            // 兼容用户只输入 host/path，例如：example.com 或 example.com/a
            // 这里默认补 https。
            if (!value.Contains("://", StringComparison.Ordinal)
                && Uri.TryCreate("https://" + value, UriKind.Absolute, out Uri? https))
            {
                uri = https;
                return true;
            }

            return false;
        }
    }
}

