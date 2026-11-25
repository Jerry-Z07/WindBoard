using Microsoft.UI.Xaml.Markup;

namespace WindBoard.Localization
{
    /// <summary>
    /// XAML MarkupExtension，按 Key 取本地化字符串，用法 <c>Text="{l10n:Loc Key=Common_Close}"</c>（需 xmlns:l10n="using:WindBoard.Localization"）。
    /// XAML 调用点统一保留 `{l10n:Loc Key=...}`，允许底层资源实现演进，避免大面积迁移到 `x:Uid`。
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
