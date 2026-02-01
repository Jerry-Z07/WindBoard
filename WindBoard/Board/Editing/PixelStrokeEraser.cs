using System;
using System.Collections.Generic;
using System.Numerics;

namespace WindBoard.Board.Editing
{
    /// <summary>
    /// 像素级（局部）擦除：
    /// - 把笔迹视为“带宽度的折线”，橡皮擦输入为“线段 + 半径”（世界坐标）。
    /// - 通过对命中的笔迹做采样 + 二分求边界，把被擦到的部分裁掉，并生成若干段新的笔迹。
    ///
    /// 说明：
    /// - 这里的“像素级”并非对最终像素缓冲做 mask，而是对笔迹几何数据进行分段裁剪；
    ///   对用户视觉效果而言，它表现为“可擦掉笔迹局部”的橡皮擦。
    /// - 为了保证撤销/重做与快照机制正确工作，本实现不会原地修改现有 Stroke 对象，
    ///   只会通过“删除/替换/分割”生成新的 Stroke 实例。
    /// </summary>
    internal sealed class PixelStrokeEraser : IBoardEraser
    {
        private readonly struct EraserCapsule
        {
            public Vector2 FromWorld { get; }
            public Vector2 ToWorld { get; }
            public Vector2 RadiusWorld { get; }

            public EraserCapsule(Vector2 fromWorld, Vector2 toWorld, Vector2 radiusWorld)
            {
                FromWorld = fromWorld;
                ToWorld = toWorld;
                RadiusWorld = radiusWorld;
            }
        }

        // 采样步长：只在“靠近橡皮擦轨迹”的线段上启用，避免无谓放大点数。
        private const float NearSegmentSampleStepWorld = 0.6f;

        // 每段线段最多采样次数，防止导入/异常数据导致单段过长时生成海量点。
        private const int MaxSamplesPerSegment = 256;

        // 二分边界迭代次数：在交互场景下 10 次足以达到亚像素级稳定。
        private const int BoundarySearchIterations = 10;

        public bool Erase(BoardDocument document, Vector2 fromWorld, Vector2 toWorld, Vector2 radiusWorld)
        {
            if (document.Strokes.Count == 0)
            {
                return false;
            }

            bool changed = false;
            var rebuilt = new List<Stroke>(document.Strokes.Count);

            foreach (Stroke stroke in document.Strokes)
            {
                // 先用现有命中测试做快速过滤，避免对所有笔迹都做采样裁剪。
                if (!StrokeHitTest.IsStrokeHitByEraserSegment(stroke, fromWorld, toWorld, radiusWorld))
                {
                    rebuilt.Add(stroke);
                    continue;
                }

                List<Stroke> keptSegments = EraseSingleStroke(stroke, fromWorld, toWorld, radiusWorld);
                if (keptSegments.Count == 1 && ReferenceEquals(keptSegments[0], stroke))
                {
                    rebuilt.Add(stroke);
                    continue;
                }

                changed = true;
                rebuilt.AddRange(keptSegments);
            }

            if (!changed)
            {
                return false;
            }

            document.Strokes.Clear();
            document.Strokes.AddRange(rebuilt);
            return true;
        }

