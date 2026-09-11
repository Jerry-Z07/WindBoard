using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Commands;
using WindBoard.Board.Editing;
using WindBoard.Board.Items;
using Vortice.Mathematics;
using Xunit;

namespace WindBoard.Tests.Board.Commands;

public sealed class UpdateShapeGeometryCommandTests
{
    [Fact]
    public void Do_AppliesAfterGeometry_AndRecalculatesBounds()
    {
        var shape = new BoardShape(BoardShapeKind.Line);
        shape.SetGeometry(new Vector2(0.0f, 0.0f), new Vector2(10.0f, 0.0f));

        var command = new UpdateShapeGeometryCommand(
            shape,
            before: (new Vector2(0.0f, 0.0f), new Vector2(10.0f, 0.0f)),
            after: (new Vector2(0.0f, 0.0f), new Vector2(20.0f, 10.0f)));

        command.Do(new BoardDocument());

        AssertEx.Equal(new Vector2(20.0f, 10.0f), shape.End);
        // 包围盒跟随新几何（Width 默认 3 → 外扩 1.5）。
        Rect bounds = shape.BoundsWorld;
        AssertEx.Equal(-1.5f, bounds.Left);
        AssertEx.Equal(-1.5f, bounds.Top);
        AssertEx.Equal(21.5f, bounds.Right);
        AssertEx.Equal(11.5f, bounds.Bottom);
    }

    [Fact]
    public void Undo_RestoresBeforeGeometry()
    {
        var shape = new BoardShape(BoardShapeKind.Rectangle);
        shape.SetGeometry(new Vector2(0.0f, 0.0f), new Vector2(10.0f, 10.0f));

        var command = new UpdateShapeGeometryCommand(
            shape,
            before: (new Vector2(0.0f, 0.0f), new Vector2(10.0f, 10.0f)),
            after: (new Vector2(0.0f, 0.0f), new Vector2(4.0f, 6.0f)));

        command.Do(new BoardDocument());
        command.Undo(new BoardDocument());

        AssertEx.Equal(new Vector2(10.0f, 10.0f), shape.End);
        Rect bounds = shape.BoundsWorld;
        AssertEx.Equal(11.5f, bounds.Right);
        AssertEx.Equal(11.5f, bounds.Bottom);
    }

    [Fact]
    public void Execute_Undo_Redo_KeepsGeometrySymmetry()
    {
        var session = new BoardSession();
        var shape = new BoardShape(BoardShapeKind.Ellipse);
        shape.SetGeometry(new Vector2(0.0f, 0.0f), new Vector2(10.0f, 10.0f));

        session.Execute(new UpdateShapeGeometryCommand(
            shape,
            before: (new Vector2(0.0f, 0.0f), new Vector2(10.0f, 10.0f)),
            after: (new Vector2(5.0f, 5.0f), new Vector2(30.0f, 20.0f))));

        AssertEx.Equal(new Vector2(30.0f, 20.0f), shape.End);

        session.Undo();
        AssertEx.Equal(new Vector2(10.0f, 10.0f), shape.End);
        AssertEx.Equal(new Vector2(0.0f, 0.0f), shape.Start);

        session.Redo();
        AssertEx.Equal(new Vector2(30.0f, 20.0f), shape.End);
        AssertEx.Equal(new Vector2(5.0f, 5.0f), shape.Start);
    }
}
