using System.Globalization;

namespace WindBoard.Updates
{
    /// <summary>
    /// 更新结果弹窗的纯布局决策：
    /// - 负责决定是否使用两栏布局；
    /// - 负责提供更新内容原文，避免 UI 层重复判断。
    /// </summary>
    internal sealed record UpdateResultDialogLayoutPlan(
        bool UseTwoColumnLayout,
        string ChangelogMarkdown,
        bool UseChangelogPlaceholder);

    internal static class UpdateResultDialogLayoutPlanBuilder
    {
        internal static UpdateResultDialogLayoutPlan Build(AppUpdateCheckResult result, string cultureName)
        {
            string markdown = result.TryGetChangelog(cultureName) ?? string.Empty;
            bool useTwoColumnLayout = result.State == AppUpdateCheckState.UpdateAvailable;
            bool usePlaceholder = useTwoColumnLayout && string.IsNullOrWhiteSpace(markdown);

            return new UpdateResultDialogLayoutPlan(
                UseTwoColumnLayout: useTwoColumnLayout,
                ChangelogMarkdown: markdown,
                UseChangelogPlaceholder: usePlaceholder);
        }
    }
}
