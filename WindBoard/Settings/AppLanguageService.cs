using System;
using System.Globalization;
using WindBoard.Logging;
using AppSdkApplicationLanguages = Microsoft.Windows.Globalization.ApplicationLanguages;

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
        /// 捕获“系统语言”（进程启动时的当前语言），用于从自定义语言切回“跟随系统”。
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

                AppLog.Info("L10n", $"已捕获系统语言：culture={_systemCulture.Name}, uiCulture={_systemUiCulture.Name}");
            }
        }

        /// <summary>
        /// 应用语言偏好到当前进程。
        /// </summary>
        internal static void Apply(string? settingValue)
        {
            CaptureSystemCulturesIfNeeded();

            if (!AppLanguagePreferenceParser.TryNormalize(settingValue, out string preference))
            {
                // 语言设置失败不应阻断应用启动/运行：记录日志并继续使用系统语言。
                if (!string.IsNullOrWhiteSpace(settingValue))
                {
                    AppLog.Warn("L10n", $"语言偏好无效或未提供资源，已回退到 system：value='{settingValue}'");
                }

                preference = AppLanguagePreferenceParser.SystemValue;
            }

            // 默认：跟随系统
            CultureInfo? targetCulture = null;
            CultureInfo? targetUiCulture = null;
            string primaryLanguageOverride = string.Empty;

            if (!AppLanguagePreferenceParser.IsSystem(preference))
            {
                targetCulture = GetCultureOrFallback(preference, fallback: _systemCulture);
                targetUiCulture = GetCultureOrFallback(preference, fallback: _systemUiCulture);
                primaryLanguageOverride = preference;
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
                // 语言设置失败不应阻断应用启动/运行：记录日志并继续使用当前语言。
                AppLog.Warn("L10n", "应用 CultureInfo 失败，将继续使用当前语言", ex);
            }

            string appliedPrimaryLanguageOverride = ApplyPrimaryLanguageOverride(primaryLanguageOverride);

            AppLog.Info(
                "L10n",
                $"语言已应用：preference='{preference}', culture={CultureInfo.CurrentCulture.Name}, uiCulture={CultureInfo.CurrentUICulture.Name}, primaryOverride='{appliedPrimaryLanguageOverride}'");
        }

        private static string ApplyPrimaryLanguageOverride(string desiredOverride)
        {
            // 与 WinUI 的语言选择保持一致（对内置控件文本/方向等更友好）。
            // 说明：
            // - 对于“跟随系统”（desiredOverride 为空）场景，如果当前已经没有 override，则不写入，避免某些环境下对空字符串赋值会抛异常并刷警告；
            // - 在“无包身份/运行时差异”等环境下，该 API 可能不可用：这里不阻断主流程（应用自身的本地化仍由 CultureInfo 驱动）。
            if (!TryGetPrimaryLanguageOverride(out string currentOverride))
            {
                // 无法读取时：
                // - “跟随系统”不强制写入（系统默认行为即可）；
                // - 显式语言偏好仍尝试写入（后续会记录失败）。
                currentOverride = string.Empty;
            }

            // 目标是“跟随系统”
            if (string.IsNullOrEmpty(desiredOverride))
            {
                // 当前已经是空：无需重复写入（避免空字符串赋值异常）。
                if (string.IsNullOrEmpty(currentOverride))
                {
                    return string.Empty;
                }

                // 尝试清空（按 API 约定：空字符串表示取消 override）。
                if (TrySetPrimaryLanguageOverride(string.Empty, out Exception? clearError))
                {
                    return string.Empty;
                }

                // 降级策略：若清空失败，则至少把 override 设置为启动时捕获到的系统 UI 语言，避免残留旧值导致语言不一致。
                // 注意：该降级仅影响内置控件语言，不影响 WindBoard 自身资源读取。
                string fallback = string.IsNullOrWhiteSpace(_systemUiCulture.Name) ? string.Empty : _systemUiCulture.Name;
                if (!string.IsNullOrEmpty(fallback) && TrySetPrimaryLanguageOverride(fallback, out _))
                {
                    AppLog.Info("L10n", $"清空 PrimaryLanguageOverride 失败，已降级为设置为系统语言：fallback='{fallback}'（desired=''）");
                    return fallback;
                }

                // 清空与降级均失败：记录警告（附带一次异常堆栈便于排查）。
                AppLog.Warn("L10n", "清空 PrimaryLanguageOverride 失败（desired=''）", clearError);
                return currentOverride;
            }

            // 目标是指定语言：若无需变更则直接返回，避免无效写入。
            if (string.Equals(currentOverride, desiredOverride, StringComparison.OrdinalIgnoreCase))
            {
                return currentOverride;
            }

            if (!TrySetPrimaryLanguageOverride(desiredOverride, out Exception? setError))
            {
                AppLog.Warn("L10n", $"设置 PrimaryLanguageOverride 失败：value='{desiredOverride}'", setError);
                return currentOverride;
            }

            return desiredOverride;
        }

        private static bool TryGetPrimaryLanguageOverride(out string value)
        {
            try
            {
                value = AppSdkApplicationLanguages.PrimaryLanguageOverride;
                if (value is null)
                {
                    value = string.Empty;
                }

                return true;
            }
            catch
            {
                // 忽略：继续尝试 Windows.* 版本的 API
            }

            try
            {
                value = Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride;
                if (value is null)
                {
                    value = string.Empty;
                }

                return true;
            }
            catch
            {
                value = string.Empty;
                return false;
            }
        }

        private static bool TrySetPrimaryLanguageOverride(string value, out Exception? error)
        {
            error = null;

            try
            {
                AppSdkApplicationLanguages.PrimaryLanguageOverride = value;
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
            }

            try
            {
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = value;
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
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
