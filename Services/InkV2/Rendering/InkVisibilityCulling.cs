using System;
using System.Collections.Generic;
using WindBoard.Models.InkV2;

namespace WindBoard.Services.InkV2.Rendering
{
    internal readonly struct InkVisibilityStats
    {
        public InkVisibilityStats(
            int spatialHitCount,
            int visibleFragmentCount,
            int forceVisibleFragmentCount,
            bool selfHealRebuildAttempted,
            bool selfHealFallbackAllFragments)
        {
            SpatialHitCount = spatialHitCount;
            VisibleFragmentCount = visibleFragmentCount;
            ForceVisibleFragmentCount = forceVisibleFragmentCount;
            SelfHealRebuildAttempted = selfHealRebuildAttempted;
            SelfHealFallbackAllFragments = selfHealFallbackAllFragments;
        }

        public int SpatialHitCount { get; }
        public int VisibleFragmentCount { get; }
        public int ForceVisibleFragmentCount { get; }
        public bool SelfHealRebuildAttempted { get; }
        public bool SelfHealFallbackAllFragments { get; }
    }

    internal static class InkVisibilityCulling
    {
        public static InkRectDip ComputeWorldCullRect(
            double viewportWidthDip,
            double viewportHeightDip,
            double zoom,
            double panXDip,
            double panYDip,
            double cullMarginScreenDip)
        {
            if (zoom <= 0) zoom = 1.0;

            double worldLeft = (0 - panXDip) / zoom;
            double worldTop = (0 - panYDip) / zoom;
            double worldWidth = viewportWidthDip / zoom;
            double worldHeight = viewportHeightDip / zoom;

            double marginWorld = cullMarginScreenDip / zoom;
            return new InkRectDip(
                worldLeft - marginWorld,
                worldTop - marginWorld,
                worldWidth + marginWorld * 2,
                worldHeight + marginWorld * 2);
        }

        public static InkVisibilityStats GatherVisibleFragments(
            InkDocument document,
            InkSpatialIndex spatialIndex,
            InkRectDip rect,
            List<InkSegmentHit> hitScratch,
            HashSet<InkFragment> visibleFragments,
            IReadOnlyCollection<InkFragment>? forceVisibleFragments)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (spatialIndex == null) throw new ArgumentNullException(nameof(spatialIndex));
            if (hitScratch == null) throw new ArgumentNullException(nameof(hitScratch));
            if (visibleFragments == null) throw new ArgumentNullException(nameof(visibleFragments));

            bool selfHealRebuildAttempted = false;
            bool selfHealFallbackAllFragments = false;
            int forceVisibleCount = forceVisibleFragments?.Count ?? 0;

            hitScratch.Clear();
            visibleFragments.Clear();

            if (rect.Width > 0 && rect.Height > 0)
            {
                try
                {
                    spatialIndex.QueryRect(rect, hitScratch);
                }
                catch
                {
                    hitScratch.Clear();
                }
            }

            for (int i = 0; i < hitScratch.Count; i++)
            {
                visibleFragments.Add(hitScratch[i].Fragment);
            }

            // If the view intersects ink bounds but the spatial index returns no hits,
            // attempt a one-shot rebuild and finally fall back to "all fragments".
            if (visibleFragments.Count == 0 &&
                forceVisibleCount == 0 &&
                document.Strokes.Count > 0 &&
                TryComputeInkBounds(document, out double inkMinX, out double inkMinY, out double inkMaxX, out double inkMaxY) &&
                rect.Intersects(inkMinX, inkMinY, inkMaxX, inkMaxY))
            {
                try
                {
                    selfHealRebuildAttempted = true;
                    spatialIndex.Rebuild(document);

                    hitScratch.Clear();
                    spatialIndex.QueryRect(rect, hitScratch);

                    for (int i = 0; i < hitScratch.Count; i++)
                    {
                        visibleFragments.Add(hitScratch[i].Fragment);
                    }
                }
                catch
                {
                }

                if (visibleFragments.Count == 0)
                {
                    selfHealFallbackAllFragments = true;
                    AddAllFragments(document, visibleFragments);
                }
            }

            if (forceVisibleFragments != null && forceVisibleFragments.Count > 0)
            {
                foreach (InkFragment fragment in forceVisibleFragments)
                {
                    if (fragment == null) continue;
                    visibleFragments.Add(fragment);
                }
            }

            return new InkVisibilityStats(
                spatialHitCount: hitScratch.Count,
                visibleFragmentCount: visibleFragments.Count,
                forceVisibleFragmentCount: forceVisibleCount,
                selfHealRebuildAttempted: selfHealRebuildAttempted,
                selfHealFallbackAllFragments: selfHealFallbackAllFragments);
        }

        private static void AddAllFragments(InkDocument document, HashSet<InkFragment> destination)
        {
            for (int si = 0; si < document.Strokes.Count; si++)
            {
                InkStroke stroke = document.Strokes[si];
                for (int fi = 0; fi < stroke.Fragments.Count; fi++)
                {
                    destination.Add(stroke.Fragments[fi]);
                }
            }
        }

        private static bool TryComputeInkBounds(InkDocument document, out double minX, out double minY, out double maxX, out double maxY)
        {
            minX = 0;
            minY = 0;
            maxX = 0;
            maxY = 0;

            bool any = false;

            for (int si = 0; si < document.Strokes.Count; si++)
            {
                InkStroke stroke = document.Strokes[si];
                for (int fi = 0; fi < stroke.Fragments.Count; fi++)
                {
                    InkFragment fragment = stroke.Fragments[fi];
                    List<InkPoint> points = fragment.Points;
                    for (int pi = 0; pi < points.Count; pi++)
                    {
                        InkPoint p = points[pi];
                        if (!any)
                        {
                            any = true;
                            minX = maxX = p.XDip;
                            minY = maxY = p.YDip;
                            continue;
                        }

                        minX = Math.Min(minX, p.XDip);
                        minY = Math.Min(minY, p.YDip);
                        maxX = Math.Max(maxX, p.XDip);
                        maxY = Math.Max(maxY, p.YDip);
                    }
                }
            }

            return any;
        }
    }
}
