using System;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using WindBoard.Core.Ink;
using WindBoard.Models.InkV2;
using WindBoard.Services.InkV2;

namespace WindBoard
{
    public partial class MainWindow
    {
        private const int MaxStylusPointsPerWpfStroke = 1800;
        private const double ThicknessEpsilonDip = 0.0001;

        private uint _inkColorArgb = 0xFFFFFFFF;
        private InkThicknessSemantics _inkThicknessSemantics = InkThicknessSemantics.ViewInvariant;

        private InkTool CreateCurrentInkToolSnapshot()
        {
            return new InkTool(
                ColorArgb: _inkColorArgb,
                BaseThicknessDip: _baseThickness,
                ThicknessSemantics: _inkThicknessSemantics,
                BrushKind: InkBrushKind.Pen,
                UsesPressure: false,
                PressureNominal: 1.0f);
        }

        private void UpdateInkStrokeThicknessForZoom(double zoom)
        {
            if (MyCanvas == null) return;
            if (zoom <= 0) zoom = 1.0;

            var strokes = MyCanvas.Strokes;
            if (strokes == null || strokes.Count == 0) return;

            for (int i = 0; i < strokes.Count; i++)
            {
                Stroke stroke = strokes[i];
                if (stroke == null) continue;

                if (!StrokeThicknessMetadata.TryGetLogicalThicknessDip(stroke, out double logicalThicknessDip))
                {
                    continue;
                }

                InkThicknessSemantics semantics = InkThicknessSemantics.ViewInvariant;
                _ = StrokeInkSemanticsMetadata.TryGetThicknessSemantics(stroke, out semantics);

                double renderDip = semantics == InkThicknessSemantics.ViewInvariant
                    ? logicalThicknessDip / zoom
                    : logicalThicknessDip;

                var da = stroke.DrawingAttributes;
                if (Math.Abs(da.Width - renderDip) > ThicknessEpsilonDip ||
                    Math.Abs(da.Height - renderDip) > ThicknessEpsilonDip)
                {
                    da.Width = renderDip;
                    da.Height = renderDip;
                }
            }
        }

        private void RebuildInkCanvasV2Strokes(BoardPage page)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));
            if (MyCanvas == null) return;

            var strokes = MyCanvas.Strokes;
            if (strokes == null) return;

            for (int i = strokes.Count - 1; i >= 0; i--)
            {
                Stroke s = strokes[i];
                if (s == null) continue;
                if (s.ContainsPropertyData(StrokeInkSemanticsMetadata.InkStrokeIdPropertyId))
                {
                    strokes.RemoveAt(i);
                }
            }

            double zoom = _zoomPanService?.Zoom ?? 1.0;
            if (zoom <= 0) zoom = 1.0;

            for (int si = 0; si < page.Ink.Strokes.Count; si++)
            {
                InkStroke inkStroke = page.Ink.Strokes[si];
                InkTool tool = inkStroke.Tool;

                double logicalThicknessDip = InkToolThickness.ComputeLogicalThicknessDip(tool);
                var da = CreateDrawingAttributes(tool, zoom, logicalThicknessDip);

                for (int fi = 0; fi < inkStroke.Fragments.Count; fi++)
                {
                    InkFragment fragment = inkStroke.Fragments[fi];
                    if (fragment.Points.Count == 0)
                    {
                        continue;
                    }

                    AddFragmentAsWpfStrokes(strokes, inkStroke, fragment, da, logicalThicknessDip);
                }
            }
        }

        private void EraseInkV2ByRect(Rect rect)
        {
            BoardPage? page = _pageService?.CurrentPage;
            if (page == null) return;
            if (rect.Width <= 0 || rect.Height <= 0) return;

            bool changed = InkEraserEngine.EraseRect(
                page.Ink,
                page.InkSpatialIndex,
                page.InkUndoHistory,
                new InkRectDip(rect.X, rect.Y, rect.Width, rect.Height));

            if (!changed)
            {
                return;
            }

            RebuildInkCanvasV2Strokes(page);
            page.ContentVersion++;
        }

        private static void AddFragmentAsWpfStrokes(
            StrokeCollection strokes,
            InkStroke inkStroke,
            InkFragment fragment,
            DrawingAttributes drawingAttributes,
            double logicalThicknessDip)
        {
            int index = 0;
            while (index < fragment.Points.Count)
            {
                var spc = new StylusPointCollection();
                if (index > 0)
                {
                    spc.Add(ToStylusPoint(fragment.Points[index - 1]));
                }

                int remaining = fragment.Points.Count - index;
                int cap = MaxStylusPointsPerWpfStroke - spc.Count;
                int take = Math.Min(remaining, cap);

                for (int i = 0; i < take; i++)
                {
                    spc.Add(ToStylusPoint(fragment.Points[index + i]));
                }

                index += take;

                var s = new Stroke(spc)
                {
                    DrawingAttributes = drawingAttributes
                };

                StrokeThicknessMetadata.SetLogicalThicknessDip(s, logicalThicknessDip);
                StrokeInkSemanticsMetadata.SetThicknessSemantics(s, inkStroke.Tool.ThicknessSemantics);
                StrokeInkSemanticsMetadata.SetInkStrokeId(s, inkStroke.StrokeId);
                StrokeInkSemanticsMetadata.SetInkFragmentId(s, fragment.FragmentId);

                strokes.Add(s);
            }
        }

        private static StylusPoint ToStylusPoint(InkPoint p)
        {
            return new StylusPoint(p.XDip, p.YDip, p.Pressure);
        }

        private static DrawingAttributes CreateDrawingAttributes(InkTool tool, double zoom, double logicalThicknessDip)
        {
            double renderThicknessDip = InkToolThickness.ComputeRenderThicknessDip(tool, zoom, logicalThicknessDip);

            var da = new DrawingAttributes
            {
                FitToCurve = false,
                IgnorePressure = !tool.UsesPressure,
                Width = renderThicknessDip,
                Height = renderThicknessDip
            };

            da.Color = ColorFromArgb(tool.ColorArgb);
            return da;
        }

        private static System.Windows.Media.Color ColorFromArgb(uint argb)
        {
            byte a = (byte)((argb >> 24) & 0xFF);
            byte r = (byte)((argb >> 16) & 0xFF);
            byte g = (byte)((argb >> 8) & 0xFF);
            byte b = (byte)(argb & 0xFF);
            return System.Windows.Media.Color.FromArgb(a, r, g, b);
        }
    }
}
