using System;
using System.Collections.Generic;
using Microsoft.Windows.AppNotifications;

namespace WindBoard.Reminders
{
    /// <summary>
    /// Windows Toast 通知携带的启动参数编解码。
    /// 
    /// 说明：
    /// - 这里使用 query string 形式（k=v&amp;k2=v2），便于未来扩展；
    /// - 参数仅用于“点击通知后执行动作”，不需要在 UI 中展示。
    /// </summary>
    internal static class AppReminderToastArguments
    {
        internal const string KeyAction = "wb_action";

        internal const string ActionOpenDataRoot = "open_data_root";
        internal const string ActionOpenLogsDir = "open_logs_dir";

        internal static string? BuildLaunchArgument(AppReminderClickAction action)
        {
            return action switch
            {
                AppReminderClickAction.OpenAppDataRootDirectory => $"{KeyAction}={ActionOpenDataRoot}",
                AppReminderClickAction.OpenLogsDirectory => $"{KeyAction}={ActionOpenLogsDir}",
                _ => null,
            };
        }

        internal static bool TryParseClickAction(AppNotificationActivatedEventArgs? args, out AppReminderClickAction action)
        {
            action = AppReminderClickAction.None;
            if (args is null)
            {
                return false;
            }

            // 1) 优先读取已解析的参数字典（如果投影层支持）。
            try
            {
                IDictionary<string, string> dict = args.Arguments;
                if (dict is not null && dict.TryGetValue(KeyAction, out string? valueFromDict))
                {
                    return TryMapActionValue(valueFromDict, out action);
                }
            }
            catch
            {
                // 忽略：不同投影/版本下可能不支持或抛异常
            }

            // 2) 兜底：解析原始 argument 字符串。
            string raw = (args.Argument ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            // 允许直接传 action 值（不带 key）。
            if (TryMapActionValue(raw, out action))
            {
                return true;
            }

            if (!TryParseQueryString(raw, out Dictionary<string, string> parsed))
            {
                return false;
            }

            return parsed.TryGetValue(KeyAction, out string? value) && TryMapActionValue(value, out action);
        }

        private static bool TryMapActionValue(string? value, out AppReminderClickAction action)
        {
            action = AppReminderClickAction.None;
            string v = (value ?? string.Empty).Trim();

            if (string.Equals(v, ActionOpenDataRoot, StringComparison.OrdinalIgnoreCase))
            {
                action = AppReminderClickAction.OpenAppDataRootDirectory;
                return true;
            }

            if (string.Equals(v, ActionOpenLogsDir, StringComparison.OrdinalIgnoreCase))
            {
                action = AppReminderClickAction.OpenLogsDirectory;
                return true;
            }

            return false;
        }

        private static bool TryParseQueryString(string text, out Dictionary<string, string> dict)
        {
            dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // 支持最小 query string：a=b&amp;c=d
            string[] pairs = text.Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (string pair in pairs)
            {
                int eq = pair.IndexOf('=', StringComparison.Ordinal);
                if (eq <= 0)
                {
                    continue;
                }

                string k = pair.Substring(0, eq).Trim();
                string v = eq >= pair.Length - 1 ? string.Empty : pair.Substring(eq + 1).Trim();

                if (string.IsNullOrWhiteSpace(k))
                {
                    continue;
                }

                try
                {
                    k = Uri.UnescapeDataString(k);
                    v = Uri.UnescapeDataString(v);
                }
                catch
                {
                    // 忽略解码失败：按原文落入字典
                }

                dict[k] = v;
            }

            return dict.Count > 0;
        }
    }
}
