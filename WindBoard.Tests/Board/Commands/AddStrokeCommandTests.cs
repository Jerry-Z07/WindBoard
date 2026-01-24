using WindBoard.Board;
using WindBoard.Board.Commands;
using Xunit;

namespace WindBoard.Tests.Board.Commands;

public sealed class AddStrokeCommandTests
{
    [Fact]
    public void Do_Undo_Redo_会保持首次执行时的插入位置()
    {
        var document = new BoardDocument();
        var a = new Stroke();
        document.Strokes.Add(a);

        var b = new Stroke();
        var command = new AddStrokeCommand(b);

        command.Do(document);
        Assert.Equal(2, document.Strokes.Count);
        Assert.Same(a, document.Strokes[0]);
        Assert.Same(b, document.Strokes[1]);

        command.Undo(document);
        Assert.Single(document.Strokes);
        Assert.Same(a, document.Strokes[0]);

        // 中途插入一个其它笔迹，再 redo：b 应插回原来的 index=1
        var x = new Stroke();
        document.Strokes.Add(x);

        command.Do(document);
        Assert.Equal(3, document.Strokes.Count);
        Assert.Same(a, document.Strokes[0]);
        Assert.Same(b, document.Strokes[1]);
        Assert.Same(x, document.Strokes[2]);
    }

    [Fact]
    public void Undo_当索引位置已变化时仍能移除对应笔迹()
    {
        var document = new BoardDocument();
        var a = new Stroke();
        document.Strokes.Add(a);

        var b = new Stroke();
        var command = new AddStrokeCommand(b);
        command.Do(document);

        // 让 b 不再处于其记录的 index=1 的位置，触发 Remove(_stroke) 分支。
        var x = new Stroke();
        document.Strokes.Insert(0, x);

        command.Undo(document);

        Assert.DoesNotContain(b, document.Strokes);
        Assert.Equal(2, document.Strokes.Count);
        Assert.Same(x, document.Strokes[0]);
        Assert.Same(a, document.Strokes[1]);
    }
}

