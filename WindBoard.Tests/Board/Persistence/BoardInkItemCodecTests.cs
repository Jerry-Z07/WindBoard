using System;
using System.Collections.Generic;
using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Items;
using WindBoard.Board.Persistence;
using WindBoard.Tests.Board.Editing;
using Vortice.Mathematics;
using Xunit;

namespace WindBoard.Tests.Board.Persistence;

public sealed class BoardInkItemCodecTests
{
    [Fact]
    public void ToSnapshot_FromDomainStroke_MapsAllFields()
    {
        var stroke = new Stroke
        {
            Color = new Color4(0.1f, 0.2f, 0.3f, 0.9f),
            BaseSize = 4.5f,
            EnablePressure = true,
        };
        stroke.Points.Add(new StrokePoint(new Vector2(1.0f, 2.0f), 0.25f));
        stroke.Points.Add(new StrokePoint(new Vector2(3.0f, 4.0f), 0.75f));

        InkItemSnapshot? snapshot = BoardInkItemCodec.ToSnapshot(stroke);

        Assert.NotNull(snapshot);
        Assert.Equal(BoardInkItemCodec.StrokeKind, snapshot!.Kind);
        Assert.NotNull(snapshot.Stroke);

        StrokeSnapshot data = snapshot.Stroke!;
        Assert.True(data.EnablePressure);
        Assert.Equal(4.5f, data.BaseSize, precision: 5);
        Assert.Equal(0.1f, data.ColorRgba.X, precision: 5);
        Assert.Equal(0.2f, data.ColorRgba.Y, precision: 5);
        Assert.Equal(0.3f, data.ColorRgba.Z, precision: 5);
        Assert.Equal(0.9f, data.ColorRgba.W, precision: 5);
        Assert.Equal(2, data.Points.Count);
        Assert.Equal(1.0f, data.Points[0].Position.X, precision: 5);
        Assert.Equal(0.25f, data.Points[0].Pressure, precision: 5);
        Assert.Equal(3.0f, data.Points[1].Position.X, precision: 5);
        Assert.Equal(0.75f, data.Points[1].Pressure, precision: 5);
    }

    [Fact]
    public void ToSnapshot_NonStrokeItem_ReturnsNull()
    {
        // 快照格式当前仅承载折线笔迹：非 Stroke 条目返回 null，由调用方跳过（与原 Converter 行为一致）。
        var item = new TestInkItem(Vortice.Mathematics.Rect.Empty);

        Assert.Null(BoardInkItemCodec.ToSnapshot(item));
    }

    [Fact]
    public void ToSnapshotList_MixedItems_SkipsNonStrokeAndKeepsOrder()
    {
        var stroke0 = CreateStroke(new Vector2(0, 0), new Vector2(10, 0));
        var stroke2 = CreateStroke(new Vector2(0, 5), new Vector2(10, 5));
        List<IBoardInkItem> items = [stroke0, new TestInkItem(Vortice.Mathematics.Rect.Empty), stroke2];

        List<InkItemSnapshot> snapshots = BoardInkItemCodec.ToSnapshotList(items);

        Assert.Equal(2, snapshots.Count);
        Assert.Equal(stroke0.Points[0].Position.X, snapshots[0].Stroke!.Points[0].Position.X, precision: 5);
        Assert.Equal(stroke2.Points[0].Position.X, snapshots[1].Stroke!.Points[0].Position.X, precision: 5);
    }

