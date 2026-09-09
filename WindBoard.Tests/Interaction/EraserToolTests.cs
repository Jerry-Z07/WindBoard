using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Editing;
using WindBoard.Board.Viewport;
using WindBoard.Interaction.Tools;
using Vortice.Mathematics;
using Xunit;

namespace WindBoard.Tests.Interaction;

/// <summary>
/// 橡皮工具状态机用例：Begin/Move/End/Cancel 行为与原控制器实现等价（快照 + 单条命令）。
/// </summary>
public sealed class EraserToolTests
{
    /// <summary>
    /// 手写擦除策略 stub：记录调用参数，可选“删除文档第一条条目”模拟像素擦除生效。
    /// </summary>
    private sealed class StubEraser : IBoardEraser
    {
        public int CallCount { get; private set; }

        public Vector2 LastFromWorld { get; private set; }

        public Vector2 LastToWorld { get; private set; }

        public Vector2 LastRadiusWorld { get; private set; }

        public bool EraseOnCall { get; set; }

        public bool Erase(BoardDocument document, Vector2 fromWorld, Vector2 toWorld, Vector2 radiusWorld)
        {
            CallCount++;
            LastFromWorld = fromWorld;
            LastToWorld = toWorld;
            LastRadiusWorld = radiusWorld;

            if (EraseOnCall && document.InkItems.Count > 0)
            {
                document.InkItems.RemoveAt(0);
                return true;
            }

            return false;
        }
    }

    // 视口 100×100：ScreenToWorld(p) = p - (50, 50)（zoom=1、camera=0）。
    private static (EraserTool Tool, BoardInputContext Context, BoardSession Session, StubEraser Eraser) CreateTool(
        Vector2? radiusDip = null,
        float zoom = 1.0f)
    {
        var viewport = new BoardViewport();
        viewport.UpdateViewportSize(new Vector2(100.0f, 100.0f));
        if (Math.Abs(zoom - 1.0f) > 0.0001f)
        {
            viewport.SetView(Vector2.Zero, zoom);
        }

        var session = new BoardSession();
        var context = new BoardInputContext(viewport, session);
        var eraser = new StubEraser();
        var tool = new EraserTool(eraser)
        {
            RadiusDip = radiusDip ?? new Vector2(24.0f, 36.0f),
        };
        return (tool, context, session, eraser);
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
    public void Begin_CapturesSnapshot_ErasesAtPoint_AndSetsErasingFlag()
    {
        (EraserTool tool, BoardInputContext context, BoardSession session, StubEraser eraser) = CreateTool();
        session.Document.InkItems.Add(CreateStroke(new Vector2(10.0f, 10.0f)));

        tool.Begin(Input(context, new Vector2(60.0f, 60.0f)));

        Assert.True(tool.IsErasing);
        Assert.Equal(1, eraser.CallCount);
        // 屏幕点 (60,60) → 世界 (10,10)。
        AssertEx.Equal(10.0f, eraser.LastFromWorld.X);
        AssertEx.Equal(10.0f, eraser.LastFromWorld.Y);
        AssertEx.Equal(10.0f, eraser.LastToWorld.X);
    }

    [Fact]
    public void Begin_RadiusDipIsConvertedToWorld_ByZoom()
    {
        (EraserTool tool, BoardInputContext context, _, StubEraser eraser) = CreateTool(
            radiusDip: new Vector2(20.0f, 30.0f),
            zoom: 2.0f);

        tool.Begin(Input(context, new Vector2(60.0f, 60.0f)));

        AssertEx.Equal(10.0f, eraser.LastRadiusWorld.X);
        AssertEx.Equal(15.0f, eraser.LastRadiusWorld.Y);
    }

    [Fact]
    public void Move_AppliesSegmentFromLastPosition_WhenBeyondMinDistance()
    {
        (EraserTool tool, BoardInputContext context, _, StubEraser eraser) = CreateTool();

        tool.Begin(Input(context, new Vector2(60.0f, 60.0f)));
        tool.Move(Input(context, new Vector2(65.0f, 60.0f)));

        Assert.Equal(2, eraser.CallCount);
        // 线段：上一位置 (10,10) → 当前 (15,10)。
        AssertEx.Equal(10.0f, eraser.LastFromWorld.X);
        AssertEx.Equal(15.0f, eraser.LastToWorld.X);
    }

    [Fact]
    public void Move_SkipsSegment_WhenWithinMinDistance()
    {
        (EraserTool tool, BoardInputContext context, _, StubEraser eraser) = CreateTool();

        tool.Begin(Input(context, new Vector2(60.0f, 60.0f)));

        // 距离 0.2 世界单位 < 0.75（zoom=1）。
        tool.Move(Input(context, new Vector2(60.2f, 60.0f)));

        Assert.Equal(1, eraser.CallCount);
    }

    [Fact]
    public void End_CommitsReplaceCommand_WhenDocumentChanged()
    {
        (EraserTool tool, BoardInputContext context, BoardSession session, StubEraser eraser) = CreateTool();
        session.Document.InkItems.Add(CreateStroke(new Vector2(10.0f, 10.0f)));

        tool.Begin(Input(context, new Vector2(60.0f, 60.0f)));
        eraser.EraseOnCall = true;
        // 距离 5 世界单位 > 0.75 最小采样距离，触发一次擦除并删除条目。
        tool.Move(Input(context, new Vector2(65.0f, 60.0f)));
        tool.End(Input(context, new Vector2(65.0f, 60.0f)));

        Assert.False(tool.IsErasing);
        Assert.Empty(session.Document.InkItems);
        Assert.True(session.CanUndo);
        session.Undo();
        Assert.Single(session.Document.InkItems);
    }

    [Fact]
    public void End_DoesNotCommit_WhenDocumentUnchanged()
    {
        (EraserTool tool, BoardInputContext context, BoardSession session, StubEraser eraser) = CreateTool();
        session.Document.InkItems.Add(CreateStroke(new Vector2(10.0f, 10.0f)));

        tool.Begin(Input(context, new Vector2(60.0f, 60.0f)));
        tool.End(Input(context, new Vector2(60.0f, 60.0f)));

        Assert.False(tool.IsErasing);
        Assert.Single(session.Document.InkItems);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Cancel_RestoresSnapshot_WithoutCommand()
    {
        (EraserTool tool, BoardInputContext context, BoardSession session, StubEraser eraser) = CreateTool();
        session.Document.InkItems.Add(CreateStroke(new Vector2(10.0f, 10.0f)));

        tool.Begin(Input(context, new Vector2(60.0f, 60.0f)));
        eraser.EraseOnCall = true;
        // 距离 5 世界单位 > 0.75 最小采样距离，触发一次擦除并删除条目。
        tool.Move(Input(context, new Vector2(65.0f, 60.0f)));
        tool.Cancel();

        Assert.False(tool.IsErasing);
        // 恢复擦除前快照，且不写入撤销栈。
        Assert.Single(session.Document.InkItems);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void End_WithoutActiveSession_IsNoOp()
    {
        (EraserTool tool, BoardInputContext context, BoardSession session, _) = CreateTool();

        tool.End(Input(context, new Vector2(60.0f, 60.0f)));

        Assert.False(tool.IsErasing);
        Assert.False(session.CanUndo);
    }
}
