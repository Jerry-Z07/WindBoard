using System;
using System.Collections.Generic;
using System.Globalization;
using WindBoard.Localization;

namespace WindBoard.Settings
{
    /// <summary>
    /// 语言偏好解析与归一化：settings.json 存 "system" 表示跟随系统，存其它值表示 <see cref="CultureInfo.Name"/>（如 "zh-CN" / "en-US" / "ja-JP"）。
    /// 设计目标：新增语言只需增加对应 *.resw 资源目录，设置页即自动出现该选项；允许手工编辑 settings.json，兼容大小写不敏感、下划线与连字符以及常见简写。
    /// </summary>
    internal static class AppLanguagePreferenceParser
    {
        internal const string SystemValue = "system";

        // 历史兼容：以前 UI 下拉框固定提供 zh-CN/en-US。
        internal const string ChineseValue = "zh-CN";
        internal const string EnglishValue = "en-US";

        /// <summary>
        /// 尝试把输入归一化为可持久化的设置值。
        /// </summary>
        /// <remarks>
        /// 只接受已提供资源的语言，避免内置控件文案与应用文案语言混搭；返回值不为 null 且不含空白。
        /// </remarks>
        internal static bool TryNormalize(string? text, out string normalizedSettingValue)
        {
            normalizedSettingValue = SystemValue;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string value = text.Trim();

            // 跟随系统
            if (value.Equals(SystemValue, StringComparison.OrdinalIgnoreCase)
                || value.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                normalizedSettingValue = SystemValue;
                return true;
            }

            // 统一把 '_' 形式映射为 '-'：例如 zh_CN -> zh-CN
            string candidate = value.Replace('_', '-');

            // 兼容：语言简写。对 zh/en 优先映射到历史默认，避免出现 "zh" 无资源而导致回退与日志。
            if (candidate.Equals("zh", StringComparison.OrdinalIgnoreCase))
            {
                return TryNormalizeToSupportedCulture(ChineseValue, out normalizedSettingValue);
            }

            if (candidate.Equals("en", StringComparison.OrdinalIgnoreCase))
            {
                return TryNormalizeToSupportedCulture(EnglishValue, out normalizedSettingValue);
            }

            if (!TryGetCultureInfo(candidate, out CultureInfo culture))
            {
                return false;
            }

            // 1) 直接匹配（用户输入本身就是可用语言）
            if (TryNormalizeToSupportedCulture(culture.Name, out normalizedSettingValue))
            {
                return true;
            }

            // 2) 中性文化（例如 ja）：若只有一个同语言的可用 culture，则自动映射（例如 ja -> ja-JP）
            if (culture.IsNeutralCulture && TryResolveNeutralCultureToSupported(culture, out normalizedSettingValue))
            {
                return true;
            }

            // 3) 父级回退（例如 zh-Hans-CN -> zh-Hans -> zh）
            for (CultureInfo current = culture.Parent; current != CultureInfo.InvariantCulture; current = current.Parent)
            {
                if (TryNormalizeToSupportedCulture(current.Name, out normalizedSettingValue))
                {
                    return true;
                }

                if (current.Parent == current)
                {
                    break;
                }
            }

            // 4) 语言代码匹配（例如 pt-BR -> pt-PT；仅在不歧义时启用）
            if (TryResolveByLanguageCodeToSupported(culture, out normalizedSettingValue))
            {
                return true;
            }

            return false;
        }

        internal static string NormalizeOrDefault(string? text)
        {
            return TryNormalize(text, out string normalized) ? normalized : SystemValue;
        }

        internal static bool IsSystem(string? settingValue)
        {
            return string.IsNullOrWhiteSpace(settingValue)
                || settingValue.Equals(SystemValue, StringComparison.OrdinalIgnoreCase);
        }

        private static IReadOnlyList<string> GetSupportedCultures()
        {
            return L10n.GetSupportedCultureNames();
        }

        private static bool TryNormalizeToSupportedCulture(string cultureName, out string normalizedSettingValue)
        {
            normalizedSettingValue = SystemValue;
            IReadOnlyList<string> supported = GetSupportedCultures();

            foreach (string supportedCulture in supported)
            {
                if (supportedCulture.Equals(cultureName, StringComparison.OrdinalIgnoreCase))
                {
                    // 返回“支持列表”中的规范值，确保大小写一致。
                    normalizedSettingValue = supportedCulture;
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolveNeutralCultureToSupported(CultureInfo neutralCulture, out string normalizedSettingValue)
        {
            normalizedSettingValue = SystemValue;

            string lang = neutralCulture.TwoLetterISOLanguageName;
            if (string.IsNullOrWhiteSpace(lang))
            {
                return false;
            }

            IReadOnlyList<string> supported = GetSupportedCultures();
            string? matched = null;

            foreach (string supportedCultureName in supported)
            {
                if (!TryGetCultureInfo(supportedCultureName, out CultureInfo supportedCulture))
                {
                    continue;
                }

                if (!supportedCulture.TwoLetterISOLanguageName.Equals(lang, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // 中性语言可能有多个地区变体：仅在“唯一候选”时自动映射。
                if (matched is not null)
                {
                    return false;
                }

                matched = supportedCultureName;
            }

            if (matched is null)
            {
                return false;
            }

            normalizedSettingValue = matched;
            return true;
        }

        private static bool TryResolveByLanguageCodeToSupported(CultureInfo culture, out string normalizedSettingValue)
        {
            normalizedSettingValue = SystemValue;

            string lang = culture.TwoLetterISOLanguageName;
            if (string.IsNullOrWhiteSpace(lang))
            {
                return false;
            }

            IReadOnlyList<string> supported = GetSupportedCultures();
            string? matched = null;

            // 优先选择历史默认（避免 en-GB/en-US 同时存在时的不可控结果）
            if (lang.Equals("zh", StringComparison.OrdinalIgnoreCase)
                && TryNormalizeToSupportedCulture(ChineseValue, out normalizedSettingValue))
            {
                return true;
            }

            if (lang.Equals("en", StringComparison.OrdinalIgnoreCase)
                && TryNormalizeToSupportedCulture(EnglishValue, out normalizedSettingValue))
            {
                return true;
            }

            foreach (string supportedCultureName in supported)
            {
                if (!TryGetCultureInfo(supportedCultureName, out CultureInfo supportedCulture))
                {
                    continue;
                }

                if (!supportedCulture.TwoLetterISOLanguageName.Equals(lang, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (matched is not null)
                {
                    // 多个候选：不自动猜测
                    return false;
                }

                matched = supportedCultureName;
            }

            if (matched is null)
            {
                return false;
            }

            normalizedSettingValue = matched;
            return true;
        }

        private static bool TryGetCultureInfo(string cultureName, out CultureInfo culture)
        {
            try
            {
                culture = CultureInfo.GetCultureInfo(cultureName);
                return true;
            }
            catch
            {
                culture = CultureInfo.InvariantCulture;
                return false;
            }
        }
    }
}
