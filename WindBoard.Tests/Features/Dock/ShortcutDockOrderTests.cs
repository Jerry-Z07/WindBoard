using System.Collections.Generic;
using System.Linq;
using WindBoard.Features.Dock.Models;
using Xunit;

namespace WindBoard.Tests.Features.Dock;

public sealed class ShortcutDockOrderTests
{
    [Fact]
    public void Move_SameSideForward_AdjustsIndexAfterRemoval()
    {
        var items = new List<ShortcutDockItemSettings>
        {
            Item("a", ShortcutDockSides.Left),
            Item("b", ShortcutDockSides.Left),
            Item("c", ShortcutDockSides.Left),
        };

        List<ShortcutDockItemSettings> result = ShortcutDockOrder.Move(
            items,
            "a",
            ShortcutDockSides.Left,
            targetIndex: 2);

        Assert.Equal("b,a,c", Ids(result));
        Assert.All(result, item => Assert.Equal(ShortcutDockSides.Left, item.Side));
    }

    [Fact]
    public void Move_SameSideBackward_KeepsTargetIndex()
    {
        var items = new List<ShortcutDockItemSettings>
        {
            Item("a", ShortcutDockSides.Left),
            Item("b", ShortcutDockSides.Left),
            Item("c", ShortcutDockSides.Left),
        };

        List<ShortcutDockItemSettings> result = ShortcutDockOrder.Move(
            items,
            "c",
            ShortcutDockSides.Left,
            targetIndex: 0);

        Assert.Equal("c,a,b", Ids(result));
    }

    [Fact]
    public void Move_CrossSide_ChangesSideAndInsertsAtTargetIndex()
    {
        var items = new List<ShortcutDockItemSettings>
        {
            Item("a", ShortcutDockSides.Left),
            Item("b", ShortcutDockSides.Right),
        };

        List<ShortcutDockItemSettings> result = ShortcutDockOrder.Move(
            items,
            "a",
            ShortcutDockSides.Right,
            targetIndex: 1);

        Assert.Equal("b,a", Ids(result));
        Assert.Equal(ShortcutDockSides.Right, result[1].Side);
    }

    [Fact]
    public void Move_UnknownId_ReturnsCopyWithoutChanges()
    {
        var items = new List<ShortcutDockItemSettings>
        {
            Item("a", ShortcutDockSides.Left),
            Item("b", ShortcutDockSides.Right),
        };

        List<ShortcutDockItemSettings> result = ShortcutDockOrder.Move(
            items,
            "missing",
            ShortcutDockSides.Right,
            targetIndex: 0);

        Assert.NotSame(items, result);
        Assert.Equal("a,b", Ids(result));
        Assert.Equal(ShortcutDockSides.Left, result[0].Side);
        Assert.Equal(ShortcutDockSides.Right, result[1].Side);
    }

    private static string Ids(IEnumerable<ShortcutDockItemSettings> items)
    {
        return string.Join(',', items.Select(item => item.Id));
    }

    private static ShortcutDockItemSettings Item(string id, string side)
    {
        return new ShortcutDockItemSettings
        {
            Id = id,
            Side = side,
            Path = id,
        };
    }
}
