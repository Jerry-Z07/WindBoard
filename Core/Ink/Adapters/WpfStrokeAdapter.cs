using System;
using System.Collections.Generic;
using System.Windows.Ink;
using WindBoard.Core.Ink;
using WindBoard.Models.Ink;

namespace WindBoard.Core.Ink.Adapters
{
    internal static class WpfStrokeAdapter
    {
        private const float DefaultPressure = 0.5f;

        public static List<InkStrokeModel> ToModelList(StrokeCollection strokes, double currentZoom)
        {
            var list = new List<InkStrokeModel>(strokes?.Count ?? 0);
            if (strokes == null) return list;

            for (int i = 0; i < strokes.Count; i++)
            {
                var s = strokes[i];
                if (s == null) continue;
                list.Add(ToModel(s, currentZoom));
            }

            return list;
        }

        public static InkStrokeModel ToModel(Stroke stroke, double currentZoom)
        {
            if (stroke == null) throw new ArgumentNullException(nameof(stroke));

            double logical = StrokeThicknessMetadata.GetOrCreateLogicalThicknessDip(stroke, currentZoom <= 0 ? 1.0 : currentZoom);

            var da = stroke.DrawingAttributes;
            double zoomAtCreation = 1.0;
            try
            {
                double render = Math.Max(da.Width, da.Height);
                if (!double.IsNaN(render) && !double.IsInfinity(render) && render > 0 && logical > 0)
                {
                    zoomAtCreation = logical / render;
                }
            }
            catch
            {
            }
            if (double.IsNaN(zoomAtCreation) || double.IsInfinity(zoomAtCreation) || zoomAtCreation <= 0)
            {
                zoomAtCreation = currentZoom <= 0 ? 1.0 : currentZoom;
            }

            var model = new InkStrokeModel
            {
                Id = Guid.NewGuid(),
                ZoomAtCreation = zoomAtCreation,
                Style = new InkStrokeStyle(
                    InkBrushKind.Pen,
                    da.Color,
                    logical,
                    UsesPressure: !da.IgnorePressure)
            };

            var pts = stroke.StylusPoints;
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                float pressure = p.PressureFactor;
                if (float.IsNaN(pressure) || float.IsInfinity(pressure))
                {
                    pressure = DefaultPressure;
                }
                model.Points.Add(new InkPoint(p.X, p.Y, pressure, TimestampTicks: 0));
            }

            return model;
        }

        public static StrokeCollection ToStrokeCollection(IEnumerable<InkStrokeModel> strokes, double currentZoom)
        {
            var collection = new StrokeCollection();
            if (strokes == null) return collection;

            foreach (var s in strokes)
            {
                var stroke = ToWpfStroke(s, currentZoom);
                if (stroke != null)
                {
                    collection.Add(stroke);
                }
            }

            return collection;
        }

        public static Stroke? ToWpfStroke(InkStrokeModel model, double currentZoom)
        {
            if (model == null) return null;
            if (model.Points.Count == 0) return null;

            double logical = model.Style.LogicalThicknessDip;
            if (logical <= 0) logical = 1.0;

            double zoomAtCreation = model.ZoomAtCreation;
            if (double.IsNaN(zoomAtCreation) || double.IsInfinity(zoomAtCreation) || zoomAtCreation <= 0)
            {
                zoomAtCreation = currentZoom <= 0 ? 1.0 : currentZoom;
            }

            double renderThicknessDip = logical / zoomAtCreation;
            if (double.IsNaN(renderThicknessDip) || double.IsInfinity(renderThicknessDip) || renderThicknessDip <= 0)
            {
                renderThicknessDip = 1.0;
            }

            var stylusPoints = new System.Windows.Input.StylusPointCollection();
            for (int i = 0; i < model.Points.Count; i++)
            {
                var p = model.Points[i];
                stylusPoints.Add(new System.Windows.Input.StylusPoint(p.X, p.Y, p.Pressure));
            }

            var da = new DrawingAttributes
            {
                Color = model.Style.Color,
                FitToCurve = false,
                IgnorePressure = !model.Style.UsesPressure,
                Width = renderThicknessDip,
                Height = renderThicknessDip
            };

            var stroke = new Stroke(stylusPoints)
            {
                DrawingAttributes = da
            };
            StrokeThicknessMetadata.SetLogicalThicknessDip(stroke, logical);
            return stroke;
        }
    }
}
