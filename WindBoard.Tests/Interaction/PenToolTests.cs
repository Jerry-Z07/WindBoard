using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Editing;
using WindBoard.Board.Viewport;
using WindBoard.Interaction;
using WindBoard.Interaction.Tools;
using Vortice.Mathematics;
using Xunit;

namespace WindBoard.Tests.Interaction;

/// <summary>
/// 画笔工具状态机用例：Begin/Move/End/Cancel 的行为与原控制器实现等价。
/// </summary>
public sealed class PenToolTests
{
    // 视口 100×100：ScreenToWorld(p) = p - (50, 50)（zoom=1、camera=0）。
    private static (PenTool Tool, BoardInputContext Context, BoardSession Session) CreateTool(
        Color4? penColor = null,
        float penBaseSize = 3.0f,
        bool enablePressure = true)
    {
        var viewport = new BoardViewport();
        viewport.UpdateViewportSize(new Vector2(100.0f, 100.0f));
        var session = new BoardSession();
        var context = new BoardInputContext(viewport, session)
        {
            ToolOptions = new ToolOptions(
                BoardTool.Pen,
                penColor ?? new Color4(1.0f, 0.0f, 0.0f, 1.0f),
                penBaseSize,
                enablePressure),
        };
        return (new PenTool(), context, session);
    }

    private static ToolInput Input(BoardInputContext context, Vector2 screenDip, float pressure = 1.0f)
    {
        return new ToolInput(screenDip, pressure, default, context);
    }

    [Fact]
    public void Begin_CreatesActiveStroke_WithPenOptionsAndExposesPreviewItem()
    {
        (PenTool tool, BoardInputContext context, _) = CreateTool(penBaseSize: 7.0f, enablePressure: false);

        tool.Begin(Input(context, new Vector2(60.0f, 60.0f)));

        Assert.NotNull(tool.ActiveStroke);
        Assert.Same(tool.ActiveStroke, context.PreviewItem);
        Assert.True(context.PreviewItem is Stroke);
        Stroke stroke = (Stroke)context.PreviewItem!;
        Assert.Single(stroke.Points);
        AssertEx.Equal(10.0f, stroke.Points[0].Position.X);
        AssertEx.Equal(10.0f, stroke.Points[0].Position.Y);
        Assert.Equal(7.0f, stroke.BaseSize);
        Assert.False(stroke.EnablePressure);
        Assert.Equal(1.0f, stroke.Points[0].Pressure);
    }

    [Fact]
    public void Move_AppendsPointBeyondMinDistance_AndRequestsDirtyRect()
    {
        (PenTool tool, BoardInputContext context, _) = CreateTool();

        tool.Begin(Input(context, new Vector2(60.0f, 60.0f)));
        context.ClearStrokeDirtyRect();
        tool.Move(Input(context, new Vector2(70.0f, 60.0f)));

        Stroke stroke = tool.ActiveStroke!;
        Assert.Equal(2, stroke.Points.Count);
        AssertEx.Equal(20.0f, stroke.Points[1].Position.X);
        Assert.NotNull(context.PeekStrokeDirtyRect());
    }

    [Fact]
    public void Move_SkipsPointWithinMinDistance()
    {
        (PenTool tool, BoardInputContext context, _) = CreateTool();

        tool.Begin(Input(context, new Vector2(60.0f, 60.0f)));
        context.ClearStrokeDirtyRect();

        // 与首点距离 0.2 世界单位 < 0.5（zoom=1），不应追加点。
        tool.Move(Input(context, new Vector2(60.2f, 60.0f)));

        Assert.Single(tool.ActiveStroke!.Points);
        Assert.Null(context.PeekStrokeDirtyRect());
    }

    [Fact]
    public void Move_WithoutActiveSession_IsNoOp()
    {
        (PenTool tool, BoardInputContext context, _) = CreateTool();

        tool.Move(Input(context, new Vector2(60.0f, 60.0f)));

        Assert.Null(tool.ActiveStroke);
        Assert.Null(context.PreviewItem);
    }

