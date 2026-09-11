using System;
using System.Collections.Generic;
using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Editing;
using WindBoard.Board.Elements;
using WindBoard.Board.Items;
using WindBoard.Board.Viewport;
using WindBoard.Interaction.Tools;
using Vortice.Mathematics;
using Xunit;

namespace WindBoard.Tests.Interaction;

/// <summary>
/// 选择工具状态机用例：选中集归一化、框选提交、变换快照提交/取消。
/// </summary>
/// <remarks>
/// 选中集已泛化为 <see cref="IBoardInkItem"/>（design D）：笔迹与形状均可选中；
/// 矩阵变换仅作用于笔迹，形状仅支持平移。
/// </remarks>
public sealed class SelectToolTests
{
    // 视口 100×100：ScreenToWorld(p) = p - (50, 50)（zoom=1、camera=0）。
    private static (SelectTool Tool, BoardInputContext Context, BoardSession Session) CreateTool()
    {
        var viewport = new BoardViewport();
        viewport.UpdateViewportSize(new Vector2(100.0f, 100.0f));
        var session = new BoardSession();
        var context = new BoardInputContext(viewport, session);
        return (new SelectTool(context), context, session);
    }

    private static ToolInput Input(BoardInputContext context, Vector2 screenDip)
    {
        return new ToolInput(screenDip, 1.0f, default, context);
    }

    private static Stroke CreateStroke(Vector2 world)
    {
        var stroke = new Stroke { BaseSize = 4.0f, EnablePressure = false };
        stroke.Points.Add(new StrokePoint(world, 1.0f));
        stroke.ExpandBounds(world, 1.0f);
        return stroke;
    }

    private static BoardShape CreateShape(BoardShapeKind kind, Vector2 start, Vector2 end)
    {
        var shape = new BoardShape(kind);
        shape.SetGeometry(start, end);
        return shape;
    }

    [Fact]
    public void SetSelectionItems_NormalizesByDocumentOrder_AndFiltersForeignItems()
    {
        (SelectTool tool, _, BoardSession session) = CreateTool();
        Stroke first = CreateStroke(Vector2.Zero);
        Stroke second = CreateStroke(new Vector2(30.0f, 30.0f));
        Stroke foreign = CreateStroke(new Vector2(60.0f, 60.0f));
        session.Document.InkItems.Add(first);
        session.Document.InkItems.Add(second);

        // 传入乱序 + 文档外的笔迹。
        tool.SetSelectionItems(new[] { foreign, second, first });

        Assert.Equal(2, tool.SelectedItems.Count);
        Assert.Same(first, tool.SelectedItems[0]);
        Assert.Same(second, tool.SelectedItems[1]);
        // 多选时 SelectedItem 兼容语义返回 null。
        Assert.Null(tool.SelectedItem);
    }

