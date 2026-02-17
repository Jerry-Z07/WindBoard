using System;
using System.Globalization;
using WindBoard.Logging;

namespace WindBoard.Settings
{
    /// <summary>
    /// 应用语言切换/覆盖服务。
    /// 
    /// 说明：
    /// - 本项目的本地化字符串读取基于 <see cref="CultureInfo.CurrentUICulture"/>（见 Localization/L10n.cs）。
    /// - XAML 的 MarkupExtension（LocExtension）会在控件加载时取值，因此语言应尽量在任何 UI 创建前应用；
    /// - 运行中切换语言通常需要重启或重载页面/窗口才能完全生效（设置页会提示）。
    /// </summary>
    internal static class AppLanguageService
    {
        private static readonly object Gate = new();
        private static bool _isSystemCultureCaptured;
        private static CultureInfo _systemCulture = CultureInfo.InvariantCulture;
        private static CultureInfo _systemUiCulture = CultureInfo.InvariantCulture;

        /// <summary>
        /// 捕获“系统文化”（进程启动时的当前文化），用于从自定义语言切回“跟随系统”。
        /// </summary>
        internal static void CaptureSystemCulturesIfNeeded()
        {
            if (_isSystemCultureCaptured)
            {
                return;
            }

            lock (Gate)
            {
                if (_isSystemCultureCaptured)
                {
                    return;
                }

                _systemCulture = CultureInfo.CurrentCulture;
                _systemUiCulture = CultureInfo.CurrentUICulture;
                _isSystemCultureCaptured = true;

                AppLog.Info("L10n", $"已捕获系统文化：culture={_systemCulture.Name}, uiCulture={_systemUiCulture.Name}");
            }
        }

        /// <summary>
        /// 应用语言偏好到当前进程。
        /// </summary>
        internal static void Apply(AppLanguagePreference preference)
        {
            CaptureSystemCulturesIfNeeded();

            // 默认：跟随系统
            CultureInfo? targetCulture = null;
            CultureInfo? targetUiCulture = null;
            string primaryLanguageOverride = string.Empty;

            if (preference == AppLanguagePreference.Chinese)
            {
                targetCulture = GetCultureOrFallback(AppLanguagePreferenceParser.ChineseValue, fallback: _systemCulture);
                targetUiCulture = GetCultureOrFallback(AppLanguagePreferenceParser.ChineseValue, fallback: _systemUiCulture);
                primaryLanguageOverride = AppLanguagePreferenceParser.ChineseValue;
            }
            else if (preference == AppLanguagePreference.English)
            {
                targetCulture = GetCultureOrFallback(AppLanguagePreferenceParser.EnglishValue, fallback: _systemCulture);
                targetUiCulture = GetCultureOrFallback(AppLanguagePreferenceParser.EnglishValue, fallback: _systemUiCulture);
                primaryLanguageOverride = AppLanguagePreferenceParser.EnglishValue;
            }

            try
            {
                // DefaultThreadCurrent* 用于影响后续新线程；
                // Current* 用于影响当前线程（UI 线程）。
                if (targetCulture is null)
                {
                    CultureInfo.DefaultThreadCurrentCulture = null;
                    CultureInfo.CurrentCulture = _systemCulture;
                }
                else
                {
                    CultureInfo.DefaultThreadCurrentCulture = targetCulture;
                    CultureInfo.CurrentCulture = targetCulture;
                }

                if (targetUiCulture is null)
                {
                    CultureInfo.DefaultThreadCurrentUICulture = null;
                    CultureInfo.CurrentUICulture = _systemUiCulture;
                }
                else
                {
                    CultureInfo.DefaultThreadCurrentUICulture = targetUiCulture;
                    CultureInfo.CurrentUICulture = targetUiCulture;
                }
            }
            catch (Exception ex)
            {
                // 语言设置失败不应阻断应用启动/运行：记录日志并继续使用当前文化。
                AppLog.Warn("L10n", "应用 CultureInfo 失败，将继续使用当前文化", ex);
            }

            try
            {
                // 与 WinUI 的语言选择保持一致（对内置控件文本/方向等更友好）。
                // 说明：该 API 在某些环境下可能失败（例如权限/运行时差异），这里不阻断主流程。
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = primaryLanguageOverride;
            }
            catch (Exception ex)
            {
                AppLog.Warn("L10n", $"设置 PrimaryLanguageOverride 失败：value='{primaryLanguageOverride}'", ex);
            }

            AppLog.Info(
                "L10n",
                $"语言已应用：preference={AppLanguagePreferenceParser.ToSettingValue(preference)}, culture={CultureInfo.CurrentCulture.Name}, uiCulture={CultureInfo.CurrentUICulture.Name}, primaryOverride='{primaryLanguageOverride}'");
        }

        private static CultureInfo GetCultureOrFallback(string cultureName, CultureInfo fallback)
        {
            try
            {
                return CultureInfo.GetCultureInfo(cultureName);
            }
            catch (Exception ex)
            {
                AppLog.Warn("L10n", $"获取 Culture 失败：cultureName='{cultureName}'，将回退到 '{fallback.Name}'", ex);
                return fallback;
            }
        }
    }
}

