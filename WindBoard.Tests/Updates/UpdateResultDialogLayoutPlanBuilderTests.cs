using WindBoard.Updates;
using Xunit;

namespace WindBoard.Tests.Updates;

public sealed class UpdateResultDialogLayoutPlanBuilderTests
{
    [Fact]
    public void Build_UpdateAvailable_UsesTwoColumnLayout_AndPreservesMarkdown()
    {
        AppUpdateCheckResult result = new()
        {
            State = AppUpdateCheckState.UpdateAvailable,
            CurrentVersion = "2.2.0",
            Latest = new LatestReleaseInfo
            {
                Version = "2.2.1",
                VersionName = "v2.2.1",
                Changelog = new()
                {
                    ["zh-CN"] = "## 更新内容\r\n- feat: 支持 Markdown",
                },
            },
        };

        UpdateResultDialogLayoutPlan plan = UpdateResultDialogLayoutPlanBuilder.Build(result, "zh-CN");

        Assert.True(plan.UseTwoColumnLayout);
        Assert.False(plan.UseChangelogPlaceholder);
        Assert.Equal("## 更新内容\r\n- feat: 支持 Markdown", plan.ChangelogMarkdown);
    }

    [Fact]
    public void Build_UpdateAvailableWithoutChangelog_UsesPlaceholder()
    {
        AppUpdateCheckResult result = new()
        {
            State = AppUpdateCheckState.UpdateAvailable,
            CurrentVersion = "2.2.0",
            Latest = new LatestReleaseInfo
            {
                Version = "2.2.1",
                VersionName = "v2.2.1",
            },
        };

        UpdateResultDialogLayoutPlan plan = UpdateResultDialogLayoutPlanBuilder.Build(result, "zh-CN");

        Assert.True(plan.UseTwoColumnLayout);
        Assert.True(plan.UseChangelogPlaceholder);
        Assert.Equal(string.Empty, plan.ChangelogMarkdown);
    }

    [Fact]
    public void Build_UpToDate_DoesNotUseTwoColumnLayout()
    {
        AppUpdateCheckResult result = new()
        {
            State = AppUpdateCheckState.UpToDate,
            CurrentVersion = "2.2.1",
        };

        UpdateResultDialogLayoutPlan plan = UpdateResultDialogLayoutPlanBuilder.Build(result, "zh-CN");

        Assert.False(plan.UseTwoColumnLayout);
    }
}
