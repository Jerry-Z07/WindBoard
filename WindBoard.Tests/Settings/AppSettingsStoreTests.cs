using System.Collections.Generic;
using WindBoard.Settings;
using Xunit;

namespace WindBoard.Tests.Settings;

public sealed class AppSettingsStoreTests
{
    [Fact]
    public void NormalizeInPlace_非法背景色会被修正为默认值()
    {
        var settings = new AppSettings
        {
            Appearance = new AppearanceSettings
            {
                CanvasBackgroundHex = "invalid",
            },
        };

        AppSettingsStore.NormalizeInPlace(settings);

        Assert.Equal(ColorHex.DefaultCanvasBackgroundHex, settings.Appearance.CanvasBackgroundHex);
    }

    [Fact]
    public void NormalizeInPlace_Dock为空会补齐为默认值()
    {
        var settings = new AppSettings
        {
            Dock = null!,
        };

        AppSettingsStore.NormalizeInPlace(settings);

        Assert.NotNull(settings.Dock);
        Assert.Equal(DockSettingsDefaults.LeftOrder, settings.Dock.LeftOrder);
        Assert.Equal(DockSettingsDefaults.ToolsOrder, settings.Dock.ToolsOrder);
        Assert.Equal(DockSettingsDefaults.UndoRedoOrder, settings.Dock.UndoRedoOrder);
        Assert.Equal(DockSettingsDefaults.PagesOrder, settings.Dock.PagesOrder);
        Assert.True(settings.Dock.IsUndoRedoVisible);
    }

    [Fact]
    public void NormalizeInPlace_Dock顺序会过滤去重并补齐缺失项()
    {
        var settings = new AppSettings
        {
            Dock = new DockSettings
            {
                LeftOrder =
                [
                    DockItemIds.Import,
                    "unknown",
                    DockItemIds.More,
                    DockItemIds.More,
                ],
                ToolsOrder = null!,
                UndoRedoOrder = new List<string> { DockItemIds.Redo, DockItemIds.Undo },
                PagesOrder = new List<string>(),
                IsUndoRedoVisible = false,
            },
        };

        AppSettingsStore.NormalizeInPlace(settings);

        Assert.Equal(
            new[] { DockItemIds.Import, DockItemIds.More, DockItemIds.Minimize },
            settings.Dock.LeftOrder);
        Assert.Equal(DockSettingsDefaults.ToolsOrder, settings.Dock.ToolsOrder);
        Assert.Equal(new[] { DockItemIds.Redo, DockItemIds.Undo }, settings.Dock.UndoRedoOrder);
        Assert.Equal(DockSettingsDefaults.PagesOrder, settings.Dock.PagesOrder);
        Assert.False(settings.Dock.IsUndoRedoVisible);
    }
}
