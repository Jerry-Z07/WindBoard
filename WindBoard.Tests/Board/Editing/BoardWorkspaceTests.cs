using WindBoard.Board.Editing;
using Xunit;

namespace WindBoard.Tests.Board.Editing;

public sealed class BoardWorkspaceTests
{
    [Fact]
    public void Ctor_CreatesSinglePageAndSelectsFirst_ByDefault()
    {
        var workspace = new BoardWorkspace();

        Assert.Single(workspace.Pages);
        Assert.Equal(0, workspace.CurrentIndex);
        Assert.Same(workspace.Pages[0], workspace.CurrentPage);
    }

    [Fact]
    public void AddPage_AddsPageAndSwitchesToIt()
    {
        var workspace = new BoardWorkspace();

        BoardPage original = workspace.CurrentPage;
        BoardPage created = workspace.AddPage();

        Assert.Equal(2, workspace.Pages.Count);
        Assert.Same(created, workspace.CurrentPage);
        Assert.NotSame(original, created);
        Assert.NotSame(original.Session, created.Session);
    }

    [Fact]
    public void RemovePage_SelectsAdjacentPage_WhenRemovingCurrent()
    {
        var workspace = new BoardWorkspace();
        BoardPage first = workspace.CurrentPage;
        BoardPage second = workspace.AddPage();

        Assert.Same(second, workspace.CurrentPage);

        bool removed = workspace.RemovePage(second);
        Assert.True(removed);

        Assert.Single(workspace.Pages);
        Assert.Same(first, workspace.CurrentPage);
        Assert.Equal(0, workspace.CurrentIndex);
    }

    [Fact]
    public void RemovePage_CreatesBlankPage_WhenRemovingLast()
    {
        var workspace = new BoardWorkspace();
        BoardPage only = workspace.CurrentPage;

        bool removed = workspace.RemovePage(only);
        Assert.True(removed);

        Assert.Single(workspace.Pages);
        Assert.Equal(0, workspace.CurrentIndex);
        Assert.NotSame(only, workspace.CurrentPage);
    }

    [Fact]
    public void TryMove_ReturnsFalse_WhenAtBoundary()
    {
        var workspace = new BoardWorkspace();

        Assert.False(workspace.TryMoveToPreviousPage());
        Assert.False(workspace.TryMoveToNextPage());

        workspace.AddPage();
        Assert.True(workspace.TryMoveToPreviousPage());
        Assert.True(workspace.TryMoveToNextPage());
    }

    [Fact]
    public void InsertPages_AdjustsCurrentIndex_WhenInsertingBeforeCurrent_AndNotSwitching()
    {
        var workspace = new BoardWorkspace();
        BoardPage p0 = workspace.CurrentPage;
        BoardPage p1 = workspace.AddPage();
        BoardPage p2 = workspace.AddPage();

        Assert.Equal(2, workspace.CurrentIndex);
        Assert.Same(p2, workspace.CurrentPage);

        // 在最前面插入 2 页，不切页：应保持“同一页”仍为当前页，只是索引后移 2。
        var a = new BoardPage();
        var b = new BoardPage();
        int start = workspace.InsertPages(0, new[] { a, b }, switchToFirstInsertedPage: false);

        Assert.Equal(0, start);
        Assert.Equal(5, workspace.Pages.Count);
        Assert.Equal(4, workspace.CurrentIndex);
        Assert.Same(p2, workspace.CurrentPage);
    }

    [Fact]
    public void InsertPages_SwitchesToFirstInsertedPage_WhenEnabled()
    {
        var workspace = new BoardWorkspace();
        BoardPage original = workspace.CurrentPage;
        workspace.AddPage();
        Assert.Equal(1, workspace.CurrentIndex);

        var inserted = new BoardPage();
        int start = workspace.InsertPages(0, new[] { inserted }, switchToFirstInsertedPage: true);

        Assert.Equal(0, start);
        Assert.Same(inserted, workspace.CurrentPage);
        Assert.Equal(0, workspace.CurrentIndex);
        Assert.Equal(3, workspace.Pages.Count);

        Assert.Same(original, workspace.Pages[1]);
    }

    [Fact]
    public void ReplacePageAt_ReplacesCurrentPage_AndKeepsIndex()
    {
        var workspace = new BoardWorkspace();
        BoardPage original = workspace.CurrentPage;
        int index = workspace.CurrentIndex;

        var replacement = new BoardPage();
        workspace.ReplacePageAt(index, replacement);

        Assert.Equal(index, workspace.CurrentIndex);
        Assert.Same(replacement, workspace.CurrentPage);
        Assert.NotSame(original, workspace.CurrentPage);
    }
}
