using System;

namespace WindBoard.Settings
{
    /// <summary>
    /// 更新检查频率（仅用于 UI 与设置持久化；实际更新逻辑后续接入）。
    /// </summary>
    internal enum UpdateCheckInterval
    {
        Weekly,
        Biweekly,
        Monthly,
        Never,
    }

    /// <summary>
    /// 更新检查频率解析与归一化（settings.json ⇄ 内存态）。
    /// </summary>
    internal static class UpdateCheckIntervalParser
    {
        internal const string WeeklyValue = "weekly";
        internal const string BiweeklyValue = "biweekly";
        internal const string MonthlyValue = "monthly";
        internal const string NeverValue = "never";

        internal static bool TryParse(string? text, out UpdateCheckInterval interval)
        {
            interval = UpdateCheckInterval.Weekly;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string value = text.Trim();

            if (value.Equals(WeeklyValue, StringComparison.OrdinalIgnoreCase))
            {
                interval = UpdateCheckInterval.Weekly;
                return true;
            }

            if (value.Equals(BiweeklyValue, StringComparison.OrdinalIgnoreCase))
            {
                interval = UpdateCheckInterval.Biweekly;
                return true;
            }

            if (value.Equals(MonthlyValue, StringComparison.OrdinalIgnoreCase))
            {
                interval = UpdateCheckInterval.Monthly;
                return true;
            }

            if (value.Equals(NeverValue, StringComparison.OrdinalIgnoreCase))
            {
                interval = UpdateCheckInterval.Never;
                return true;
            }

            return false;
        }

        internal static string ToSettingValue(UpdateCheckInterval interval)
        {
            return interval switch
            {
                UpdateCheckInterval.Biweekly => BiweeklyValue,
                UpdateCheckInterval.Monthly => MonthlyValue,
                UpdateCheckInterval.Never => NeverValue,
                _ => WeeklyValue,
            };
        }
    }
}