    [Fact]
    public void ToItem_ExplicitNullKind_FallsBackToStroke()
    {
        // 显式 null 防御：Kind 为 null 时不依赖 C# 初始化器缺省值，直接归一为 stroke。
        var snapshot = new InkItemSnapshot
        {
            Kind = null!,
            Stroke = CreateStrokeData(enablePressure: true),
        };

        IBoardInkItem? item = BoardInkItemCodec.ToItem(snapshot);

        Assert.IsType<Stroke>(item);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ToItem_EmptyKind_FallsBackToStroke(string kind)
    {
        var snapshot = new InkItemSnapshot
        {
            Kind = kind,
            Stroke = CreateStrokeData(enablePressure: false),
        };

        Assert.IsType<Stroke>(BoardInkItemCodec.ToItem(snapshot));
    }

    [Fact]
    public void ToItem_KindIsCaseInsensitive()
    {
        var snapshot = new InkItemSnapshot
        {
            Kind = "STROKE",
            Stroke = CreateStrokeData(enablePressure: false),
        };

        Assert.IsType<Stroke>(BoardInkItemCodec.ToItem(snapshot));
    }

    [Fact]
    public void ToItem_UnknownKind_ReturnsNull()
    {
        // 未知类型：跳过（Warn 日志）而不是抛异常，避免单条数据阻断整个导入流程。
        var snapshot = new InkItemSnapshot
        {
            Kind = "shape",
            Stroke = CreateStrokeData(enablePressure: false),
        };

        Assert.Null(BoardInkItemCodec.ToItem(snapshot));
    }

    [Fact]
    public void ToItemList_PreservesOrderAsZOrder()
    {
        var a = new InkItemSnapshot { Stroke = CreateStrokeData(enablePressure: false, x0: 0.0f) };
        var b = new InkItemSnapshot { Stroke = CreateStrokeData(enablePressure: false, x0: 100.0f) };
        var c = new InkItemSnapshot { Stroke = CreateStrokeData(enablePressure: false, x0: 200.0f) };

        List<IBoardInkItem> items = BoardInkItemCodec.ToItemList([a, b, c]);

        Assert.Equal(3, items.Count);
        var s0 = Assert.IsType<Stroke>(items[0]);
        var s1 = Assert.IsType<Stroke>(items[1]);
        var s2 = Assert.IsType<Stroke>(items[2]);
        Assert.Equal(0.0f, s0.Points[0].Position.X, precision: 5);
        Assert.Equal(100.0f, s1.Points[0].Position.X, precision: 5);
        Assert.Equal(200.0f, s2.Points[0].Position.X, precision: 5);
    }

    [Fact]
    public void ToItemList_NullOrEmpty_ReturnsEmptyList()
    {
        Assert.Empty(BoardInkItemCodec.ToItemList(null));
        Assert.Empty(BoardInkItemCodec.ToItemList(Array.Empty<InkItemSnapshot>()));
    }

    [Fact]
    public void ToStrokeItem_WithoutPressure_BoundsReflectBaseSize()
    {
        // Bounds 重算语义（原三处实现一致）：未启用压感时，包围盒按 BaseSize/2 外扩。
        // 点 (0,0) 与 (10,0)，BaseSize=6 → padding=3 → Bounds=(-3,-3)~(13,3)。
        var snapshot = new StrokeSnapshot(
            Points:
            [
                new StrokePointSnapshot(new Vector2(0.0f, 0.0f), 1.0f),
                new StrokePointSnapshot(new Vector2(10.0f, 0.0f), 1.0f),
            ],
            ColorRgba: new Vector4(0, 0, 0, 1),
            BaseSize: 6.0f,
            EnablePressure: false);

        Stroke stroke = BoardInkItemCodec.ToStrokeItem(snapshot);

        Assert.True(stroke.HasBounds);
        Assert.Equal(-3.0f, stroke.BoundsMin.X, precision: 5);
        Assert.Equal(-3.0f, stroke.BoundsMin.Y, precision: 5);
        Assert.Equal(13.0f, stroke.BoundsMax.X, precision: 5);
        Assert.Equal(3.0f, stroke.BoundsMax.Y, precision: 5);
    }

    [Fact]
    public void ToStrokeItem_WithPressure_BoundsReflectPressure()
    {
        // 启用压感时：widthFactor=clamp(pressure,0.1,1)；pressure=0.5、BaseSize=6 → padding=1.5。
        var snapshot = new StrokeSnapshot(
            Points:
            [
                new StrokePointSnapshot(new Vector2(0.0f, 0.0f), 0.5f),
                new StrokePointSnapshot(new Vector2(10.0f, 0.0f), 0.5f),
            ],
            ColorRgba: new Vector4(0, 0, 0, 1),
            BaseSize: 6.0f,
            EnablePressure: true);

        Stroke stroke = BoardInkItemCodec.ToStrokeItem(snapshot);

        Assert.True(stroke.HasBounds);
        Assert.Equal(-1.5f, stroke.BoundsMin.X, precision: 5);
        Assert.Equal(-1.5f, stroke.BoundsMin.Y, precision: 5);
        Assert.Equal(11.5f, stroke.BoundsMax.X, precision: 5);
        Assert.Equal(1.5f, stroke.BoundsMax.Y, precision: 5);
    }

    [Fact]
    public void ToSnapshot_FromDomainShape_MapsAllFields()
    {
        var shape = new BoardShape(BoardShapeKind.Arrow)
        {
            Color = new Color4(0.3f, 0.6f, 0.9f, 0.8f),
            Width = 7.5f,
        };
        shape.SetGeometry(new Vector2(1.0f, -2.0f), new Vector2(30.0f, 40.0f));

        InkItemSnapshot? snapshot = BoardInkItemCodec.ToSnapshot(shape);

        Assert.NotNull(snapshot);
        Assert.Equal(BoardInkItemCodec.ArrowKind, snapshot!.Kind);
        Assert.NotNull(snapshot.Shape);

        ShapeSnapshot data = snapshot.Shape!;
        AssertEx.Equal(1.0f, data.Start.X);
        AssertEx.Equal(-2.0f, data.Start.Y);
        AssertEx.Equal(30.0f, data.End.X);
        AssertEx.Equal(40.0f, data.End.Y);
        Assert.Equal(0.3f, data.ColorRgba.X, precision: 5);
        Assert.Equal(0.6f, data.ColorRgba.Y, precision: 5);
        Assert.Equal(0.9f, data.ColorRgba.Z, precision: 5);
        Assert.Equal(0.8f, data.ColorRgba.W, precision: 5);
        Assert.Equal(7.5f, data.Width, precision: 5);
    }

    [Fact]
    public void ToSnapshotList_MixedStrokeAndShape_KeepsOrder()
    {
        var stroke = CreateStroke(new Vector2(0, 0), new Vector2(10, 0));
        var shape = new BoardShape(BoardShapeKind.Rectangle);
        shape.SetGeometry(new Vector2(0, 5), new Vector2(10, 5));

        List<InkItemSnapshot> snapshots = BoardInkItemCodec.ToSnapshotList([stroke, new TestInkItem(Vortice.Mathematics.Rect.Empty), shape]);

        Assert.Equal(2, snapshots.Count);
        Assert.Equal(BoardInkItemCodec.StrokeKind, snapshots[0].Kind);
        Assert.Equal(BoardInkItemCodec.RectKind, snapshots[1].Kind);
    }

    [Theory]
    [InlineData(BoardInkItemCodec.LineKind, nameof(BoardShapeKind.Line))]
    [InlineData(BoardInkItemCodec.RectKind, nameof(BoardShapeKind.Rectangle))]
    [InlineData(BoardInkItemCodec.EllipseKind, nameof(BoardShapeKind.Ellipse))]
    [InlineData(BoardInkItemCodec.ArrowKind, nameof(BoardShapeKind.Arrow))]
    public void ToItem_ShapeKinds_BuildShapesWithBounds(string kindText, string expectedKindName)
    {
        var snapshot = new InkItemSnapshot
        {
            Kind = kindText,
            Shape = new ShapeSnapshot(
                new Vector2(0.0f, 0.0f),
                new Vector2(20.0f, 10.0f),
                new Vector4(1.0f, 0.5f, 0.0f, 1.0f),
                6.0f),
        };

        var shape = Assert.IsType<BoardShape>(BoardInkItemCodec.ToItem(snapshot));

        Assert.Equal(expectedKindName, shape.Kind.ToString());
        Assert.Equal(6.0f, shape.Width, precision: 5);
        Assert.Equal(1.0f, shape.Color.R, precision: 5);
        // Bounds 由 SetGeometry 单点重算：AABB(0,0)-(20,10) 外扩 3 → (-3,-3)-(23,13)。
        Rect bounds = shape.BoundsWorld;
        AssertEx.Equal(-3.0f, bounds.Left);
        AssertEx.Equal(-3.0f, bounds.Top);
        AssertEx.Equal(23.0f, bounds.Right);
        AssertEx.Equal(13.0f, bounds.Bottom);
    }

    [Fact]
    public void ToItem_ShapeKindWithoutShapeData_Throws()
    {
        // 与 Stroke 约定一致：kind 与数据载荷不匹配属于损坏数据，fail-fast 而非静默丢弃。
        var snapshot = new InkItemSnapshot
        {
            Kind = BoardInkItemCodec.LineKind,
            Shape = null,
        };

        Assert.Throws<ArgumentException>(() => BoardInkItemCodec.ToItem(snapshot));
    }

    [Fact]
    public void ToItem_ShapeKindCaseInsensitive()
    {
        var snapshot = new InkItemSnapshot
        {
            Kind = "LINE",
            Shape = new ShapeSnapshot(
                new Vector2(0.0f, 0.0f),
                new Vector2(1.0f, 1.0f),
                new Vector4(0, 0, 0, 1),
                3.0f),
        };

        var shape = Assert.IsType<BoardShape>(BoardInkItemCodec.ToItem(snapshot));
        Assert.Equal(BoardShapeKind.Line, shape.Kind);
    }

    [Fact]
    public void RoundTrip_DomainShapeToSnapshotToDomain_KeepsValues()
    {
        var original = new BoardShape(BoardShapeKind.Ellipse)
        {
            Color = new Color4(0.4f, 0.5f, 0.6f, 0.7f),
            Width = 9.0f,
        };
        original.SetGeometry(new Vector2(-10.0f, 2.5f), new Vector2(60.0f, 80.0f));

        InkItemSnapshot snapshot = BoardInkItemCodec.ToSnapshot(original)!;
        var restored = Assert.IsType<BoardShape>(BoardInkItemCodec.ToItem(snapshot));

        Assert.Equal(original.Kind, restored.Kind);
        AssertEx.Equal(original.Start, restored.Start);
        AssertEx.Equal(original.End, restored.End);
        Assert.Equal(original.Width, restored.Width, precision: 5);
        Assert.Equal(original.Color.R, restored.Color.R, precision: 5);
        Assert.Equal(original.Color.G, restored.Color.G, precision: 5);
        Assert.Equal(original.Color.B, restored.Color.B, precision: 5);
        Assert.Equal(original.Color.A, restored.Color.A, precision: 5);

        // Bounds 经重算后一致。
        AssertEx.Equal(original.BoundsWorld.Left, restored.BoundsWorld.Left);
        AssertEx.Equal(original.BoundsWorld.Top, restored.BoundsWorld.Top);
        AssertEx.Equal(original.BoundsWorld.Right, restored.BoundsWorld.Right);
        AssertEx.Equal(original.BoundsWorld.Bottom, restored.BoundsWorld.Bottom);
    }

    [Fact]
    public void RoundTrip_DomainToSnapshotToDomain_KeepsValues()
    {
        var original = new Stroke
        {
            Color = new Color4(0.2f, 0.4f, 0.6f, 0.8f),
            BaseSize = 5.5f,
            EnablePressure = true,
        };
        original.Points.Add(new StrokePoint(new Vector2(-2.0f, 3.5f), 0.4f));
        original.Points.Add(new StrokePoint(new Vector2(8.0f, -1.25f), 0.9f));
        original.RecalculateBoundsFromPoints();

        InkItemSnapshot snapshot = BoardInkItemCodec.ToSnapshot(original)!;
        var restored = Assert.IsType<Stroke>(BoardInkItemCodec.ToItem(snapshot));

        Assert.Equal(original.EnablePressure, restored.EnablePressure);
        Assert.Equal(original.BaseSize, restored.BaseSize, precision: 5);
        Assert.Equal(original.Color.R, restored.Color.R, precision: 5);
        Assert.Equal(original.Color.G, restored.Color.G, precision: 5);
        Assert.Equal(original.Color.B, restored.Color.B, precision: 5);
        Assert.Equal(original.Color.A, restored.Color.A, precision: 5);
        Assert.Equal(original.Points.Count, restored.Points.Count);

        for (int i = 0; i < original.Points.Count; i++)
        {
            Assert.Equal(original.Points[i].Position.X, restored.Points[i].Position.X, precision: 5);
            Assert.Equal(original.Points[i].Position.Y, restored.Points[i].Position.Y, precision: 5);
            Assert.Equal(original.Points[i].Pressure, restored.Points[i].Pressure, precision: 5);
        }

        // Bounds 经重算后一致（重启/导入后 Bounds 不落盘，由点集重建）。
        Assert.Equal(original.BoundsMin.X, restored.BoundsMin.X, precision: 5);
        Assert.Equal(original.BoundsMin.Y, restored.BoundsMin.Y, precision: 5);
        Assert.Equal(original.BoundsMax.X, restored.BoundsMax.X, precision: 5);
        Assert.Equal(original.BoundsMax.Y, restored.BoundsMax.Y, precision: 5);
    }

    private static StrokeSnapshot CreateStrokeData(bool enablePressure, float x0 = 0.0f)
    {
        return new StrokeSnapshot(
            Points:
            [
                new StrokePointSnapshot(new Vector2(x0, 1.0f), 1.0f),
                new StrokePointSnapshot(new Vector2(x0 + 10.0f, 2.0f), 1.0f),
            ],
            ColorRgba: new Vector4(0.0f, 0.0f, 0.0f, 1.0f),
            BaseSize: 4.0f,
            EnablePressure: enablePressure);
    }

    private static Stroke CreateStroke(Vector2 p0, Vector2 p1)
    {
        var stroke = new Stroke
        {
            BaseSize = 6.0f,
            EnablePressure = false,
        };

        stroke.Points.Add(new StrokePoint(p0, 1.0f));
        stroke.Points.Add(new StrokePoint(p1, 1.0f));
        stroke.RecalculateBoundsFromPoints();

        return stroke;
    }
}
