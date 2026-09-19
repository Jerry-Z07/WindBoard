using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Editing;
using WindBoard.Board.Items;
using WindBoard.Board.Viewport;
using WindBoard.Interaction;
using WindBoard.Interaction.Tools;
using Vortice.Mathematics;
using Xunit;

namespace WindBoard.Tests.Interaction;

/// <summary>
/// 形状工具状态机用例：Begin/Move/End/Cancel 的行为与 PenTool 模式对齐（design B）。
/// </summary>
public sealed class ShapeToolTests
{
    // 视口 100×100：ScreenToWorld(p) = p - (50, 50)（zoom=1、camera=0）。
    private static (ShapeTool Tool, BoardInputContext Context, BoardSession Session) CreateTool(
        BoardShapeKind kind,
        Color4? penColor = null,
        float penBaseSize = 3.0f)
    {
        var viewport = new BoardViewport();
        viewport.UpdateViewportSize(new Vector2(100.0f, 100.0f));
        var session = new BoardSession();
        var context = new BoardInputContext(viewport, session)
        {
            ToolOptions = new ToolOptions(
                BoardTool.Line,
                penColor ?? new Color4(1.0f, 0.0f, 0.0f, 1.0f),
                penBaseSize,
                PenEnablePressure: true),
        };
        return (new ShapeTool(kind), context, session);
    }

    private static ToolInput Input(BoardInputContext context, Vector2 screenDip, float pressure = 1.0f)
    {
        return new ToolInput(screenDip, pressure, default, context);
    }

    [Fact]
    public void Begin_CreatesActiveShape_WithPenOptionsAndExposesPreviewItem()
    {
        (ShapeTool tool, BoardInputContext context, _) = CreateTool(BoardShapeKind.Rectangle, penBaseSize: 7.0f);

        tool.Begin(Input(context, new Vector2(60.0f, 60.0f)));

        Assert.NotNull(tool.ActiveShape);
        Assert.Same(tool.ActiveShape, context.PreviewItem);
        BoardShape shape = tool.ActiveShape!;
        Assert.Equal(BoardShapeKind.Rectangle, shape.Kind);
        // Start = End = 按下点（世界坐标）。
        AssertEx.Equal(new Vector2(10.0f, 10.0f), shape.Start);
        AssertEx.Equal(new Vector2(10.0f, 10.0f), shape.End);
        // 参数快照：颜色/线宽来自 ToolOptions；压感忽略（形状不支持压感）。
        Assert.Equal(7.0f, shape.Width, precision: 5);
        Assert.Equal(1.0f, shape.Color.R, precision: 5);
        Assert.Equal(0.0f, shape.Color.G, precision: 5);
    }

    [Fact]
    public void Begin_IgnoresInputPressure()
    {
        (ShapeTool tool, BoardInputContext context, _) = CreateTool(BoardShapeKind.Line, penBaseSize: 5.0f);

        // 输入压感 0.4：形状线宽不随压感变化（Width 来自 PenBaseSize 快照）。
        tool.Begin(Input(context, new Vector2(60.0f, 60.0f), pressure: 0.4f));
        tool.Move(Input(context, new Vector2(80.0f, 60.0f), pressure: 0.2f));

        Assert.Equal(5.0f, tool.ActiveShape!.Width, precision: 5);
    }

    [Fact]
    public void Move_UpdatesEnd_AndRequestsDirtyRect()
    {
        (ShapeTool tool, BoardInputContext context, _) = CreateTool(BoardShapeKind.Line);

        tool.Begin(Input(context, new Vector2(60.0f, 60.0f)));
        context.ClearStrokeDirtyRect();
        tool.Move(Input(context, new Vector2(80.0f, 60.0f)));

        BoardShape shape = tool.ActiveShape!;
        AssertEx.Equal(new Vector2(30.0f, 10.0f), shape.End);
        AssertEx.Equal(new Vector2(10.0f, 10.0f), shape.Start);
        Assert.NotNull(context.PeekStrokeDirtyRect());
    }

