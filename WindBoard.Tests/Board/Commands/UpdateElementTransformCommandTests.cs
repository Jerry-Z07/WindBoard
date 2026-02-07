using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Commands;
using WindBoard.Board.Elements;
using Xunit;

namespace WindBoard.Tests.Board.Commands;

public sealed class UpdateElementTransformCommandTests
{
    [Fact]
    public void Do_Undo_UpdatesPositionWorld_WhenSizeNotProvided()
    {
        var doc = new BoardDocument();
        var element = new BoardTextElement
        {
            Text = "hello",
            PositionWorld = new Vector2(1, 2),
            SizeWorld = new Vector2(10, 10),
        };

        doc.ElementsBelowInk.Add(element);

        var cmd = new UpdateElementTransformCommand(element, beforePositionWorld: new Vector2(1, 2), afterPositionWorld: new Vector2(5, 6));

        cmd.Do(doc);
        Assert.Equal(new Vector2(5, 6), element.PositionWorld);
        Assert.Equal(new Vector2(10, 10), element.SizeWorld);

        cmd.Undo(doc);
        Assert.Equal(new Vector2(1, 2), element.PositionWorld);
        Assert.Equal(new Vector2(10, 10), element.SizeWorld);
    }

    [Fact]
    public void Do_Undo_UpdatesPositionAndSizeWorld_WhenSizeProvided()
    {
        var doc = new BoardDocument();
        var element = new BoardTextElement
        {
            Text = "hello",
            PositionWorld = new Vector2(1, 2),
            SizeWorld = new Vector2(10, 10),
        };

        doc.ElementsBelowInk.Add(element);

        var cmd = new UpdateElementTransformCommand(
            element,
            beforePositionWorld: new Vector2(1, 2),
            afterPositionWorld: new Vector2(5, 6),
            beforeSizeWorld: new Vector2(10, 10),
            afterSizeWorld: new Vector2(20, 30));

        cmd.Do(doc);
        Assert.Equal(new Vector2(5, 6), element.PositionWorld);
        Assert.Equal(new Vector2(20, 30), element.SizeWorld);

        cmd.Undo(doc);
        Assert.Equal(new Vector2(1, 2), element.PositionWorld);
        Assert.Equal(new Vector2(10, 10), element.SizeWorld);
    }
}
