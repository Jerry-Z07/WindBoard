using System.Collections.Generic;
using WindBoard.Features.Dock.Models;
using Xunit;

namespace WindBoard.Tests.Features.Dock;

public sealed class DockLayoutRulesTests
{
    [Fact]
    public void CanHide_ReturnsFalseForMore_AndTrueForOtherItems()
    {
        Assert.False(DockLayoutRules.CanHide(DockItemIds.More));

        foreach (string id in DockSettingsDefaults.All)
        {
            if (string.Equals(id, DockItemIds.More, System.StringComparison.Ordinal))
            {
                continue;
            }

            Assert.True(DockLayoutRules.CanHide(id), id);
        }
    }

    [Fact]
    public void NormalizeHiddenItems_FiltersUnknownWhitespaceDuplicatesAndMore()
    {
        var input = new List<string>
        {
            " import ",
            DockItemIds.More,
            " unknown ",
            DockItemIds.Import,
            DockItemIds.ToolPen,
            "   ",
        };

        List<string> normalized = DockLayoutRules.NormalizeHiddenItems(input);

        Assert.Equal(new[] { DockItemIds.Import, DockItemIds.ToolPen }, normalized);
    }

    [Fact]
    public void NormalizeHiddenItems_Null_ReturnsEmpty()
    {
        Assert.Empty(DockLayoutRules.NormalizeHiddenItems(null));
    }

    [Fact]
    public void FilterVisibleOrder_PreservesOrderAndSkipsHidden()
    {
        var order = new List<string> { DockItemIds.Import, DockItemIds.More, DockItemIds.Minimize };
        HashSet<string> hidden = new(System.StringComparer.Ordinal) { DockItemIds.More };

        List<string> visible = DockLayoutRules.FilterVisibleOrder(order, hidden);

        Assert.Equal(new[] { DockItemIds.Import, DockItemIds.Minimize }, visible);
    }

    [Fact]
    public void FilterVisibleOrder_NoHidden_ReturnsOriginalOrder()
    {
        var order = new List<string>(DockSettingsDefaults.All);

        List<string> visible = DockLayoutRules.FilterVisibleOrder(order, new HashSet<string>());

        Assert.Equal(order, visible);
    }
}