        private static List<Stroke> EraseSingleStroke(Stroke stroke, Vector2 eraserFromWorld, Vector2 eraserToWorld, Vector2 eraserRadiusWorld)
        {
            int pointCount = stroke.Points.Count;
            if (pointCount == 0)
            {
                return new List<Stroke> { stroke };
            }

            var eraser = new EraserCapsule(eraserFromWorld, eraserToWorld, eraserRadiusWorld);

            // 单点笔迹：按点是否落入橡皮擦“胶囊体”阈值判断删除/保留。
            if (pointCount == 1)
            {
                return IsPointErased(stroke, stroke.Points[0], eraser)
                    ? new List<Stroke>()
                    : new List<Stroke> { stroke };
            }

            float maxHalfWidth = GetMaxHalfStrokeWidthWorld(stroke);
            Vector2 maxRadius = new(
                Math.Max(0.0f, eraserRadiusWorld.X) + maxHalfWidth,
                Math.Max(0.0f, eraserRadiusWorld.Y) + maxHalfWidth);

            Vector2 maxInv = new(
                1.0f / Math.Max(0.0000001f, maxRadius.X),
                1.0f / Math.Max(0.0000001f, maxRadius.Y));

            var result = new List<Stroke>();
            Stroke? current = null;

            // removedAny 用于判断“最终是否真的擦掉了东西”，避免误生成新 Stroke。
            bool removedAny = false;

            StrokePoint prevSample = stroke.Points[0];
            bool prevKeep = !IsPointErased(stroke, prevSample, eraser);
            if (prevKeep)
            {
                current = CreateDerivedStroke(stroke);
                AddPointWithBounds(current, prevSample);
            }
            else
            {
                removedAny = true;
            }

            // 逐段处理折线：仅对“可能被擦到”的线段做采样，以平衡性能与精度。
            for (int i = 1; i < pointCount; i++)
            {
                StrokePoint a = stroke.Points[i - 1];
                StrokePoint b = stroke.Points[i];

                float d2 = SegmentMath2D.DistanceSquaredSegmentToSegment(
                    new Vector2(eraserFromWorld.X * maxInv.X, eraserFromWorld.Y * maxInv.Y),
                    new Vector2(eraserToWorld.X * maxInv.X, eraserToWorld.Y * maxInv.Y),
                    new Vector2(a.Position.X * maxInv.X, a.Position.Y * maxInv.Y),
                    new Vector2(b.Position.X * maxInv.X, b.Position.Y * maxInv.Y));

                bool isNear = d2 <= 1.0f;

                int samples = 1;
                if (isNear)
                {
                    float len = Vector2.Distance(a.Position, b.Position);
                    samples = (int)MathF.Ceiling(len / Math.Max(0.0001f, NearSegmentSampleStepWorld));
                    samples = Math.Max(2, samples); // 至少补一个中点，避免“端点都在外侧但中间穿过”的漏擦。
                    samples = Math.Min(MaxSamplesPerSegment, samples);
                }

                // 注意：跳过 t=0，避免与上一段的末点重复。
                for (int s = 1; s <= samples; s++)
                {
                    float t = s / (float)samples;
                    StrokePoint sample = Lerp(a, b, t);

                    bool keep = !IsPointErased(stroke, sample, eraser);
                    if (!keep)
                    {
                        removedAny = true;
                    }

                    if (keep == prevKeep)
                    {
                        if (keep && current is not null)
                        {
                            AddPointWithBounds(current, sample);
                        }
                    }
                    else
                    {
                        // 在“保留 ↔ 擦除”的交界处插入边界点，让裁剪更稳定（避免硬断点造成明显缺口）。
                        StrokePoint boundary = FindBoundaryOutside(
                            stroke,
                            prevSample,
                            sample,
                            keepAtStart: prevKeep,
                            eraser);

                        if (prevKeep)
                        {
                            // 保留 -> 擦除：先补一个“最后的保留边界点”，然后结束当前段。
                            if (current is not null)
                            {
                                AddPointWithBounds(current, boundary);
                                CommitCurrentIfNotEmpty(ref current, result);
                            }
                        }
                        else
                        {
                            // 擦除 -> 保留：从“第一个保留边界点”开始新段，再接上当前采样点。
                            current ??= CreateDerivedStroke(stroke);
                            AddPointWithBounds(current, boundary);
                            AddPointWithBounds(current, sample);
                        }
                    }

                    prevSample = sample;
                    prevKeep = keep;
                }
            }

            CommitCurrentIfNotEmpty(ref current, result);

            // 如果没有真正擦到任何点，就直接复用原 Stroke，避免无意义的“替换”。
            if (!removedAny)
            {
                return new List<Stroke> { stroke };
            }

            return result;
        }

        private static void CommitCurrentIfNotEmpty(ref Stroke? current, List<Stroke> result)
        {
            if (current is null)
            {
                return;
            }

            if (current.Points.Count > 0)
            {
                result.Add(current);
            }

            current = null;
        }

