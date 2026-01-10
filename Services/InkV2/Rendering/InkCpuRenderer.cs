using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using WindBoard.Models.InkV2;

namespace WindBoard.Services.InkV2.Rendering
{
    internal static class InkCpuRenderer
    {
        public static Rect CalculateInkBounds(InkDocument document)
        {
            if (document.Strokes.Count == 0)
            {
                return Rect.Empty;
            }

            bool any = false;
            double minX = 0;
            double minY = 0;
            double maxX = 0;
            double maxY = 0;

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

            return any ? new Rect(minX, minY, maxX - minX, maxY - minY) : Rect.Empty;
        }

        public static void RenderInk(DrawingContext dc, InkDocument document, double zoom)
        {
            if (document.Strokes.Count == 0) return;
            if (zoom <= 0) zoom = 1.0;

            for (int si = 0; si < document.Strokes.Count; si++)
            {
                InkStroke stroke = document.Strokes[si];
                InkTool tool = stroke.Tool;

                Pen? pen = CreateInkPen(tool, zoom);
                if (pen == null) continue;

                for (int fi = 0; fi < stroke.Fragments.Count; fi++)
                {
                    InkFragment fragment = stroke.Fragments[fi];
                    StreamGeometry? geometry = BuildPolylineGeometry(fragment.Points);
                    if (geometry == null) continue;

                    dc.DrawGeometry(null, pen, geometry);
                }
            }
        }

        public static Color ColorFromArgb(uint argb)
        {
            byte a = (byte)((argb >> 24) & 0xFF);
            byte r = (byte)((argb >> 16) & 0xFF);
            byte g = (byte)((argb >> 8) & 0xFF);
            byte b = (byte)(argb & 0xFF);
            return Color.FromArgb(a, r, g, b);
        }

        private static Pen? CreateInkPen(InkTool tool, double zoom)
        {
            double baseThickness = tool.BaseThicknessDip;
            if (baseThickness <= 0 || double.IsNaN(baseThickness) || double.IsInfinity(baseThickness))
            {
                baseThickness = 1.0;
            }

            double widthWorldDip = tool.ThicknessSemantics == InkThicknessSemantics.ViewInvariant
                ? baseThickness / zoom
                : baseThickness;

            if (widthWorldDip <= 0.001 || double.IsNaN(widthWorldDip) || double.IsInfinity(widthWorldDip))
            {
                widthWorldDip = 0.001;
            }

            var brush = new SolidColorBrush(ColorFromArgb(tool.ColorArgb));
            brush.Freeze();

            var pen = new Pen(brush, widthWorldDip)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
                MiterLimit = 1.0
            };
            pen.Freeze();
            return pen;
        }

        private static StreamGeometry? BuildPolylineGeometry(List<InkPoint> points)
        {
            if (points.Count < 2) return null;

            int firstIndex = FindFirstDistinctPointIndex(points);
            if (firstIndex < 0) return null;

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                InkPoint p0 = points[firstIndex];
                ctx.BeginFigure(new Point(p0.XDip, p0.YDip), isFilled: false, isClosed: false);

                double lastX = p0.XDip;
                double lastY = p0.YDip;

                for (int i = firstIndex + 1; i < points.Count; i++)
                {
                    InkPoint p = points[i];
                    if (Math.Abs(p.XDip - lastX) <= 1e-9 && Math.Abs(p.YDip - lastY) <= 1e-9)
                    {
                        continue;
                    }

                    ctx.LineTo(new Point(p.XDip, p.YDip), isStroked: true, isSmoothJoin: false);
                    lastX = p.XDip;
                    lastY = p.YDip;
                }
            }

            geometry.Freeze();
            return geometry;
        }

        private static int FindFirstDistinctPointIndex(List<InkPoint> points)
        {
            if (points.Count < 2) return -1;
            InkPoint a = points[0];
            for (int i = 1; i < points.Count; i++)
            {
                InkPoint b = points[i];
                if (Math.Abs(a.XDip - b.XDip) > 1e-9 || Math.Abs(a.YDip - b.YDip) > 1e-9)
                {
                    return 0;
                }
            }
            return -1;
        }
    }
}

