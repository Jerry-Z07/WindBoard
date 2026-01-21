using System;
using System.Collections.Generic;
using WindBoard.Models.InkV2;

namespace WindBoard.Services.InkV2
{
    internal static class InkEraserEngine
    {
        private const double IntersectionEpsilon = 1e-9;
        private const double MinSegmentLengthSquared = 1e-12;

        public static bool EraseCircle(
            InkDocument document,
            InkSpatialIndex spatialIndex,
            InkUndoHistory undoHistory,
            double centerXDip,
            double centerYDip,
            double radiusDip)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (spatialIndex == null) throw new ArgumentNullException(nameof(spatialIndex));
            if (undoHistory == null) throw new ArgumentNullException(nameof(undoHistory));
            if (radiusDip <= 0) return false;

            var hits = spatialIndex.QueryCircle(centerXDip, centerYDip, radiusDip);
            if (hits.Count == 0) return false;

            var candidates = GroupCandidatesByStroke(hits);
            bool changed = false;

            foreach (var kv in candidates)
            {
                InkStroke stroke = kv.Key;
                if (!document.Strokes.Contains(stroke))
                {
                    continue;
                }

                bool strokeChanged = TryEraseCircleFromStroke(stroke, kv.Value, centerXDip, centerYDip, radiusDip, out var afterFragments);
                if (!strokeChanged)
                {
                    continue;
                }

                changed = true;
                ApplyStrokeFragmentsOrRemove(document, undoHistory, stroke, afterFragments);
            }

            if (changed)
            {
                spatialIndex.Rebuild(document);
            }

            return changed;
        }

        public static bool EraseRect(
            InkDocument document,
            InkSpatialIndex spatialIndex,
            InkUndoHistory undoHistory,
            InkRectDip rectDip)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (spatialIndex == null) throw new ArgumentNullException(nameof(spatialIndex));
            if (undoHistory == null) throw new ArgumentNullException(nameof(undoHistory));
            if (rectDip.Width <= 0 || rectDip.Height <= 0) return false;

            var hits = spatialIndex.QueryRect(rectDip);
            if (hits.Count == 0) return false;

            var candidates = GroupCandidatesByStroke(hits);
            bool changed = false;

            foreach (var kv in candidates)
            {
                InkStroke stroke = kv.Key;
                if (!document.Strokes.Contains(stroke))
                {
                    continue;
                }

                bool strokeChanged = TryEraseRectFromStroke(stroke, kv.Value, rectDip, out var afterFragments);
                if (!strokeChanged)
                {
                    continue;
                }

                changed = true;
                ApplyStrokeFragmentsOrRemove(document, undoHistory, stroke, afterFragments);
            }

            if (changed)
            {
                spatialIndex.Rebuild(document);
            }

