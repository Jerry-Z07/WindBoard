using WindBoard.Board;
using WindBoard.Board.Commands;
using Xunit;

namespace WindBoard.Tests.Board.Commands;

public sealed class AddStrokeCommandTests
{
    // Do/Undo/Redo 会保持首次执行时的插入位置
    [Fact]
    public void Do_Undo_Redo_KeepsOriginalInsertIndex()
    {
        var document = new BoardDocument();
        var a = new Stroke();
        document.InkItems.Add(a);

        var b = new Stroke();
        var command = new AddStrokeCommand(b);

        command.Do(document);
        Assert.Equal(2, document.InkItems.Count);
        Assert.Same(a, document.InkItems[0]);
        Assert.Same(b, document.InkItems[1]);

        command.Undo(document);
        Assert.Single(document.InkItems);
        Assert.Same(a, document.InkItems[0]);

        // 中途插入一个其它笔迹，再 redo：b 应插回原来的 index=1
        var x = new Stroke();
        document.InkItems.Add(x);

        command.Do(document);
        Assert.Equal(3, document.InkItems.Count);
        Assert.Same(a, document.InkItems[0]);
        Assert.Same(b, document.InkItems[1]);
        Assert.Same(x, document.InkItems[2]);
    }

    // Undo：当索引位置已变化时仍能移除对应笔迹
    [Fact]
    public void Undo_RemovesStrokeEvenIfIndexChanged()
    {
        var document = new BoardDocument();
        var a = new Stroke();
        document.InkItems.Add(a);

        var b = new Stroke();
        var command = new AddStrokeCommand(b);
        command.Do(document);

        // 让 b 不再处于其记录的 index=1 的位置，触发 Remove(_stroke) 分支。
        var x = new Stroke();
        document.InkItems.Insert(0, x);

        command.Undo(document);

        Assert.DoesNotContain(b, document.InkItems);
        Assert.Equal(2, document.InkItems.Count);
        Assert.Same(x, document.InkItems[0]);
        Assert.Same(a, document.InkItems[1]);
    }
}
