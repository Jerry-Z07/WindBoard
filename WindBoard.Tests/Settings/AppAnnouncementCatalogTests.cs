using Microsoft.UI.Xaml.Controls;
using WindBoard.Settings;
using Xunit;

namespace WindBoard.Tests.Settings;

public sealed class AppAnnouncementCatalogTests
{
    [Fact]
    public void All_Ids_AreNotBlankAndUnique()
    {
        HashSet<string> ids = new(StringComparer.Ordinal);

        foreach (AppAnnouncement announcement in AppAnnouncementCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(announcement.Id));
            Assert.True(ids.Add(announcement.Id), $"公告 Id 重复：{announcement.Id}");
        }
    }

    [Fact]
    public void SelectNext_ReturnsFirstAnnouncement_WhenNothingDismissed()
    {
        AppAnnouncement? selected = AppAnnouncementCatalog.SelectNext(
            AppAnnouncementCatalog.All,
            Array.Empty<string>());

        Assert.Same(AppAnnouncementCatalog.All[0], selected);
    }

    [Fact]
    public void SelectNext_SkipsDismissedAnnouncement_AndReturnsNextOne()
    {
        AppAnnouncement first = CreateAnnouncement("first");
        AppAnnouncement second = CreateAnnouncement("second");
        AppAnnouncement[] announcements = [first, second];
        string[] dismissedIds = ["first"];

        AppAnnouncement? selected = AppAnnouncementCatalog.SelectNext(announcements, dismissedIds);

        Assert.Same(second, selected);
    }

    [Fact]
    public void SelectNext_ReturnsNull_WhenAllDismissed()
    {
        AppAnnouncement first = CreateAnnouncement("first");
        AppAnnouncement second = CreateAnnouncement("second");
        AppAnnouncement[] announcements = [first, second];
        string[] dismissedIds = ["first", "second"];

        AppAnnouncement? selected = AppAnnouncementCatalog.SelectNext(announcements, dismissedIds);

        Assert.Null(selected);
    }

    [Fact]
    public void SelectNext_IgnoresUnknownDismissedIds()
    {
        string[] dismissedIds = ["unknown-id"];

        AppAnnouncement? selected = AppAnnouncementCatalog.SelectNext(AppAnnouncementCatalog.All, dismissedIds);

        Assert.Same(AppAnnouncementCatalog.All[0], selected);
    }

    [Fact]
    public void SelectNext_ReturnsEarliestNotDismissed_WhenMultipleCandidatesRemain()
    {
        AppAnnouncement first = CreateAnnouncement("first");
        AppAnnouncement second = CreateAnnouncement("second");
        AppAnnouncement third = CreateAnnouncement("third");
        AppAnnouncement[] announcements = [first, second, third];
        string[] dismissedIds = ["second"];

        AppAnnouncement? selected = AppAnnouncementCatalog.SelectNext(announcements, dismissedIds);

        // 多条候选时取顺序最靠前的一条；反向遍历会误返回 third。
        Assert.Same(first, selected);
    }

    [Fact]
    public void SelectNext_TreatsDifferentCaseId_AsNotDismissed()
    {
        AppAnnouncement announcement = CreateAnnouncement("installer-distribution-changed");
        AppAnnouncement[] announcements = [announcement];
        string[] dismissedIds = ["INSTALLER-DISTRIBUTION-CHANGED"];

        AppAnnouncement? selected = AppAnnouncementCatalog.SelectNext(announcements, dismissedIds);

        // Id 是程序内部标识，大小写不同即不同项；改用 OrdinalIgnoreCase 会误判为已关闭。
        Assert.Same(announcement, selected);
    }

    private static AppAnnouncement CreateAnnouncement(string id)
    {
        return new AppAnnouncement(
            id,
            InfoBarSeverity.Informational,
            static () => string.Empty,
            static () => string.Empty,
            null,
            AppAnnouncementAction.None);
    }
}