    [Fact]
    public void SetSelectionItems_SameSetClearsElementSelection()
    {
        (SelectTool tool, _, BoardSession session) = CreateTool();
        Stroke stroke = CreateStroke(Vector2.Zero);
        session.Document.InkItems.Add(stroke);

        int changedCount = 0;
        tool.SelectionChanged += () => changedCount++;

        tool.SetSelectionItems(new[] { stroke });
        Assert.Null(tool.SelectedElement);
        Assert.Equal(1, changedCount);

        // 重复设置同一集合是幂等操作：不重复触发 SelectionChanged。
        tool.SetSelectionItems(tool.SelectedItems);
        Assert.Single(tool.SelectedItems);
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void ClearSelection_RemovesItemsAndElement()
    {
        (SelectTool tool, _, _) = CreateTool();

        tool.SetSelectionItems(new[] { CreateStroke(Vector2.Zero) });
        tool.ClearSelection();

        Assert.Empty(tool.SelectedItems);
        Assert.Null(tool.SelectedElement);
    }

    [Fact]
    public void Marquee_BeginMoveEnd_SmallRectTreatedAsClick_AndSelectsTopMostItem()
    {
        (SelectTool tool, BoardInputContext context, BoardSession session) = CreateTool();
        Stroke stroke = CreateStroke(new Vector2(10.0f, 10.0f));
        session.Document.InkItems.Add(stroke);

        // 点击世界点 (10,10) → 屏幕点 (60,60)（无抖动，矩形宽高 0 ≤ 阈值 6）。
        tool.Begin(Input(context, new Vector2(60.0f, 60.0f)));
        tool.End(Input(context, new Vector2(60.0f, 60.0f)));

        Assert.Single(tool.SelectedItems);
        Assert.Same(stroke, tool.SelectedItems[0]);
        Assert.Null(tool.LastMarqueeClickedElement);
    }

    [Fact]
    public void Marquee_LargeRectSelectsItemsInWorldRect()
    {
        (SelectTool tool, BoardInputContext context, BoardSession session) = CreateTool();
        Stroke inside = CreateStroke(new Vector2(10.0f, 10.0f));
        Stroke outside = CreateStroke(new Vector2(200.0f, 200.0f));
        session.Document.InkItems.Add(inside);
        session.Document.InkItems.Add(outside);

        // 屏幕矩形 (55,55)-(75,75) → 世界 (5,5)-(25,25)，包含 inside。
        tool.Begin(Input(context, new Vector2(55.0f, 55.0f)));
        tool.Move(Input(context, new Vector2(75.0f, 75.0f)));
        tool.End(Input(context, new Vector2(75.0f, 75.0f)));

        Assert.Single(tool.SelectedItems);
        Assert.Same(inside, tool.SelectedItems[0]);
    }

    [Fact]
    public void Marquee_ClickOnShape_SelectsShape()
    {
        (SelectTool tool, BoardInputContext context, BoardSession session) = CreateTool();
        BoardShape shape = CreateShape(BoardShapeKind.Rectangle, new Vector2(5.0f, 5.0f), new Vector2(25.0f, 25.0f));
        session.Document.InkItems.Add(shape);

        // 点击世界点 (15,15)（矩形内部）→ 屏幕点 (65,65)，按“点击”处理。
        tool.Begin(Input(context, new Vector2(65.0f, 65.0f)));
        tool.End(Input(context, new Vector2(65.0f, 65.0f)));

        Assert.Single(tool.SelectedItems);
        Assert.Same(shape, tool.SelectedItems[0]);
        // 点选命中形状不再按 Stroke 截断（design D）。
        Assert.Same(shape, tool.SelectedItem);
        Assert.Null(tool.SelectedElement);
    }

    [Fact]
    public void Marquee_LargeRectSelectsMixedItems_InDocumentOrder()
    {
        (SelectTool tool, BoardInputContext context, BoardSession session) = CreateTool();
        Stroke stroke = CreateStroke(new Vector2(10.0f, 10.0f));
        BoardShape shape = CreateShape(BoardShapeKind.Line, new Vector2(15.0f, 15.0f), new Vector2(25.0f, 25.0f));
        session.Document.InkItems.Add(stroke);
        session.Document.InkItems.Add(shape);

        // 屏幕矩形 (55,55)-(80,80) → 世界 (5,5)-(30,30)，同时包含笔迹与形状。
        tool.Begin(Input(context, new Vector2(55.0f, 55.0f)));
        tool.Move(Input(context, new Vector2(80.0f, 80.0f)));
        tool.End(Input(context, new Vector2(80.0f, 80.0f)));

        Assert.Equal(2, tool.SelectedItems.Count);
        // 框选按文档顺序归一化（design D）。
        Assert.Same(stroke, tool.SelectedItems[0]);
        Assert.Same(shape, tool.SelectedItems[1]);
    }

    [Fact]
    public void Marquee_ClickOnElement_SelectsElement_AndRecordsClickedElementForOpen()
    {
        (SelectTool tool, BoardInputContext context, BoardSession session) = CreateTool();

        // 直接构造真实元素对象（项目约定不使用 mock 框架）。
        var element = new BoardTextElement
        {
            PositionWorld = new Vector2(0.0f, 0.0f),
            SizeWorld = new Vector2(10.0f, 10.0f),
        };
        session.Document.ElementsAboveInk.Add(element);

        // 点击世界点 (5,5) → 屏幕点 (55,55)（无抖动，矩形宽高 0 ≤ 阈值 6，按“点击”处理）。
        tool.Begin(Input(context, new Vector2(55.0f, 55.0f)));
        tool.End(Input(context, new Vector2(55.0f, 55.0f)));

        Assert.Null(tool.SelectedItem);
        Assert.Same(element, tool.SelectedElement);
        // 命中元素与点击起点由 End 记录，控制器据此处理双击外部打开。
        Assert.Same(element, tool.LastMarqueeClickedElement);
        AssertEx.Equal(55.0f, tool.LastMarqueeClickScreenDip.X);
        AssertEx.Equal(55.0f, tool.LastMarqueeClickScreenDip.Y);
    }

    [Fact]
    public void Marquee_Cancel_DoesNotChangeSelection()
    {
        (SelectTool tool, BoardInputContext context, BoardSession session) = CreateTool();
        Stroke stroke = CreateStroke(new Vector2(10.0f, 10.0f));
        session.Document.InkItems.Add(stroke);

        tool.Begin(Input(context, new Vector2(55.0f, 55.0f)));
        tool.Move(Input(context, new Vector2(75.0f, 75.0f)));
        tool.Cancel();

        Assert.Empty(tool.SelectedItems);
        Assert.False(tool.TryGetMarqueeRectDip(out _));
    }

    [Fact]
    public void MoveSelectionByScreenDelta_TranslatesSelectedStrokes_AndMarksModified()
    {
        (SelectTool tool, BoardInputContext context, BoardSession session) = CreateTool();
        Stroke stroke = CreateStroke(new Vector2(10.0f, 10.0f));
        session.Document.InkItems.Add(stroke);
        tool.SetSelectionItems(new[] { stroke });

        tool.BeginSelectionMove();
        tool.MoveSelectionByScreenDelta(new Vector2(10.0f, 0.0f));

        AssertEx.Equal(20.0f, stroke.Points[0].Position.X);
        Assert.True(tool.SelectionModified);
    }

    [Fact]
    public void MoveSelectionByScreenDelta_TranslatesSelectedShape()
    {
        (SelectTool tool, BoardInputContext context, BoardSession session) = CreateTool();
        BoardShape shape = CreateShape(BoardShapeKind.Rectangle, new Vector2(5.0f, 5.0f), new Vector2(15.0f, 15.0f));
        session.Document.InkItems.Add(shape);
        tool.SetSelectionItems(new[] { shape });

        tool.BeginSelectionMove();
        tool.MoveSelectionByScreenDelta(new Vector2(10.0f, 0.0f));

        // 拖拽平移对形状生效（design D：形状仅支持平移）。
        AssertEx.Equal(15.0f, shape.Start.X);
        AssertEx.Equal(25.0f, shape.End.X);
        Assert.True(tool.SelectionModified);
    }

    [Fact]
    public void CommitSelection_ShapeMove_WritesGeometryCommand_AndUndoRestores()
    {
        (SelectTool tool, BoardInputContext context, BoardSession session) = CreateTool();
        BoardShape shape = CreateShape(BoardShapeKind.Rectangle, new Vector2(5.0f, 5.0f), new Vector2(15.0f, 15.0f));
        session.Document.InkItems.Add(shape);
        tool.SetSelectionItems(new[] { shape });

        tool.BeginSelectionMove();
        tool.MoveSelectionByScreenDelta(new Vector2(10.0f, 0.0f));
        tool.CommitSelection();

        Assert.False(tool.SelectionModified);
        Assert.True(session.CanUndo);
        AssertEx.Equal(15.0f, shape.Start.X);

        session.Undo();
        AssertEx.Equal(5.0f, shape.Start.X);
        AssertEx.Equal(15.0f, shape.End.X);
    }

    [Fact]
    public void CommitSelection_MixedSelection_WritesSingleCompositeCommand()
    {
        (SelectTool tool, BoardInputContext context, BoardSession session) = CreateTool();
        Stroke stroke = CreateStroke(new Vector2(10.0f, 10.0f));
        BoardShape shape = CreateShape(BoardShapeKind.Rectangle, new Vector2(5.0f, 5.0f), new Vector2(15.0f, 15.0f));
        session.Document.InkItems.Add(stroke);
        session.Document.InkItems.Add(shape);
        tool.SetSelectionItems(new IBoardInkItem[] { stroke, shape });

        // 混合选择拖拽一次：一次连续交互应合并为一次撤销记录（design D）。
        tool.BeginSelectionMove();
        tool.MoveSelectionByScreenDelta(new Vector2(10.0f, 0.0f));
        tool.CommitSelection();

        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
        AssertEx.Equal(20.0f, stroke.Points[0].Position.X);
        AssertEx.Equal(15.0f, shape.Start.X);

        // 一次 Ctrl+Z 同时恢复两者。
        session.Undo();
        AssertEx.Equal(10.0f, stroke.Points[0].Position.X);
        AssertEx.Equal(5.0f, shape.Start.X);
        AssertEx.Equal(15.0f, shape.End.X);

        // 重做同样一次恢复两者。
        session.Redo();
        AssertEx.Equal(20.0f, stroke.Points[0].Position.X);
        AssertEx.Equal(15.0f, shape.Start.X);
    }

    [Fact]
    public void ApplyMatrixTransformToSelectedStrokes_SkipsShapes()
    {
        (SelectTool tool, BoardInputContext context, BoardSession session) = CreateTool();
        Stroke stroke = CreateStroke(new Vector2(10.0f, 0.0f));
        BoardShape shape = CreateShape(BoardShapeKind.Rectangle, new Vector2(5.0f, 5.0f), new Vector2(15.0f, 15.0f));
        session.Document.InkItems.Add(stroke);
        session.Document.InkItems.Add(shape);
        tool.SetSelectionItems(new IBoardInkItem[] { stroke, shape });

        tool.BeginSelectionTransformSnapshotForSelectedItems();
        // 以原点为锚点旋转 90°：笔迹被变换，形状跳过（design D 已知边界）。
        tool.ApplyMatrixTransformToSelectedStrokes(Matrix3x2.CreateRotation(MathF.PI / 2.0f));

        AssertEx.Equal(0.0f, stroke.Points[0].Position.X, 0.0001f);
        AssertEx.Equal(10.0f, stroke.Points[0].Position.Y, 0.0001f);
        // 形状几何不受矩阵变换影响。
        AssertEx.Equal(5.0f, shape.Start.X, 0.0001f);
        AssertEx.Equal(5.0f, shape.Start.Y, 0.0001f);
        AssertEx.Equal(15.0f, shape.End.X, 0.0001f);
        AssertEx.Equal(15.0f, shape.End.Y, 0.0001f);
    }

    [Fact]
    public void TranslateSelectedItems_MovesMixedSelection()
    {
        (SelectTool tool, BoardInputContext context, BoardSession session) = CreateTool();
        Stroke stroke = CreateStroke(new Vector2(10.0f, 10.0f));
        BoardShape shape = CreateShape(BoardShapeKind.Line, new Vector2(5.0f, 5.0f), new Vector2(15.0f, 15.0f));
        session.Document.InkItems.Add(stroke);
        session.Document.InkItems.Add(shape);
        tool.SetSelectionItems(new IBoardInkItem[] { stroke, shape });
        tool.BeginSelectionTransformSnapshotForSelectedItems();

        tool.TranslateSelectedItems(new Vector2(2.0f, 0.0f));

        // 纯平移对形状与笔迹都生效。
        AssertEx.Equal(12.0f, stroke.Points[0].Position.X);
        AssertEx.Equal(7.0f, shape.Start.X);
        Assert.True(tool.SelectionModified);
    }

    [Fact]
    public void CancelSelection_RestoresShapeGeometry_WithoutCommand()
    {
        (SelectTool tool, BoardInputContext context, BoardSession session) = CreateTool();
        BoardShape shape = CreateShape(BoardShapeKind.Rectangle, new Vector2(5.0f, 5.0f), new Vector2(15.0f, 15.0f));
        session.Document.InkItems.Add(shape);
        tool.SetSelectionItems(new[] { shape });

        tool.BeginSelectionMove();
        tool.MoveSelectionByScreenDelta(new Vector2(10.0f, 0.0f));
        tool.CancelSelection();

        AssertEx.Equal(5.0f, shape.Start.X);
        AssertEx.Equal(15.0f, shape.End.X);
        Assert.False(tool.SelectionModified);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void CommitSelection_WithoutChanges_DoesNotWriteCommand()
    {
        (SelectTool tool, BoardInputContext context, BoardSession session) = CreateTool();
        Stroke stroke = CreateStroke(new Vector2(10.0f, 10.0f));
        session.Document.InkItems.Add(stroke);
        tool.SetSelectionItems(new[] { stroke });

        tool.BeginSelectionMove();
        // 未发生位移（delta 为 0 不标记修改）。
        tool.MoveSelectionByScreenDelta(Vector2.Zero);
        tool.CommitSelection();

        Assert.False(session.CanUndo);
    }

    [Fact]
    public void CancelSelection_RestoresPoints_WithoutCommand()
    {
        (SelectTool tool, BoardInputContext context, BoardSession session) = CreateTool();
        Stroke stroke = CreateStroke(new Vector2(10.0f, 10.0f));
        session.Document.InkItems.Add(stroke);
        tool.SetSelectionItems(new[] { stroke });

        tool.BeginSelectionMove();
        tool.MoveSelectionByScreenDelta(new Vector2(10.0f, 0.0f));
        tool.CancelSelection();

        AssertEx.Equal(10.0f, stroke.Points[0].Position.X);
        Assert.False(tool.SelectionModified);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void ValidateSelection_RemovesStrokesNoLongerInDocument()
    {
        (SelectTool tool, _, BoardSession session) = CreateTool();
        Stroke stroke = CreateStroke(new Vector2(10.0f, 10.0f));
        session.Document.InkItems.Add(stroke);
        tool.SetSelectionItems(new[] { stroke });

        // 模拟撤销导致笔迹被移除。
        session.Document.InkItems.Clear();
        tool.ValidateSelection();

        Assert.Empty(tool.SelectedItems);
    }

    [Fact]
    public void ValidateSelection_RemovesShapesNoLongerInDocument_AndKeepsValidMixedSelection()
    {
        (SelectTool tool, _, BoardSession session) = CreateTool();
        Stroke stroke = CreateStroke(new Vector2(10.0f, 10.0f));
        BoardShape removed = CreateShape(BoardShapeKind.Rectangle, new Vector2(5.0f, 5.0f), new Vector2(15.0f, 15.0f));
        session.Document.InkItems.Add(stroke);
        session.Document.InkItems.Add(removed);
        tool.SetSelectionItems(new IBoardInkItem[] { stroke, removed });

        // 模拟撤销导致形状被移除（笔迹仍在文档中）。
        session.Document.InkItems.Remove(removed);
        tool.ValidateSelection();

        // 幽灵形状被清理，有效条目保留（design D：撤销/重做后清理幽灵选择）。
        Assert.Single(tool.SelectedItems);
        Assert.Same(stroke, tool.SelectedItems[0]);
    }
}
