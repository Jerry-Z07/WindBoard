using WindBoard.Updates;
using Xunit;

namespace WindBoard.Tests.Updates;

public sealed class UpdateResultDialogLayoutPlanBuilderTests
{
    private static AppUpdateCheckResult CreateResult(AppUpdateCheckState state, string? changelog = null)
    {
        return new AppUpdateCheckResult
        {
            State = state,
            CurrentVersion = "2.2.0",
            Latest = changelog is null
                ? null
                : new LatestReleaseInfo
                {
                    Version = "2.2.1",
                    VersionName = "v2.2.1",
                    Changelog = new()
                    {
                        ["zh-CN"] = changelog,
                    },
                },
        };
    }

    [Fact]
    public void Build_UpdateAvailableWithWideWindow_UsesTwoColumnLayout_AndPreservesMarkdown()
    {
        UpdateResultDialogLayoutPlan plan = UpdateResultDialogLayoutPlanBuilder.Build(
            CreateResult(AppUpdateCheckState.UpdateAvailable, "## 更新内容\r\n- feat: 支持 Markdown"),
            "zh-CN",
            windowWidth: 1280,
            windowHeight: 800);

        Assert.True(plan.UseTwoColumnLayout);
        Assert.False(plan.UseChangelogPlaceholder);
        Assert.Equal("## 更新内容\r\n- feat: 支持 Markdown", plan.ChangelogMarkdown);
        // 两栏高度 clamp 上限：800 - 180 = 620 > 480，取 480。
        Assert.Equal(480d, plan.ScrollAreaMaxHeight);
    }

    [Fact]
    public void Build_UpdateAvailableWithoutChangelog_UsesPlaceholder()
    {
        var result = new AppUpdateCheckResult
        {
            State = AppUpdateCheckState.UpdateAvailable,
            CurrentVersion = "2.2.0",
            Latest = new LatestReleaseInfo
            {
                Version = "2.2.1",
                VersionName = "v2.2.1",
            },
        };

        UpdateResultDialogLayoutPlan plan = UpdateResultDialogLayoutPlanBuilder.Build(
            result, "zh-CN", windowWidth: 1280, windowHeight: 800);

        Assert.True(plan.UseTwoColumnLayout);
        Assert.True(plan.UseChangelogPlaceholder);
        Assert.Equal(string.Empty, plan.ChangelogMarkdown);
    }

    [Fact]
    public void Build_UpdateAvailableWithNarrowWindow_FallsBackToSingleColumn()
    {
        UpdateResultDialogLayoutPlan plan = UpdateResultDialogLayoutPlanBuilder.Build(
            CreateResult(AppUpdateCheckState.UpdateAvailable, "## 更新内容"),
            "zh-CN",
            windowWidth: 1024,
            windowHeight: 768);

        // 宽度不足阈值：两栏决策退化为单栏，且不再产生占位语义。
        Assert.False(plan.UseTwoColumnLayout);
        Assert.False(plan.UseChangelogPlaceholder);
        // 单栏高度 clamp 上限：768 - 180 = 588 > 560，取 560。
        Assert.Equal(560d, plan.ScrollAreaMaxHeight);
    }

    [Fact]
    public void Build_WidthAtThreshold_UsesTwoColumnLayout()
    {
        // 阈值边界：恰好等于 1060（两栏内容 MinWidth 980 + 弹窗左右 chrome 80）时允许两栏。
        UpdateResultDialogLayoutPlan plan = UpdateResultDialogLayoutPlanBuilder.Build(
            CreateResult(AppUpdateCheckState.UpdateAvailable, "## 更新内容"),
            "zh-CN",
            windowWidth: 1060,
            windowHeight: 800);

        Assert.True(plan.UseTwoColumnLayout);
    }

    [Fact]
    public void Build_UpToDate_UsesSingleColumnLayout_EvenWithWideWindow()
    {
        UpdateResultDialogLayoutPlan plan = UpdateResultDialogLayoutPlanBuilder.Build(
            CreateResult(AppUpdateCheckState.UpToDate),
            "zh-CN",
            windowWidth: 1280,
            windowHeight: 800);

        // 两栏仅用于“有更新”状态，宽度足够也不启用。
        Assert.False(plan.UseTwoColumnLayout);
        // 单栏高度 clamp 上限：800 - 180 = 620 > 560，取 560。
        Assert.Equal(560d, plan.ScrollAreaMaxHeight);
    }

    [Fact]
    public void Build_SingleColumnMediumWindow_ScrollHeightUsesAvailableHeight()
    {
        UpdateResultDialogLayoutPlan plan = UpdateResultDialogLayoutPlanBuilder.Build(
            CreateResult(AppUpdateCheckState.Indeterminate),
            "zh-CN",
            windowWidth: 800,
            windowHeight: 500);

        Assert.False(plan.UseTwoColumnLayout);
        // 500 - 180 = 320 落在单栏 clamp 区间 [240, 560] 内，按实际值取。
        Assert.Equal(320d, plan.ScrollAreaMaxHeight);
    }

    [Fact]
    public void Build_SingleColumnSmallWindow_ScrollHeightClampedToMinimum()
    {
        UpdateResultDialogLayoutPlan plan = UpdateResultDialogLayoutPlanBuilder.Build(
            CreateResult(AppUpdateCheckState.Error),
            "zh-CN",
            windowWidth: 800,
            windowHeight: 300);

        Assert.False(plan.UseTwoColumnLayout);
        // 单栏高度 clamp 下限：300 - 180 = 120 < 240，取 240。
        Assert.Equal(240d, plan.ScrollAreaMaxHeight);
    }

    [Fact]
    public void Build_TwoColumnSmallWindow_ScrollHeightClampedToTwoColumnMinimum()
    {
        UpdateResultDialogLayoutPlan plan = UpdateResultDialogLayoutPlanBuilder.Build(
            CreateResult(AppUpdateCheckState.UpdateAvailable, "## 更新内容"),
            "zh-CN",
            windowWidth: 1280,
            windowHeight: 400);

        Assert.True(plan.UseTwoColumnLayout);
        // 两栏高度 clamp 下限：400 - 180 = 220 < 280，取 280。
        Assert.Equal(280d, plan.ScrollAreaMaxHeight);
    }
}