    [Fact]
    public void Move_RequestsDirtyRect_CoveringOldAndNewGeometry()
    {
        (ShapeTool tool, BoardInputContext context, _) = CreateTool(BoardShapeKind.Line, penBaseSize: 3.0f);

        tool.Begin(Input(context, new Vector2(60.0f, 60.0f)));
        context.ClearStrokeDirtyRect();

        // 先拖到 A=(20,10)（世界），再拖回到起点附近 B=(10,10)：端点回退，
        // 脏矩形必须保留旧几何区域（避免旧位置残影）。
        tool.Move(Input(context, new Vector2(70.0f, 60.0f)));
        tool.Move(Input(context, new Vector2(60.0f, 60.0f)));

        Rect? dirty = context.PeekStrokeDirtyRect();
        Assert.NotNull(dirty);
        Rect rect = dirty.Value;
        // zoom=1：世界 bounds 外扩半宽 1.5 + 屏幕 +50 偏移 + 额外 padding 2。
        // 旧几何（A 端）右边界：21.5 + 50 + 2 = 73.5；起点（B 端）左边界：8.5 + 50 - 2 = 56.5。
        AssertEx.Equal(56.5f, rect.Left);
        AssertEx.Equal(73.5f, rect.Right);
        AssertEx.Equal(56.5f, rect.Top);
        AssertEx.Equal(63.5f, rect.Bottom);
    }

    [Fact]
    public void End_CommitsShapeViaCommand_AndClearsPreview()
    {
        (ShapeTool tool, BoardInputContext context, BoardSession session) = CreateTool(BoardShapeKind.Ellipse);

        tool.Begin(Input(context, new Vector2(60.0f, 60.0f)));
        tool.Move(Input(context, new Vector2(80.0f, 80.0f)));
        tool.End(Input(context, new Vector2(80.0f, 80.0f)));

        Assert.Null(tool.ActiveShape);
        Assert.Null(context.PreviewItem);
        Assert.Null(context.PeekStrokeDirtyRect());
        Assert.Single(session.Document.InkItems);
        var committed = Assert.IsType<BoardShape>(session.Document.InkItems[0]);
        Assert.Equal(BoardShapeKind.Ellipse, committed.Kind);
        Assert.True(session.CanUndo);
    }