            return changed;
        }

        private static Dictionary<InkStroke, HashSet<InkFragment>> GroupCandidatesByStroke(List<InkSegmentHit> hits)
        {
            var result = new Dictionary<InkStroke, HashSet<InkFragment>>();

            for (int i = 0; i < hits.Count; i++)
            {
                InkStroke stroke = hits[i].Stroke;
                InkFragment fragment = hits[i].Fragment;

                if (!result.TryGetValue(stroke, out var set))
                {
                    set = new HashSet<InkFragment>();
                    result.Add(stroke, set);
                }
                set.Add(fragment);
            }

            return result;
        }

        private static bool TryEraseCircleFromStroke(
            InkStroke stroke,
            HashSet<InkFragment> candidateFragments,
            double centerXDip,
            double centerYDip,
            double radiusDip,
            out List<InkFragment> afterFragments)
        {
            var beforeFragments = stroke.Fragments;
            afterFragments = new List<InkFragment>(beforeFragments.Count + 1);

            bool changed = false;
            for (int fi = 0; fi < beforeFragments.Count; fi++)
            {
                InkFragment fragment = beforeFragments[fi];
                if (!candidateFragments.Contains(fragment))
                {
                    afterFragments.Add(fragment);
                    continue;
                }

                if (!TryEraseCircleFromFragment(fragment, centerXDip, centerYDip, radiusDip, out var remaining))
                {
                    afterFragments.Add(fragment);
                    continue;
                }

                changed = true;
                for (int i = 0; i < remaining.Count; i++)
                {
                    afterFragments.Add(remaining[i]);
                }
            }

            if (!changed)
            {
                afterFragments.Clear();
            }

            return changed;
        }

        private static bool TryEraseRectFromStroke(
            InkStroke stroke,
            HashSet<InkFragment> candidateFragments,
            InkRectDip rect,
            out List<InkFragment> afterFragments)
        {
            var beforeFragments = stroke.Fragments;
            afterFragments = new List<InkFragment>(beforeFragments.Count + 1);

            bool changed = false;
            for (int fi = 0; fi < beforeFragments.Count; fi++)
            {
                InkFragment fragment = beforeFragments[fi];
                if (!candidateFragments.Contains(fragment))
                {
                    afterFragments.Add(fragment);
                    continue;
                }

                if (!TryEraseRectFromFragment(fragment, rect, out var remaining))
                {
                    afterFragments.Add(fragment);
                    continue;
                }

                changed = true;
                for (int i = 0; i < remaining.Count; i++)
                {
                    afterFragments.Add(remaining[i]);
                }
            }

            if (!changed)
            {
                afterFragments.Clear();
            }

            return changed;
        }

        private static void ApplyStrokeFragmentsOrRemove(
            InkDocument document,
            InkUndoHistory undoHistory,
            InkStroke stroke,
            List<InkFragment> afterFragments)
        {
            if (afterFragments.Count == 0)
            {
                int index = document.Strokes.IndexOf(stroke);
                if (index < 0) return;

                document.Strokes.RemoveAt(index);
                undoHistory.Record(new RemoveStrokeCommand(index, stroke));
                return;
            }

            var beforeFragments = new List<InkFragment>(stroke.Fragments);
            var afterCopy = new List<InkFragment>(afterFragments);

            stroke.Fragments.Clear();
            for (int i = 0; i < afterFragments.Count; i++)
            {
                stroke.Fragments.Add(afterFragments[i]);
            }

            undoHistory.Record(new ReplaceStrokeFragmentsCommand(stroke, beforeFragments, afterCopy));
        }

        private static bool TryEraseCircleFromFragment(
            InkFragment fragment,
            double centerXDip,
            double centerYDip,
            double radiusDip,
            out List<InkFragment> remainingFragments)
        {
            remainingFragments = new List<InkFragment>(2);

            List<InkPoint> points = fragment.Points;
            if (points.Count < 2)
            {
                return false;
            }

            double radiusSquared = radiusDip * radiusDip;
            bool anyErased = false;

            List<InkPoint>? current = null;

            for (int i = 0; i < points.Count - 1; i++)
            {
                InkPoint a = points[i];
                InkPoint b = points[i + 1];

                bool aInside = IsInsideCircle(a, centerXDip, centerYDip, radiusSquared);
                bool bInside = IsInsideCircle(b, centerXDip, centerYDip, radiusSquared);

                int intersectionCount = FindSegmentCircleIntersections(a, b, centerXDip, centerYDip, radiusDip, out double t0, out double t1);

                if (aInside && bInside)
                {
                    anyErased = true;
                    continue;
                }

                if (!aInside && !bInside)
                {
                    if (intersectionCount == 2 && (t1 - t0) > IntersectionEpsilon)
                    {
                        anyErased = true;

                        InkPoint pEnter = Lerp(a, b, t0);
                        InkPoint pExit = Lerp(a, b, t1);

                        EnsureStarted(ref current, a);
                        AddPoint(current!, pEnter);
                        Finalize(ref current, remainingFragments);

                        EnsureStarted(ref current, pExit);
                        AddPoint(current!, b);
                        continue;
                    }

                    EnsureStarted(ref current, a);
                    AddPoint(current!, b);
                    continue;
                }

                if (!aInside && bInside)
                {
                    if (intersectionCount == 0)
                    {
                        anyErased = true;
                        Finalize(ref current, remainingFragments);
                        continue;
                    }

                    anyErased = true;
                    double tEnter = intersectionCount == 2 ? t0 : t0;
                    InkPoint pEnter = Lerp(a, b, tEnter);

                    EnsureStarted(ref current, a);
                    AddPoint(current!, pEnter);
                    Finalize(ref current, remainingFragments);
                    continue;
                }

                if (aInside && !bInside)
                {
                    if (intersectionCount == 0)
                    {
                        anyErased = true;
                        EnsureStarted(ref current, b);
                        AddPoint(current!, b);
                        continue;
                    }

                    anyErased = true;
                    double tExit = intersectionCount == 2 ? t1 : t0;
                    InkPoint pExit = Lerp(a, b, tExit);

                    EnsureStarted(ref current, pExit);
                    AddPoint(current!, b);
                    continue;
                }
            }

            Finalize(ref current, remainingFragments);

            if (!anyErased)
            {
                remainingFragments.Clear();
                return false;
            }

            return true;
        }

        private static bool TryEraseRectFromFragment(
            InkFragment fragment,
            InkRectDip rect,
            out List<InkFragment> remainingFragments)
        {
            remainingFragments = new List<InkFragment>(2);

            List<InkPoint> points = fragment.Points;
            if (points.Count < 2)
            {
                return false;
            }

            bool anyErased = false;
            List<InkPoint>? current = null;

            double left = rect.Left;
            double right = rect.Right;
            double top = rect.Top;
            double bottom = rect.Bottom;

            for (int i = 0; i < points.Count - 1; i++)
            {
                InkPoint a = points[i];
                InkPoint b = points[i + 1];

                if (!TryClipSegmentToRect(a.XDip, a.YDip, b.XDip, b.YDip, left, top, right, bottom, out double tEnter, out double tExit))
                {
                    EnsureStarted(ref current, a);
                    AddPoint(current!, b);
                    continue;
                }

                if ((tExit - tEnter) <= IntersectionEpsilon)
                {
                    EnsureStarted(ref current, a);
                    AddPoint(current!, b);
                    continue;
                }

                anyErased = true;

                if (tEnter <= 0 && tExit >= 1)
                {
                    Finalize(ref current, remainingFragments);
                    continue;
                }

                if (tEnter > 0)
                {
                    InkPoint pEnter = Lerp(a, b, tEnter);
                    EnsureStarted(ref current, a);
                    AddPoint(current!, pEnter);
                    Finalize(ref current, remainingFragments);
                }
                else
                {
                    Finalize(ref current, remainingFragments);
                }

                if (tExit < 1)
                {
                    InkPoint pExit = Lerp(a, b, tExit);
                    EnsureStarted(ref current, pExit);
                    AddPoint(current!, b);
                }
            }

            Finalize(ref current, remainingFragments);

            if (!anyErased)
            {
                remainingFragments.Clear();
                return false;
            }

            return true;
        }

        private static bool IsInsideCircle(InkPoint p, double centerXDip, double centerYDip, double radiusSquaredDip)
        {
            double dx = p.XDip - centerXDip;
            double dy = p.YDip - centerYDip;
            return ((dx * dx) + (dy * dy)) <= radiusSquaredDip;
        }

        private static void EnsureStarted(ref List<InkPoint>? current, InkPoint start)
        {
            if (current != null) return;
            current = new List<InkPoint>(64) { start };
        }

        private static void AddPoint(List<InkPoint> list, InkPoint point)
        {
            if (list.Count > 0)
            {
                InkPoint last = list[list.Count - 1];
                if (Math.Abs(last.XDip - point.XDip) <= IntersectionEpsilon &&
                    Math.Abs(last.YDip - point.YDip) <= IntersectionEpsilon)
                {
                    return;
                }
            }
            list.Add(point);
        }

        private static void Finalize(ref List<InkPoint>? current, List<InkFragment> fragments)
        {
            if (current == null) return;

            if (current.Count >= 2)
            {
                var f = new InkFragment();
                for (int i = 0; i < current.Count; i++)
                {
                    f.Points.Add(current[i]);
                }
                fragments.Add(f);
            }
            current = null;
        }

        private static InkPoint Lerp(in InkPoint a, in InkPoint b, double t)
        {
            double x = a.XDip + (b.XDip - a.XDip) * t;
            double y = a.YDip + (b.YDip - a.YDip) * t;
            float p = (float)(a.Pressure + (b.Pressure - a.Pressure) * t);
            long ticks = (long)Math.Round(a.TimestampTicks + (b.TimestampTicks - a.TimestampTicks) * t);
            return new InkPoint(x, y, p, ticks);
        }

        private static int FindSegmentCircleIntersections(
            in InkPoint a,
            in InkPoint b,
            double centerXDip,
            double centerYDip,
            double radiusDip,
            out double t0,
            out double t1)
        {
            t0 = 0;
            t1 = 0;

            double ax = a.XDip;
            double ay = a.YDip;
            double bx = b.XDip;
            double by = b.YDip;

            double dx = bx - ax;
            double dy = by - ay;
            double aCoeff = (dx * dx) + (dy * dy);
            if (aCoeff <= MinSegmentLengthSquared)
            {
                return 0;
            }

            double fx = ax - centerXDip;
            double fy = ay - centerYDip;
            double bCoeff = 2.0 * ((fx * dx) + (fy * dy));
            double cCoeff = (fx * fx) + (fy * fy) - (radiusDip * radiusDip);

            double disc = (bCoeff * bCoeff) - (4.0 * aCoeff * cCoeff);
            if (disc < 0)
            {
                return 0;
            }

            double sqrtDisc = Math.Sqrt(disc);
            double inv = 0.5 / aCoeff;
            double r0 = (-bCoeff - sqrtDisc) * inv;
            double r1 = (-bCoeff + sqrtDisc) * inv;

            if (r0 > r1)
            {
                (r0, r1) = (r1, r0);
            }

            int count = 0;
            if (r0 >= 0.0 - IntersectionEpsilon && r0 <= 1.0 + IntersectionEpsilon)
            {
                t0 = Math.Clamp(r0, 0.0, 1.0);
                count = 1;
            }

            if (r1 >= 0.0 - IntersectionEpsilon && r1 <= 1.0 + IntersectionEpsilon)
            {
                if (count == 0)
                {
                    t0 = Math.Clamp(r1, 0.0, 1.0);
                    count = 1;
                }
                else
                {
                    t1 = Math.Clamp(r1, 0.0, 1.0);
                    count = 2;
                }
            }

            if (count == 2 && t0 > t1)
            {
                (t0, t1) = (t1, t0);
            }

            return count;
        }

        private static bool TryClipSegmentToRect(
            double ax,
            double ay,
            double bx,
            double by,
            double left,
            double top,
            double right,
            double bottom,
            out double tEnter,
            out double tExit)
        {
            tEnter = 0.0;
            tExit = 1.0;

            double dx = bx - ax;
            double dy = by - ay;

            if (!ClipTest(-dx, ax - left, ref tEnter, ref tExit)) return false;
            if (!ClipTest(dx, right - ax, ref tEnter, ref tExit)) return false;
            if (!ClipTest(-dy, ay - top, ref tEnter, ref tExit)) return false;
            if (!ClipTest(dy, bottom - ay, ref tEnter, ref tExit)) return false;

            return true;
        }

        private static bool ClipTest(double p, double q, ref double tEnter, ref double tExit)
        {
            if (Math.Abs(p) <= IntersectionEpsilon)
            {
                return q >= 0;
            }

            double r = q / p;
            if (p < 0)
            {
                if (r > tExit) return false;
                if (r > tEnter) tEnter = r;
            }
            else
            {
                if (r < tEnter) return false;
                if (r < tExit) tExit = r;
            }

            return true;
        }
    }
}

