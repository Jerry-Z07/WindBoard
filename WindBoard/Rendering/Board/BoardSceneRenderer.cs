using System;
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using WindBoard.Board;
using WindBoard.Board.Viewport;

namespace WindBoard.Rendering.Board
{
    internal sealed class BoardSceneRenderer : IDisposable
    {
        private ID2D1SolidColorBrush? _strokeBrush;
        private ID2D1SolidColorBrush? _gridMinorBrush;
        private ID2D1SolidColorBrush? _gridMajorBrush;
        private ID2D1SolidColorBrush? _axisBrush;

        public void Draw(ID2D1DeviceContext ctx, BoardDocument document, Stroke? activeStroke, BoardViewport viewport)
        {
            _strokeBrush ??= ctx.CreateSolidColorBrush(new Color4(0, 0, 0, 1));
            _gridMinorBrush ??= ctx.CreateSolidColorBrush(new Color4(0.92f, 0.92f, 0.92f, 1.0f));
            _gridMajorBrush ??= ctx.CreateSolidColorBrush(new Color4(0.86f, 0.86f, 0.86f, 1.0f));
            _axisBrush ??= ctx.CreateSolidColorBrush(new Color4(0.78f, 0.78f, 0.78f, 1.0f));

            Matrix3x2 oldTransform = ctx.Transform;
            ctx.Transform = viewport.GetWorldToScreenTransform();

            DrawInfiniteGrid(ctx, viewport);

            foreach (var stroke in document.Strokes)
            {
                DrawStroke(ctx, stroke);
            }

            if (activeStroke is not null)
            {
                DrawStroke(ctx, activeStroke);
            }

            ctx.Transform = oldTransform;
        }

        private void DrawStroke(ID2D1DeviceContext ctx, Stroke stroke)
        {
            if (_strokeBrush is null)
            {
                return;
            }

            _strokeBrush.Color = stroke.Color;

            if (stroke.Points.Count == 1)
            {
                float radius = Math.Max(0.5f, stroke.BaseSize * GetStrokeWidthFactor(stroke.Points[0].Pressure) / 2.0f);
                ctx.FillEllipse(new Ellipse(stroke.Points[0].Position, radius, radius), _strokeBrush);
                return;
            }

            for (int i = 1; i < stroke.Points.Count; i++)
            {
                StrokePoint p0 = stroke.Points[i - 1];
                StrokePoint p1 = stroke.Points[i];

                float widthFactor = stroke.EnablePressure
                    ? GetStrokeWidthFactor((p0.Pressure + p1.Pressure) / 2.0f)
                    : 1.0f;

                float strokeWidth = Math.Max(0.5f, stroke.BaseSize * widthFactor);
                ctx.DrawLine(p0.Position, p1.Position, _strokeBrush, strokeWidth);
            }
        }

        private void DrawInfiniteGrid(ID2D1DeviceContext ctx, BoardViewport viewport)
        {
            if (_gridMinorBrush is null || _gridMajorBrush is null || _axisBrush is null)
            {
                return;
            }

            Vector2 worldTopLeft = viewport.ScreenToWorld(Vector2.Zero);
            Vector2 worldBottomRight = viewport.ScreenToWorld(viewport.ViewportSizeDip);

            float minX = Math.Min(worldTopLeft.X, worldBottomRight.X);
            float maxX = Math.Max(worldTopLeft.X, worldBottomRight.X);
            float minY = Math.Min(worldTopLeft.Y, worldBottomRight.Y);
            float maxY = Math.Max(worldTopLeft.Y, worldBottomRight.Y);

            float step = GetAdaptiveGridStepWorld(viewport.Zoom);
            if (step <= 0.0f)
            {
                return;
            }

            const int majorEvery = 5;
            float minorThicknessWorld = 1.0f / Math.Max(0.0001f, viewport.Zoom);
            float majorThicknessWorld = 1.5f / Math.Max(0.0001f, viewport.Zoom);
            float axisThicknessWorld = 2.0f / Math.Max(0.0001f, viewport.Zoom);

            long firstX = (long)Math.Floor(minX / step);
            long lastX = (long)Math.Ceiling(maxX / step);
            long firstY = (long)Math.Floor(minY / step);
            long lastY = (long)Math.Ceiling(maxY / step);

            for (long ix = firstX; ix <= lastX; ix++)
            {
                float x = (float)(ix * step);
                bool isMajor = ix % majorEvery == 0;
                ctx.DrawLine(
                    new Vector2(x, minY),
                    new Vector2(x, maxY),
                    isMajor ? _gridMajorBrush : _gridMinorBrush,
                    isMajor ? majorThicknessWorld : minorThicknessWorld);
            }

            for (long iy = firstY; iy <= lastY; iy++)
            {
                float y = (float)(iy * step);
                bool isMajor = iy % majorEvery == 0;
                ctx.DrawLine(
                    new Vector2(minX, y),
                    new Vector2(maxX, y),
                    isMajor ? _gridMajorBrush : _gridMinorBrush,
                    isMajor ? majorThicknessWorld : minorThicknessWorld);
            }

            // 世界坐标原点轴（用于方向感）
            if (0.0f >= minX && 0.0f <= maxX)
            {
                ctx.DrawLine(new Vector2(0.0f, minY), new Vector2(0.0f, maxY), _axisBrush, axisThicknessWorld);
            }

            if (0.0f >= minY && 0.0f <= maxY)
            {
                ctx.DrawLine(new Vector2(minX, 0.0f), new Vector2(maxX, 0.0f), _axisBrush, axisThicknessWorld);
            }
        }

        private static float GetAdaptiveGridStepWorld(float zoom)
        {
            // 基准：zoom=1 时每 40 DIP 一格。根据缩放自适应，保证屏幕上网格密度大致稳定。
            float step = 40.0f;
            float stepScreen = step * zoom;

            while (stepScreen < 20.0f)
            {
                step *= 2.0f;
                stepScreen = step * zoom;
            }

            while (stepScreen > 80.0f)
            {
                step /= 2.0f;
                stepScreen = step * zoom;
            }

            return step;
        }

        private static float GetStrokeWidthFactor(float normalizedPressure)
        {
            return Math.Clamp(normalizedPressure, 0.1f, 1.0f);
        }

        public void Dispose()
        {
            _strokeBrush?.Dispose();
            _strokeBrush = null;

            _gridMinorBrush?.Dispose();
            _gridMinorBrush = null;

            _gridMajorBrush?.Dispose();
            _gridMajorBrush = null;

            _axisBrush?.Dispose();
            _axisBrush = null;
        }
    }
}

