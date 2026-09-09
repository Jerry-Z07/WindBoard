using System;
using System.Collections.Generic;
using System.Numerics;
using WindBoard.Board.Elements;
using WindBoard.Board.Items;
using Vortice.Mathematics;

namespace WindBoard.Board
{
    internal sealed class BoardDocument
    {
        /// <summary>
        /// 笔迹层条目集合（折线笔迹与未来的几何形状按创建顺序交错叠放）。
        /// </summary>
        /// <remarks>
        /// 阶段一统一抽象（R1）：通过 <see cref="IBoardInkItem"/> 承载不同绘制条目类型，
        /// 渲染/命中/擦除按条目类型单点分发。
        /// </remarks>
        public List<IBoardInkItem> InkItems { get; } = new();

        /// <summary>
        /// 页面元素（默认层）：位于笔迹下方绘制。
        /// </summary>
        public List<BoardElement> ElementsBelowInk { get; } = new();

        /// <summary>
        /// 页面元素（置顶层）：位于笔迹上方绘制。
        /// </summary>
        public List<BoardElement> ElementsAboveInk { get; } = new();
    }

    /// <summary>
    /// 折线笔迹（点集 + 颜色/粗细/压感）。
    /// </summary>
    /// <remarks>
    /// 实现 <see cref="IBoardInkItem"/>：作为笔迹层条目接入文档集合与统一分发点，
    /// 折线语义（<see cref="Points"/> 点集）保持不变。
    /// </remarks>
    internal sealed class Stroke : IBoardInkItem
    {
        public List<StrokePoint> Points { get; } = new();

        /// <summary>
        /// 笔迹唯一标识（创建时生成，仅用于内存定位，不参与持久化）。
        /// </summary>
        public Guid Id { get; } = Guid.NewGuid();

        public Color4 Color { get; init; } = new(0, 0, 0, 1);

        public float BaseSize { get; init; } = 3.0f;

        public bool EnablePressure { get; init; } = true;

        public Vector2 BoundsMin { get; private set; } = new(float.PositiveInfinity, float.PositiveInfinity);

        public Vector2 BoundsMax { get; private set; } = new(float.NegativeInfinity, float.NegativeInfinity);

        public bool HasBounds => BoundsMin.X <= BoundsMax.X && BoundsMin.Y <= BoundsMax.Y;

        /// <summary>
        /// 世界坐标包围盒（<see cref="IBoardInkItem"/> 契约实现）。
        /// </summary>
        /// <remarks>
        /// 无有效 Bounds 时返回空矩形（宽高均为 0），与 <see cref="HasBounds"/> 的判定保持一致。
        /// </remarks>
        public Rect BoundsWorld => HasBounds
            ? Rect.FromLTRB(BoundsMin.X, BoundsMin.Y, BoundsMax.X, BoundsMax.Y)
            : Rect.Empty;

        internal void ExpandBounds(Vector2 position, float normalizedPressure)
        {
            float widthFactor = EnablePressure
                ? Math.Clamp(normalizedPressure, 0.1f, 1.0f)
                : 1.0f;

            float halfWidth = Math.Max(0.25f, BaseSize * widthFactor / 2.0f);
            ExpandBoundsWithPadding(position, halfWidth);
        }

        /// <summary>
        /// 重新计算包围盒（基于当前 Points）。
        /// </summary>
        /// <remarks>
        /// 用途：选择变换（平移/缩放/旋转）、导入后重建 Bounds 等。
        /// </remarks>
        internal void RecalculateBoundsFromPoints()
        {
            BoundsMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            BoundsMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

            for (int i = 0; i < Points.Count; i++)
            {
                StrokePoint p = Points[i];
                ExpandBounds(p.Position, p.Pressure);
            }
        }

        /// <summary>
        /// 平移笔迹（会同步更新 Points 与 Bounds）。
        /// </summary>
        /// <remarks>
        /// 可访问性为 public 以满足 <see cref="IBoardInkItem.Translate"/> 的接口实现要求。
        /// </remarks>
        public void Translate(Vector2 deltaWorld)
        {
            if (deltaWorld.LengthSquared() <= 0.0000001f)
            {
                return;
            }

            for (int i = 0; i < Points.Count; i++)
            {
                StrokePoint p = Points[i];
                Points[i] = new StrokePoint(p.Position + deltaWorld, p.Pressure);
            }

            if (HasBounds)
            {
                BoundsMin += deltaWorld;
                BoundsMax += deltaWorld;
            }
        }

        /// <summary>
        /// 对笔迹 Points 应用 2D 变换矩阵，并重建包围盒。
        /// </summary>
        internal void Transform(Matrix3x2 transform)
        {
            if (Points.Count == 0)
            {
                return;
            }

            for (int i = 0; i < Points.Count; i++)
            {
                StrokePoint p = Points[i];
                Points[i] = new StrokePoint(Vector2.Transform(p.Position, transform), p.Pressure);
            }

            RecalculateBoundsFromPoints();
        }

        private void ExpandBoundsWithPadding(Vector2 position, float padding)
        {
            Vector2 min = position - new Vector2(padding, padding);
            Vector2 max = position + new Vector2(padding, padding);

            if (!HasBounds)
            {
                BoundsMin = min;
                BoundsMax = max;
                return;
            }

            BoundsMin = new Vector2(
                Math.Min(BoundsMin.X, min.X),
                Math.Min(BoundsMin.Y, min.Y));

            BoundsMax = new Vector2(
                Math.Max(BoundsMax.X, max.X),
                Math.Max(BoundsMax.Y, max.Y));
        }
    }

    internal readonly record struct StrokePoint(Vector2 Position, float Pressure);
}
