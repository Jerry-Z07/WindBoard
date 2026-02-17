using System;

namespace WindBoard.Settings
{
    /// <summary>
    /// 应用显示语言偏好（仅用于设置持久化与文化切换入口）。
    /// </summary>
    internal enum AppLanguagePreference
    {
        /// <summary>
        /// 跟随系统语言。
        /// </summary>
        System,

        /// <summary>
        /// 简体中文（zh-CN）。
        /// </summary>
        Chinese,

        /// <summary>
        /// 英文（en-US）。
        /// </summary>
        English,
    }

    /// <summary>
    /// 语言偏好解析与归一化（settings.json ⇄ 内存态）。
    /// </summary>
    internal static class AppLanguagePreferenceParser
    {
        internal const string SystemValue = "system";
        internal const string ChineseValue = "zh-CN";
        internal const string EnglishValue = "en-US";

        internal static bool TryParse(string? text, out AppLanguagePreference preference)
        {
            preference = AppLanguagePreference.System;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string value = text.Trim();

            // 跟随系统
            if (value.Equals(SystemValue, StringComparison.OrdinalIgnoreCase)
                || value.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                preference = AppLanguagePreference.System;
                return true;
            }

            // 简体中文（兼容 '-' vs '_' 与 'zh' 简写）
            if (value.Equals(ChineseValue, StringComparison.OrdinalIgnoreCase)
                || value.Equals("zh_CN", StringComparison.OrdinalIgnoreCase)
                || value.Equals("zh", StringComparison.OrdinalIgnoreCase))
            {
                preference = AppLanguagePreference.Chinese;
                return true;
            }

            // English（兼容 '-' vs '_' 与 'en' 简写）
            if (value.Equals(EnglishValue, StringComparison.OrdinalIgnoreCase)
                || value.Equals("en_US", StringComparison.OrdinalIgnoreCase)
                || value.Equals("en", StringComparison.OrdinalIgnoreCase))
            {
                preference = AppLanguagePreference.English;
                return true;
            }

            return false;
        }

        internal static string ToSettingValue(AppLanguagePreference preference)
        {
            return preference switch
            {
                AppLanguagePreference.Chinese => ChineseValue,
                AppLanguagePreference.English => EnglishValue,
                _ => SystemValue,
            };
        }
    }
}

