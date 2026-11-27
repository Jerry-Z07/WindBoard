using Microsoft.UI.Xaml.Controls;
using WindBoard.Settings;

namespace WindBoard.Tests.Settings;

public sealed class AppAnnouncementSelectorTests
{
    [Fact]
    public void SelectNext_ReturnsFirstAnnouncement_WhenNothingDismissed()
    {
        AppAnnouncement first = CreateAnnouncement("first");
        AppAnnouncement second = CreateAnnouncement("second");

        AppAnnouncement? selected = AppAnnouncementSelector.SelectNext([first, second], Array.Empty<string>());

        Assert.Same(first, selected);
    }

    [Fact]
    public void SelectNext_SkipsDismissedAnnouncement_AndReturnsNextOne()
    {
        AppAnnouncement first = CreateAnnouncement("first");
        AppAnnouncement second = CreateAnnouncement("second");

        AppAnnouncement? selected = AppAnnouncementSelector.SelectNext([first, second], ["first"]);

        Assert.Same(second, selected);
    }

    [Fact]
    public void SelectNext_ReturnsNull_WhenAllDismissed()
    {
        AppAnnouncement first = CreateAnnouncement("first");
        AppAnnouncement second = CreateAnnouncement("second");

        AppAnnouncement? selected = AppAnnouncementSelector.SelectNext([first, second], ["first", "second"]);

        Assert.Null(selected);
    }

    [Fact]
    public void SelectNext_ReturnsNull_WhenListIsEmpty()
    {
        AppAnnouncement? selected = AppAnnouncementSelector.SelectNext([], Array.Empty<string>());

        Assert.Null(selected);
    }

    [Fact]
    public void SelectNext_IgnoresUnknownDismissedIds()
    {
        AppAnnouncement first = CreateAnnouncement("first");

        AppAnnouncement? selected = AppAnnouncementSelector.SelectNext([first], ["unknown-id"]);

        Assert.Same(first, selected);
    }

    [Fact]
    public void SelectNext_ReturnsEarliestNotDismissed_WhenMultipleCandidatesRemain()
    {
        AppAnnouncement first = CreateAnnouncement("first");
        AppAnnouncement second = CreateAnnouncement("second");
        AppAnnouncement third = CreateAnnouncement("third");

        AppAnnouncement? selected = AppAnnouncementSelector.SelectNext([first, second, third], ["second"]);

        // 多条候选时取顺序最靠前的一条；反向遍历会误返回 third。
        Assert.Same(first, selected);
    }

    [Fact]
    public void SelectNext_TreatsDifferentCaseId_AsNotDismissed()
    {
        AppAnnouncement announcement = CreateAnnouncement("installer-distribution-changed");

        AppAnnouncement? selected = AppAnnouncementSelector.SelectNext(
            [announcement],
            ["INSTALLER-DISTRIBUTION-CHANGED"]);

        // Id 是程序内部标识，大小写不同即不同项；改用 OrdinalIgnoreCase 会误判为已关闭。
        Assert.Same(announcement, selected);
    }

    private static AppAnnouncement CreateAnnouncement(string id)
    {
        return new AppAnnouncement(
            id,
            InfoBarSeverity.Informational,
            AppAnnouncementAction.None,
            new Dictionary<string, AppAnnouncementText>
            {
                ["zh-CN"] = new AppAnnouncementText("title", "message", null),
            });
    }
}
