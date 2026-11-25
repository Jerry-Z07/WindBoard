using WindBoard.Features.Dock.Models;
using WindBoard.Settings;
using Xunit;

namespace WindBoard.Tests.Settings;

public sealed class AppSettingsClonerTests
{
    [Fact]
    public void Clone_CopiesDockHiddenItems_WithoutSharingList()
    {
        var source = new AppSettings();
        source.Dock.HiddenItems.Add(DockItemIds.Import);

        AppSettings clone = AppSettingsCloner.Clone(source);

        Assert.Equal(new[] { DockItemIds.Import }, clone.Dock.HiddenItems);

        source.Dock.HiddenItems.Add(DockItemIds.ToolPen);
        Assert.Single(clone.Dock.HiddenItems);
    }
}