    [Fact]
    public void End_DiscardsDegenerateGeometry_WithoutCommand()
    {
        (ShapeTool tool, BoardInputContext context, BoardSession session) = CreateTool(BoardShapeKind.Line);

        // 点击未拖动（长度 0 < 1e-3 世界单位）：丢弃不提交。
        tool.Begin(Input(context, new Vector2(60.0f, 60.0f)));
        tool.End(Input(context, new Vector2(60.0f, 60.0f)));

        Assert.Null(tool.ActiveShape);
        Assert.Null(context.PreviewItem);
        Assert.Empty(session.Document.InkItems);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void End_CommitsShape_RecordsLastCommittedShape()
    {
        (ShapeTool tool, BoardInputContext context, BoardSession session) = CreateTool(BoardShapeKind.Rectangle);

        tool.Begin(Input(context, new Vector2(60.0f, 60.0f)));
        tool.Move(Input(context, new Vector2(80.0f, 80.0f)));
        tool.End(Input(context, new Vector2(80.0f, 80.0f)));

        // 提交结果供控制器抛 ShapeCommitted（宿主据此自动选中）：必须是已入文档的同一实例。
        BoardShape committed = Assert.IsType<BoardShape>(session.Document.InkItems[0]);
        Assert.Same(committed, tool.LastCommittedShape);
    }

    [Fact]
    public void End_DiscardsDegenerateGeometry_ClearsPreviousCommitResult()
    {
        (ShapeTool tool, BoardInputContext context, BoardSession session) = CreateTool(BoardShapeKind.Line);

        tool.Begin(Input(context, new Vector2(60.0f, 60.0f)));
        tool.Move(Input(context, new Vector2(80.0f, 60.0f)));
        tool.End(Input(context, new Vector2(80.0f, 60.0f)));
        Assert.NotNull(tool.LastCommittedShape);

        // 点击未拖动：退化几何被丢弃，不得残留上一次结果（否则宿主会重复选中上一个形状）。
        tool.Begin(Input(context, new Vector2(60.0f, 60.0f)));
        tool.End(Input(context, new Vector2(60.0f, 60.0f)));

        Assert.Null(tool.LastCommittedShape);
        Assert.Single(session.Document.InkItems);
    }

    [Fact]
    public void Move_WithoutActiveSession_IsNoOp()
    {
        (ShapeTool tool, BoardInputContext context, _) = CreateTool(BoardShapeKind.Line);

        tool.Move(Input(context, new Vector2(60.0f, 60.0f)));

        Assert.Null(tool.ActiveShape);
        Assert.Null(context.PreviewItem);
    }

    [Fact]
    public void Cancel_DiscardsShape_WithoutCommand()
    {
        (ShapeTool tool, BoardInputContext context, BoardSession session) = CreateTool(BoardShapeKind.Arrow);

        tool.Begin(Input(context, new Vector2(60.0f, 60.0f)));
        tool.Move(Input(context, new Vector2(80.0f, 80.0f)));
        tool.Cancel();

        Assert.Null(tool.ActiveShape);
        Assert.Empty(session.Document.InkItems);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Move_KeepsParameterSnapshotFromBegin_WhenOptionsChangeMidStroke()
    {
        (ShapeTool tool, BoardInputContext context, BoardSession session) = CreateTool(BoardShapeKind.Rectangle, penBaseSize: 5.0f);

        tool.Begin(Input(context, new Vector2(60.0f, 60.0f)));

        // Begin 后变更参数：进行中的形状保持 Begin 时刻的值拷贝快照（与 PenTool 语义一致）。
        context.ToolOptions = context.ToolOptions with
        {
            PenColor = new Color4(0f, 1f, 0f, 1f),
            PenBaseSize = 11.0f,
        };
        tool.Move(Input(context, new Vector2(80.0f, 80.0f)));
        tool.End(Input(context, new Vector2(80.0f, 80.0f)));

        var committed = Assert.IsType<BoardShape>(session.Document.InkItems[0]);
        Assert.Equal(5.0f, committed.Width, precision: 5);
        Assert.Equal(1.0f, committed.Color.R, precision: 5);
    }

    [Fact]
    public void Begin_UsesLatestOptions_ForEachNewShape()
    {
        (ShapeTool tool, BoardInputContext context, BoardSession session) = CreateTool(BoardShapeKind.Line, penBaseSize: 3.0f);

        tool.Begin(Input(context, new Vector2(60.0f, 60.0f)));
        tool.Move(Input(context, new Vector2(80.0f, 60.0f)));
        tool.End(Input(context, new Vector2(80.0f, 60.0f)));

        // 提交后变更参数，参数变更只影响后续新建形状。
        context.ToolOptions = context.ToolOptions with
        {
            PenColor = new Color4(0f, 0f, 1f, 1f),
            PenBaseSize = 9.0f,
        };
        tool.Begin(Input(context, new Vector2(60.0f, 60.0f)));

        var committed = Assert.IsType<BoardShape>(session.Document.InkItems[0]);
        Assert.Equal(3.0f, committed.Width, precision: 5);
        BoardShape next = tool.ActiveShape!;
        Assert.Equal(0.0f, next.Color.R, precision: 5);
        Assert.Equal(1.0f, next.Color.B, precision: 5);
        Assert.Equal(9.0f, next.Width, precision: 5);
    }
}
