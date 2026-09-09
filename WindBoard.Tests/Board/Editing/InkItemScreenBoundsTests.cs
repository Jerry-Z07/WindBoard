using System.Collections.Generic;
using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Editing;
using WindBoard.Board.Items;
using Vortice.Mathematics;
using Xunit;

namespace WindBoard.Tests.Board.Editing;

public sealed class InkItemScreenBoundsTests
{
    [Fact]
    public void TryGetInkItemsBoundsScreenDip_ReturnsFalse_WhenEmpty()
    {
        bool ok = InkItemScreenBounds.TryGetInkItemsBoundsScreenDip(
            items: new List<IBoardInkItem>(),
            worldToScreen: Matrix3x2.Identity,
            out _);
        Assert.False(ok);
    }

    [Fact]
    public void TryGetInkItemsBoundsScreenDip_ReturnsFalse_WhenNoPoints()
    {
        var items = new List<IBoardInkItem>
        {
            new Stroke(),
            new Stroke(),
        };

        bool ok = InkItemScreenBounds.TryGetInkItemsBoundsScreenDip(items, Matrix3x2.Identity, out _);
        Assert.False(ok);
    }

    [Fact]
    public void TryGetInkItemsBoundsScreenDip_UnionsBounds_WhenMultipleStrokes()
    {
        // BaseSize=0 => halfWidth 最小为 0.25
        var s0 = new Stroke { BaseSize = 0.0f, EnablePressure = false };
        s0.Points.Add(new StrokePoint(new Vector2(0.0f, 0.0f), 1.0f));
        s0.ExpandBounds(new Vector2(0.0f, 0.0f), 1.0f);

        var s1 = new Stroke { BaseSize = 0.0f, EnablePressure = false };
        s1.Points.Add(new StrokePoint(new Vector2(10.0f, 5.0f), 1.0f));
        s1.ExpandBounds(new Vector2(10.0f, 5.0f), 1.0f);

        var items = new List<IBoardInkItem> { s0, s1 };

        bool ok = InkItemScreenBounds.TryGetInkItemsBoundsScreenDip(items, Matrix3x2.Identity, out Rect bounds);
        Assert.True(ok);

        AssertEx.Equal(-0.25f, bounds.Left);
        AssertEx.Equal(-0.25f, bounds.Top);
        AssertEx.Equal(10.25f, bounds.Right);
        AssertEx.Equal(5.25f, bounds.Bottom);
    }

    [Fact]
    public void TryGetInkItemsBoundsScreenDip_RecalculatesBounds_WhenMissing()
    {
        // 不调用 ExpandBounds / RecalculateBoundsFromPoints，让 Bounds 处于“未计算”状态，验证兜底逻辑。
        var stroke = new Stroke { BaseSize = 0.0f, EnablePressure = false };
        stroke.Points.Add(new StrokePoint(new Vector2(1.0f, 2.0f), 1.0f));

        bool ok = InkItemScreenBounds.TryGetInkItemsBoundsScreenDip(new IBoardInkItem[] { stroke }, Matrix3x2.Identity, out Rect bounds);
        Assert.True(ok);

        AssertEx.Equal(0.75f, bounds.Left);
        AssertEx.Equal(1.75f, bounds.Top);
        AssertEx.Equal(1.25f, bounds.Right);
        AssertEx.Equal(2.25f, bounds.Bottom);
    }

    // 非 Stroke 条目走通用路径：直接取接口契约的世界包围盒
    [Fact]
    public void TryGetInkItemsBoundsScreenDip_UsesBoundsWorld_ForNonStrokeItem()
    {
        var item = new TestInkItem(Rect.FromLTRB(2.0f, 3.0f, 12.0f, 8.0f));

        bool ok = InkItemScreenBounds.TryGetInkItemsBoundsScreenDip(new IBoardInkItem[] { item }, Matrix3x2.Identity, out Rect bounds);
        Assert.True(ok);

        AssertEx.Equal(2.0f, bounds.Left);
        AssertEx.Equal(3.0f, bounds.Top);
        AssertEx.Equal(12.0f, bounds.Right);
        AssertEx.Equal(8.0f, bounds.Bottom);
    }

    // 非 Stroke 条目：无有效包围盒（空矩形）时跳过，不影响其余条目的并集计算
    [Fact]
    public void TryGetInkItemsBoundsScreenDip_SkipsNonStrokeItem_WhenBoundsEmpty()
    {
        var empty = new TestInkItem(Rect.Empty);
        var stroke = new Stroke { BaseSize = 0.0f, EnablePressure = false };
        stroke.Points.Add(new StrokePoint(new Vector2(1.0f, 2.0f), 1.0f));
        stroke.ExpandBounds(new Vector2(1.0f, 2.0f), 1.0f);

        bool ok = InkItemScreenBounds.TryGetInkItemsBoundsScreenDip(new IBoardInkItem[] { empty, stroke }, Matrix3x2.Identity, out Rect bounds);
        Assert.True(ok);

        AssertEx.Equal(0.75f, bounds.Left);
        AssertEx.Equal(1.75f, bounds.Top);
        AssertEx.Equal(1.25f, bounds.Right);
        AssertEx.Equal(2.25f, bounds.Bottom);
    }
}