        private static Stroke CreateDerivedStroke(Stroke source)
        {
            return new Stroke
            {
                Color = source.Color,
                BaseSize = source.BaseSize,
                EnablePressure = source.EnablePressure,
            };
        }

        private static void AddPointWithBounds(Stroke stroke, StrokePoint point)
        {
            // 避免重复点：采样/二分边界很容易生成同一位置的相邻点。
            if (stroke.Points.Count > 0)
            {
                StrokePoint last = stroke.Points[^1];
                if (Vector2.DistanceSquared(last.Position, point.Position) <= 0.0000001f)
                {
                    return;
                }
            }

            stroke.Points.Add(point);
            stroke.ExpandBounds(point.Position, point.Pressure);
        }

        private static StrokePoint FindBoundaryOutside(
            Stroke stroke,
            StrokePoint start,
            StrokePoint end,
            bool keepAtStart,
            in EraserCapsule eraser)
        {
            // 这里用“二分”在两采样点之间求一个更接近边界的点。
            // 目标：返回一个“仍然处于保留侧”的点，避免把边界点算进擦除区导致可见的断裂。
            float low = 0.0f;
            float high = 1.0f;

            for (int i = 0; i < BoundarySearchIterations; i++)
            {
                float mid = (low + high) / 2.0f;
                StrokePoint p = Lerp(start, end, mid);
                bool keep = !IsPointErased(stroke, p, eraser);

                if (keepAtStart)
                {
                    // 保留 -> 擦除：low 始终指向“最后一个保留点”
                    if (keep)
                    {
                        low = mid;
                    }
                    else
                    {
                        high = mid;
                    }
                }
                else
                {
                    // 擦除 -> 保留：high 始终指向“第一个保留点”
                    if (keep)
                    {
                        high = mid;
                    }
                    else
                    {
                        low = mid;
                    }
                }
            }

            float t = keepAtStart ? low : high;
            return Lerp(start, end, t);
        }

        private static StrokePoint Lerp(StrokePoint a, StrokePoint b, float t)
        {
            return new StrokePoint(
                Vector2.Lerp(a.Position, b.Position, t),
                a.Pressure + (b.Pressure - a.Pressure) * t);
        }

        private static bool IsPointErased(Stroke stroke, StrokePoint point, in EraserCapsule eraser)
        {
            float halfWidth = GetHalfStrokeWidthWorld(stroke, point.Pressure);
            Vector2 r = new(
                Math.Max(0.0f, eraser.RadiusWorld.X) + halfWidth,
                Math.Max(0.0f, eraser.RadiusWorld.Y) + halfWidth);

            Vector2 inv = new(
                1.0f / Math.Max(0.0000001f, r.X),
                1.0f / Math.Max(0.0000001f, r.Y));

            float d2 = SegmentMath2D.DistanceSquaredPointToSegment(
                new Vector2(point.Position.X * inv.X, point.Position.Y * inv.Y),
                new Vector2(eraser.FromWorld.X * inv.X, eraser.FromWorld.Y * inv.Y),
                new Vector2(eraser.ToWorld.X * inv.X, eraser.ToWorld.Y * inv.Y));

            return d2 <= 1.0f;
        }

        private static float GetHalfStrokeWidthWorld(Stroke stroke, float normalizedPressure)
        {
            float widthFactor = stroke.EnablePressure
                ? Math.Clamp(normalizedPressure, 0.1f, 1.0f)
                : 1.0f;

            // 与渲染/Bounds 逻辑保持一致：BaseSize 是直径，且最小半径不小于 0.25。
            return Math.Max(0.25f, stroke.BaseSize * widthFactor / 2.0f);
        }

        private static float GetMaxHalfStrokeWidthWorld(Stroke stroke)
        {
            // 采样阶段需要一个“上界半径”用于快速判断某线段是否可能被擦到。
            // 对于启用压力的笔迹，最大宽度出现在压力=1.0；禁用压力时恒定。
            return Math.Max(0.25f, stroke.BaseSize / 2.0f);
        }
    }
}
