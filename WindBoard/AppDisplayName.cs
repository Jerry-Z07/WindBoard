using System;
using System.Globalization;

namespace WindBoard
{
    /// <summary>
    /// 应用对外展示名称（用于窗口标题、通知等用户可见位置）。
    ///
    /// 设计说明：
    /// - 本项目的本地化默认语言为 zh-CN；当系统语言未提供资源时，会回退到默认语言；
    /// - 但“软件名”的产品规则更明确：中文环境显示“轻风白板”，其他语言显示“WindBoard”；
    /// - 因此这里不依赖资源回退策略，直接按 UI Culture 判定，确保在任何语言环境下行为一致。
    /// </summary>
    internal static class AppDisplayName
    {
        internal const string ChineseName = "轻风白板";
        internal const string DefaultName = "WindBoard";

        /// <summary>
        /// 获取应用对外展示名称。
        /// 约定：所有 zh-*（含简体/繁体/脚本变体）均视为中文环境。
        /// </summary>
        internal static string Get(CultureInfo? culture = null)
        {
            CultureInfo uiCulture = culture ?? CultureInfo.CurrentUICulture;

            // 使用 Name 前缀判断（例如 zh-CN / zh-Hans / zh-TW），避免依赖 TwoLetterISOLanguageName 在自定义 culture 上的差异。
            string name = uiCulture?.Name ?? string.Empty;
            if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                return ChineseName;
            }

            return DefaultName;
        }
    }
}

