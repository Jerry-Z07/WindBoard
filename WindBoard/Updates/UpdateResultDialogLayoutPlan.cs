using System;
using System.Globalization;

namespace WindBoard.Updates
{
    /// <summary>
    /// 更新结果弹窗的纯布局决策：
    /// - 负责决定是否使用两栏布局（按窗口可用宽度，而非固定 MinWidth 硬撑）；
    /// - 负责提供更新内容原文与滚动区高度建议，避免 UI 层重复判断。
    /// </summary>
    internal sealed record UpdateResultDialogLayoutPlan(
        bool UseTwoColumnLayout,
        string ChangelogMarkdown,
        bool UseChangelogPlaceholder,
        double ScrollAreaMaxHeight);

    internal static class UpdateResultDialogLayoutPlanBuilder
    {
        // 两栏阈值 = 两栏内容 MinWidth(980) + 弹窗左右 chrome（边框/内边距约 80）。
        // 低于该宽度时原生 ContentDialog 两栏布局会被压缩截断，需退化为单栏。
        internal const double TwoColumnThresholdWidth = 1060d;

        // 弹窗标题 + 命令区 + 上下内边距的垂直预留，从窗口高度扣除后得到滚动区可用高度。
        internal const double DialogVerticalChrome = 180d;

        // 单栏（外层 ScrollViewer 统一滚动）高度 clamp：下限保证摘要与按钮区可见，上限避免超过默认窗口高度。
        internal const double SingleColumnMinScrollHeight = 240d;
        internal const double SingleColumnMaxScrollHeight = 560d;

        // 两栏（左右各自 ScrollViewer）高度 clamp：两栏内容更紧凑，上限收窄以贴近弹窗视觉重心。
        internal const double TwoColumnMinScrollHeight = 280d;
        internal const double TwoColumnMaxScrollHeight = 480d;

        /// <param name="windowWidth">属主窗口可用宽度（XamlRoot.Size.Width，DIP）。</param>
        /// <param name="windowHeight">属主窗口可用高度（XamlRoot.Size.Height，DIP）。</param>
        internal static UpdateResultDialogLayoutPlan Build(
            AppUpdateCheckResult result,
            string cultureName,
            double windowWidth,
            double windowHeight)
        {
            string markdown = result.TryGetChangelog(cultureName) ?? string.Empty;
            bool useTwoColumnLayout = result.State == AppUpdateCheckState.UpdateAvailable
                && windowWidth >= TwoColumnThresholdWidth;
            bool usePlaceholder = useTwoColumnLayout && string.IsNullOrWhiteSpace(markdown);

            return new UpdateResultDialogLayoutPlan(
                UseTwoColumnLayout: useTwoColumnLayout,
                ChangelogMarkdown: markdown,
                UseChangelogPlaceholder: usePlaceholder,
                ScrollAreaMaxHeight: ComputeScrollAreaMaxHeight(useTwoColumnLayout, windowHeight));
        }

        /// <summary>
        /// 按窗口高度计算滚动区 MaxHeight（clamp 到对应布局的预设区间）。
        /// 独立成静态方法：UI 层在 XamlRoot.SizeChanged 响应式调整时复用同一决策逻辑。
        /// </summary>
        internal static double ComputeScrollAreaMaxHeight(bool useTwoColumnLayout, double windowHeight)
        {
            double availableHeight = windowHeight - DialogVerticalChrome;
            double minHeight = useTwoColumnLayout ? TwoColumnMinScrollHeight : SingleColumnMinScrollHeight;
            double maxHeight = useTwoColumnLayout ? TwoColumnMaxScrollHeight : SingleColumnMaxScrollHeight;
            return Math.Clamp(availableHeight, minHeight, maxHeight);
        }
    }
}
