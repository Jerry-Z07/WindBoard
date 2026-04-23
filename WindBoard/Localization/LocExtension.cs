using Microsoft.UI.Xaml.Markup;

namespace WindBoard.Localization
{
    /// <summary>
    /// XAML MarkupExtension：用于在 XAML 中通过 Key 取本地化字符串。
    ///
    /// 用法示例：
    /// <code>
    /// xmlns:l10n="using:WindBoard.Localization"
    /// Text="{l10n:Loc Key=Common_Close}"
    /// </code>
    /// 说明：底层资源实现可演进，但 XAML 调用点统一保留 `{l10n:Loc Key=...}`，避免大面积迁移到 `x:Uid`。
    /// </summary>
    public sealed class LocExtension : MarkupExtension
    {
        public string Key { get; set; } = string.Empty;

        public string? Fallback { get; set; }

        protected override object ProvideValue()
        {
            return L10n.Get(Key, Fallback);
        }
    }
}
