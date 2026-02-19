using System;
using System.Collections.Generic;
using System.Globalization;
using WindBoard.Localization;

namespace WindBoard.Settings
{
    /// <summary>
    /// 语言偏好解析与归一化（settings.json ⇄ 内存态）。
    /// 
    /// 约定：
    /// - settings.json 存储值：
    ///   - "system"：跟随系统；
    ///   - 其它：<see cref="CultureInfo.Name"/>（例如 "zh-CN" / "en-US" / "ja-JP"）。
    /// 
    /// 设计目标：
    /// - 新增语言时尽量不改代码：只要增加对应的 *.resx 资源目录，设置页即可自动出现该语言选项；
    /// - 允许用户手工编辑 settings.json：支持大小写不敏感、'_' vs '-'、以及常见简写。
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
        /// - 只有“已提供资源”的语言才会被接受（避免出现“内置控件是 A 语言，但应用文案回退到默认语言”的混搭体验）。
        /// - 返回值不会为 null，且不会包含空白。
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
