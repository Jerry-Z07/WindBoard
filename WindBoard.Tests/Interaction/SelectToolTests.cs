using System.Collections.Generic;
using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Editing;
using WindBoard.Board.Elements;
using WindBoard.Board.Viewport;
using WindBoard.Interaction.Tools;
using Vortice.Mathematics;
using Xunit;

namespace WindBoard.Tests.Interaction;

/// <summary>
/// 选择工具状态机用例：选中集归一化、框选提交、变换快照提交/取消。
/// </summary>
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

    [Fact]
    public void SetSelectionStrokes_NormalizesByDocumentOrder_AndFiltersForeignStrokes()
    {
        (SelectTool tool, _, BoardSession session) = CreateTool();
        Stroke first = CreateStroke(Vector2.Zero);
        Stroke second = CreateStroke(new Vector2(30.0f, 30.0f));
        Stroke foreign = CreateStroke(new Vector2(60.0f, 60.0f));
        session.Document.InkItems.Add(first);
        session.Document.InkItems.Add(second);

        // 传入乱序 + 文档外的笔迹。
        tool.SetSelectionStrokes(new[] { foreign, second, first });

        Assert.Equal(2, tool.SelectedStrokes.Count);
        Assert.Same(first, tool.SelectedStrokes[0]);
        Assert.Same(second, tool.SelectedStrokes[1]);
        // 多选时 SelectedStroke 兼容语义返回 null。
        Assert.Null(tool.SelectedStroke);
    }

    [Fact]
    public void SetSelectionStrokes_SameSetClearsElementSelection()
    {
        (SelectTool tool, _, BoardSession session) = CreateTool();
        Stroke stroke = CreateStroke(Vector2.Zero);
        session.Document.InkItems.Add(stroke);

        int changedCount = 0;
        tool.SelectionChanged += () => changedCount++;

        tool.SetSelectionStrokes(new[] { stroke });
        Assert.Null(tool.SelectedElement);
        Assert.Equal(1, changedCount);

        // 重复设置同一集合是幂等操作：不重复触发 SelectionChanged。
        tool.SetSelectionStrokes(tool.SelectedStrokes);
        Assert.Single(tool.SelectedStrokes);
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void ClearSelection_RemovesStrokesAndElement()
    {
        (SelectTool tool, _, _) = CreateTool();

        tool.SetSelectionStrokes(new[] { CreateStroke(Vector2.Zero) });
        tool.ClearSelection();

        Assert.Empty(tool.SelectedStrokes);
        Assert.Null(tool.SelectedElement);
    }

    [Fact]
    public void Marquee_BeginMoveEnd_SmallRectTreatedAsClick_AndSelectsTopMostStroke()
    {
        (SelectTool tool, BoardInputContext context, BoardSession session) = CreateTool();
        Stroke stroke = CreateStroke(new Vector2(10.0f, 10.0f));
        session.Document.InkItems.Add(stroke);

        // 点击世界点 (10,10) → 屏幕点 (60,60)（无抖动，矩形宽高 0 ≤ 阈值 6）。
        tool.Begin(Input(context, new Vector2(60.0f, 60.0f)));
        tool.End(Input(context, new Vector2(60.0f, 60.0f)));

        Assert.Single(tool.SelectedStrokes);
        Assert.Same(stroke, tool.SelectedStrokes[0]);
        Assert.Null(tool.LastMarqueeClickedElement);
    }

    [Fact]
    public void Marquee_LargeRectSelectsStrokesInWorldRect()
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

        Assert.Single(tool.SelectedStrokes);
        Assert.Same(inside, tool.SelectedStrokes[0]);
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

        Assert.Null(tool.SelectedStroke);
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

        Assert.Empty(tool.SelectedStrokes);
        Assert.False(tool.TryGetMarqueeRectDip(out _));
    }

    [Fact]
    public void MoveSelectionByScreenDelta_TranslatesSelectedStrokes_AndMarksModified()
    {
        (SelectTool tool, BoardInputContext context, BoardSession session) = CreateTool();
        Stroke stroke = CreateStroke(new Vector2(10.0f, 10.0f));
        session.Document.InkItems.Add(stroke);
        tool.SetSelectionStrokes(new[] { stroke });

        tool.BeginSelectionMove();
        tool.MoveSelectionByScreenDelta(new Vector2(10.0f, 0.0f));

        AssertEx.Equal(20.0f, stroke.Points[0].Position.X);
        Assert.True(tool.SelectionModified);
    }

    [Fact]
    public void CommitSelection_WritesUndoCommand_AndUndoRestoresPoints()
    {
        (SelectTool tool, BoardInputContext context, BoardSession session) = CreateTool();
        Stroke stroke = CreateStroke(new Vector2(10.0f, 10.0f));
        session.Document.InkItems.Add(stroke);
        tool.SetSelectionStrokes(new[] { stroke });

        tool.BeginSelectionMove();
        tool.MoveSelectionByScreenDelta(new Vector2(10.0f, 0.0f));
        tool.CommitSelection();

        Assert.False(tool.SelectionModified);
        Assert.True(session.CanUndo);
        AssertEx.Equal(20.0f, stroke.Points[0].Position.X);

        session.Undo();
        AssertEx.Equal(10.0f, stroke.Points[0].Position.X);
    }

    [Fact]
    public void CommitSelection_WithoutChanges_DoesNotWriteCommand()
    {
        (SelectTool tool, BoardInputContext context, BoardSession session) = CreateTool();
        Stroke stroke = CreateStroke(new Vector2(10.0f, 10.0f));
        session.Document.InkItems.Add(stroke);
        tool.SetSelectionStrokes(new[] { stroke });

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
        tool.SetSelectionStrokes(new[] { stroke });

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
        tool.SetSelectionStrokes(new[] { stroke });

        // 模拟撤销导致笔迹被移除。
        session.Document.InkItems.Clear();
        tool.ValidateSelection();

        Assert.Empty(tool.SelectedStrokes);
    }
}
