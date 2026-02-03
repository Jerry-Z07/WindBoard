using System.Collections.Generic;
using WindBoard.Settings;
using Xunit;

namespace WindBoard.Tests.Settings;

public sealed class AppSettingsStoreTests
{
    // 非法背景色会被修正为默认值
    [Fact]
    public void NormalizeInPlace_ResetsInvalidCanvasBackgroundHexToDefault()
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

    // Dock 为空会补齐为默认值
    [Fact]
    public void NormalizeInPlace_CreatesDockDefaults_WhenDockIsNull()
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
        Assert.False(settings.Dock.IsShortcutDocksVisible);
        Assert.NotNull(settings.Dock.ShortcutItems);
        Assert.Empty(settings.Dock.ShortcutItems);
    }

    // Dock 顺序会过滤去重并补齐缺失项
    [Fact]
    public void NormalizeInPlace_NormalizesDockOrders_DedupesAndFillsMissingItems()
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
        Assert.False(settings.Dock.IsShortcutDocksVisible);
        Assert.NotNull(settings.Dock.ShortcutItems);
        Assert.Empty(settings.Dock.ShortcutItems);
    }

    [Fact]
    public void NormalizeInPlace_NormalizesShortcutDockItems_FillsDefaultsAndLimitsCount()
    {
        var settings = new AppSettings
        {
            Dock = new DockSettings
            {
                IsShortcutDocksVisible = true,
                ShortcutItems =
                [
                    new ShortcutDockItemSettings
                    {
                        Id = " ",
                        Side = "unknown",
                        Type = "unknown",
                        Path = "  C:\\Temp\\a.txt  ",
                        IconSource = "unknown",
                        IconPath = "  C:\\Temp\\icon.png  ",
                        Arguments = "  --foo bar  ",
                    },
                    new ShortcutDockItemSettings
                    {
                        Id = "",
                        Side = ShortcutDockSides.Right,
                        Type = ShortcutDockItemTypes.Link,
                        Path = " https://example.com/path ",
                        IconSource = ShortcutDockIconSources.Default,
                    },
                    new ShortcutDockItemSettings { Id = "", Path = "" },
                    new ShortcutDockItemSettings { Id = "", Path = "C:\\Temp\\b.txt" },
                    new ShortcutDockItemSettings { Id = "", Path = "C:\\Temp\\c.txt" },
                    new ShortcutDockItemSettings { Id = "", Path = "C:\\Temp\\d.txt" }, // 超过 5：应被丢弃
                ],
            },
        };

        AppSettingsStore.NormalizeInPlace(settings);

        Assert.NotNull(settings.Dock.ShortcutItems);
        Assert.Equal(5, settings.Dock.ShortcutItems.Count);

        ShortcutDockItemSettings first = settings.Dock.ShortcutItems[0];
        Assert.False(string.IsNullOrWhiteSpace(first.Id));
        Assert.Equal(ShortcutDockSides.Left, first.Side);
        Assert.Equal(ShortcutDockItemTypes.File, first.Type);
        Assert.Equal("C:\\Temp\\a.txt", first.Path);
        Assert.Equal(ShortcutDockIconSources.Default, first.IconSource);
        Assert.Equal("C:\\Temp\\icon.png", first.IconPath);
        Assert.Equal("  --foo bar  ", first.Arguments);

        ShortcutDockItemSettings link = settings.Dock.ShortcutItems[1];
        Assert.Equal(ShortcutDockSides.Right, link.Side);
        Assert.Equal(ShortcutDockItemTypes.Link, link.Type);
        Assert.Equal("https://example.com/path", link.Path);
        Assert.Equal(ShortcutDockIconSources.Default, link.IconSource);
    }

    // 书写设置为空会补齐默认画笔设置
    [Fact]
    public void NormalizeInPlace_CreatesPenDefaults_WhenWritingIsNull()
    {
        var settings = new AppSettings
        {
            Writing = null!,
        };

        AppSettingsStore.NormalizeInPlace(settings);

        Assert.NotNull(settings.Writing);
        Assert.NotNull(settings.Writing.Pen);
        Assert.Equal(PenSettingsDefaults.DefaultPaletteHexes.Count, settings.Writing.Pen.PaletteHexes.Count);
        Assert.Equal(PenSettingsDefaults.DefaultThicknessPresets.Count, settings.Writing.Pen.ThicknessPresets.Count);
        Assert.False(settings.Writing.Pen.UseThicknessSlider);
    }

    // 画笔色板会归一化数量与颜色格式
    [Fact]
    public void NormalizeInPlace_NormalizesPenPalette_CountAndColorFormat()
    {
        var settings = new AppSettings
        {
            Writing = new WritingSettings
            {
                Pen = new PenSettings
                {
                    // 小于最小数量：应补齐到 3
                    PaletteHexes = [ "#FF000000", "invalid" ],

                    // 乱序且含非法值：应回退到默认值并排序为递增
                    ThicknessPresets = [ 5.0f, -1.0f, 3.0f ],
                },
            },
        };

        AppSettingsStore.NormalizeInPlace(settings);

        Assert.Equal(3, settings.Writing.Pen.PaletteHexes.Count);
        Assert.Equal("#000000", settings.Writing.Pen.PaletteHexes[0]);
        Assert.Null(settings.Writing.Pen.PaletteHexes[1]);
        Assert.Null(settings.Writing.Pen.PaletteHexes[2]);

        Assert.Equal(PenSettingsDefaults.DefaultThicknessPresets, settings.Writing.Pen.ThicknessPresets);
    }
}