    [Fact]
    public void End_CommitsStrokeViaCommand_AndClearsPreview()
    {
        (PenTool tool, BoardInputContext context, BoardSession session) = CreateTool();

        tool.Begin(Input(context, new Vector2(60.0f, 60.0f)));
        tool.Move(Input(context, new Vector2(70.0f, 60.0f)));
        tool.End(Input(context, new Vector2(70.0f, 60.0f)));

        Assert.Null(tool.ActiveStroke);
        Assert.Null(context.PreviewItem);
        Assert.Null(context.PeekStrokeDirtyRect());
        Assert.Single(session.Document.InkItems);
        Assert.True(session.CanUndo);
    }

    [Fact]
    public void Cancel_DiscardsStroke_WithoutCommand()
    {
        (PenTool tool, BoardInputContext context, BoardSession session) = CreateTool();

        tool.Begin(Input(context, new Vector2(60.0f, 60.0f)));
        tool.Move(Input(context, new Vector2(70.0f, 60.0f)));
        tool.Cancel();

        Assert.Null(tool.ActiveStroke);
        Assert.Empty(session.Document.InkItems);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Begin_AppliesPressureFromInput_WhenEnabled()
    {
        (PenTool tool, BoardInputContext context, _) = CreateTool(enablePressure: true);

        tool.Begin(Input(context, new Vector2(60.0f, 60.0f), pressure: 0.5f));

        Stroke stroke = tool.ActiveStroke!;
        Assert.Equal(0.5f, stroke.Points[0].Pressure);
    }

    [Fact]
    public void Begin_IgnoresPressureNormalization_WhenDisabled()
    {
        (PenTool tool, BoardInputContext context, _) = CreateTool(enablePressure: false);

        // 压感仍写入点集（渲染端按 EnablePressure 决定是否使用），与原实现一致。
        tool.Begin(Input(context, new Vector2(60.0f, 60.0f), pressure: 0.4f));

        Stroke stroke = tool.ActiveStroke!;
        Assert.Equal(0.4f, stroke.Points[0].Pressure);
    }

    [Fact]
    public void Move_KeepsParameterSnapshotFromBegin_WhenOptionsChangeMidStroke()
    {
        (PenTool tool, BoardInputContext context, _) = CreateTool(penBaseSize: 5.0f);

        tool.Begin(Input(context, new Vector2(60.0f, 60.0f)));

        // Begin 后变更参数：ToolInput/Context 中是最新值，但进行中的笔迹保持 Begin 时刻的值拷贝快照。
        context.ToolOptions = context.ToolOptions with
        {
            PenColor = new Color4(0f, 1f, 0f, 1f),
            PenBaseSize = 11.0f,
            PenEnablePressure = false,
        };
        tool.Move(Input(context, new Vector2(70.0f, 60.0f)));

        Stroke stroke = tool.ActiveStroke!;
        Assert.Equal(2, stroke.Points.Count);
        Assert.Equal(1.0f, stroke.Color.R);
        Assert.Equal(5.0f, stroke.BaseSize);
        Assert.True(stroke.EnablePressure);
    }

    [Fact]
    public void Begin_UsesLatestOptions_ForEachNewStroke()
    {
        (PenTool tool, BoardInputContext context, BoardSession session) = CreateTool(penBaseSize: 3.0f);

        tool.Begin(Input(context, new Vector2(60.0f, 60.0f)));
        tool.End(Input(context, new Vector2(70.0f, 60.0f)));

        // 提交后变更参数，参数变更只影响后续新建笔迹。
        context.ToolOptions = context.ToolOptions with
        {
            PenColor = new Color4(0f, 0f, 1f, 1f),
            PenBaseSize = 9.0f,
        };
        tool.Begin(Input(context, new Vector2(60.0f, 60.0f)));

        Stroke committed = Assert.IsType<Stroke>(session.Document.InkItems[0]);
        Assert.Equal(3.0f, committed.BaseSize);
        Stroke next = tool.ActiveStroke!;
        Assert.Equal(0.0f, next.Color.R);
        Assert.Equal(1.0f, next.Color.B);
        Assert.Equal(9.0f, next.BaseSize);
    }
}
