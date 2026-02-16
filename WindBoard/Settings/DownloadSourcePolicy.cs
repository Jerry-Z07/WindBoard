using System;

namespace WindBoard.Settings
{
    /// <summary>
    /// 下载源策略：
    /// - Auto：自动测速并选择最快源
    /// - Fixed：固定使用指定源
    /// </summary>
    internal enum DownloadSourcePolicy
    {
        Auto,
        Fixed,
    }

    /// <summary>
    /// 下载源策略解析与归一化（settings.json ⇄ 内存态）。
    /// </summary>
    internal static class DownloadSourcePolicyParser
    {
        internal const string AutoValue = "auto";
        internal const string FixedValue = "fixed";

        internal static bool TryParse(string? text, out DownloadSourcePolicy policy)
        {
            policy = DownloadSourcePolicy.Auto;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string value = text.Trim();

            if (value.Equals(AutoValue, StringComparison.OrdinalIgnoreCase))
            {
                policy = DownloadSourcePolicy.Auto;
                return true;
            }

            if (value.Equals(FixedValue, StringComparison.OrdinalIgnoreCase))
            {
                policy = DownloadSourcePolicy.Fixed;
                return true;
            }

            return false;
        }

        internal static string ToSettingValue(DownloadSourcePolicy policy)
        {
            return policy switch
            {
                DownloadSourcePolicy.Fixed => FixedValue,
                _ => AutoValue,
            };
        }
    }
}

