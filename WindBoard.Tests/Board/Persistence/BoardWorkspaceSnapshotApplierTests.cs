using System;
using System.Collections.Generic;
using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Editing;
using WindBoard.Board.Persistence;
using Vortice.Mathematics;
using Xunit;

namespace WindBoard.Tests.Board.Persistence;

public sealed class BoardWorkspaceSnapshotApplierTests
{
    [Fact]
    public void CreatePages_RebuildsStrokesWithValuesBoundsAndZOrder()
    {
        // 收敛后行为等价性：快照 → 域重建（Applier 主链路）按快照顺序重建 Stroke，
        // 并由 Codec 单点重算 Bounds（导入后选择/变换依赖 Bounds）。
        Guid pageId = Guid.NewGuid();
        var page = new BoardPageSnapshot(
            pageId,
            Strokes:
            [
                new InkItemSnapshot
                {
                    Stroke = new StrokeSnapshot(
                        Points:
                        [
                            new StrokePointSnapshot(new Vector2(0.0f, 0.0f), 1.0f),
                            new StrokePointSnapshot(new Vector2(10.0f, 0.0f), 1.0f),
                        ],
                        ColorRgba: new Vector4(0.1f, 0.2f, 0.3f, 1.0f),
                        BaseSize: 6.0f,
                        EnablePressure: false),
                },
                new InkItemSnapshot
                {
                    Stroke = new StrokeSnapshot(
                        Points:
                        [
                            new StrokePointSnapshot(new Vector2(100.0f, 0.0f), 0.5f),
                            new StrokePointSnapshot(new Vector2(110.0f, 0.0f), 0.5f),
                        ],
                        ColorRgba: new Vector4(1.0f, 0.0f, 0.0f, 0.5f),
                        BaseSize: 8.0f,
                        EnablePressure: true),
                },
            ]);

        var snapshot = new BoardWorkspaceSnapshot([page], CurrentIndex: 0);

        List<BoardPage> pages = BoardWorkspaceSnapshotApplier.CreatePages(snapshot);

        BoardPage applied = Assert.Single(pages);
        Assert.Equal(pageId, applied.Id);
        Assert.Equal(2, applied.Session.Document.InkItems.Count);

        // z-order：按快照顺序保持。
        var first = Assert.IsType<Stroke>(applied.Session.Document.InkItems[0]);
        var second = Assert.IsType<Stroke>(applied.Session.Document.InkItems[1]);

        // 字段等价：颜色/粗细/压感/点集。
        Assert.Equal(0.1f, first.Color.R, precision: 5);
        Assert.Equal(0.3f, first.Color.B, precision: 5);
        Assert.Equal(6.0f, first.BaseSize, precision: 5);
        Assert.False(first.EnablePressure);
        Assert.Equal(2, first.Points.Count);
        Assert.Equal(10.0f, first.Points[1].Position.X, precision: 5);

        Assert.Equal(1.0f, second.Color.R, precision: 5);
        Assert.Equal(0.5f, second.Color.A, precision: 5);
        Assert.Equal(8.0f, second.BaseSize, precision: 5);
        Assert.True(second.EnablePressure);

        // Bounds 等价：由 Codec 单点重算（未启用压感 → BaseSize/2 外扩）。
        Assert.True(first.HasBounds);
        Assert.Equal(-3.0f, first.BoundsMin.X, precision: 5);
        Assert.Equal(-3.0f, first.BoundsMin.Y, precision: 5);
        Assert.Equal(13.0f, first.BoundsMax.X, precision: 5);
        Assert.Equal(3.0f, first.BoundsMax.Y, precision: 5);

        Assert.True(second.HasBounds);
        // pressure=0.5、BaseSize=8 → widthFactor=0.5 → padding=2。
        Assert.Equal(98.0f, second.BoundsMin.X, precision: 5);
        Assert.Equal(112.0f, second.BoundsMax.X, precision: 5);
    }

    [Fact]
    public void CreatePages_EmptySnapshot_CreatesDefaultPage()
    {
        // 空工作区兜底：至少返回一个默认页（与原行为一致）。
        var snapshot = new BoardWorkspaceSnapshot([], CurrentIndex: 0);

        List<BoardPage> pages = BoardWorkspaceSnapshotApplier.CreatePages(snapshot);

        BoardPage page = Assert.Single(pages);
        Assert.Empty(page.Session.Document.InkItems);
    }
}
