using System;
using System.Numerics;
using WindBoard.Board.Items;
using Vortice.Mathematics;

namespace WindBoard.Tests.Board.Editing;

/// <summary>
/// 测试用最小 <see cref="IBoardInkItem"/> 实现（非折线笔迹的替身）。
/// </summary>
/// <remarks>
/// 用途：验证命中/擦除/Bounds 辅助方法对“非 Stroke 条目”走通用分发路径，
/// 不使用 mock 框架，直接构造真实对象。
/// </remarks>
internal sealed class TestInkItem : IBoardInkItem
{
    public TestInkItem(Rect boundsWorld)
    {
        BoundsWorld = boundsWorld;
    }

    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>世界坐标包围盒（测试替身直接以矩形为几何形态）。</summary>
    public Rect BoundsWorld { get; private set; }

    /// <summary>记录最近一次平移增量，便于断言接口契约被正确调用。</summary>
    public Vector2 LastTranslateDelta { get; private set; }

    public void Translate(Vector2 delta)
    {
        LastTranslateDelta = delta;

        Rect b = BoundsWorld;
        if (b.Width <= 0.0f && b.Height <= 0.0f)
        {
            return;
        }

        BoundsWorld = Rect.FromLTRB(
            b.Left + delta.X,
            b.Top + delta.Y,
            b.Right + delta.X,
            b.Bottom + delta.Y);
    }
}
