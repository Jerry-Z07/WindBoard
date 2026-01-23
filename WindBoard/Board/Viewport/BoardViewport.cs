using System;
using System.Numerics;

namespace WindBoard.Board.Viewport
{
    internal sealed class BoardViewport
    {
        private const float MinZoom = 0.05f;
        private const float MaxZoom = 32.0f;

        public float Zoom { get; private set; } = 1.0f;

        public Vector2 CameraWorld { get; private set; } = Vector2.Zero;

        public Vector2 ViewportSizeDip { get; private set; } = new(1.0f, 1.0f);

        public Vector2 ViewportCenterDip => ViewportSizeDip / 2.0f;

        public void UpdateViewportSize(Vector2 sizeDip)
        {
            float w = Math.Max(1.0f, sizeDip.X);
            float h = Math.Max(1.0f, sizeDip.Y);
            ViewportSizeDip = new Vector2(w, h);
        }

        public Matrix3x2 GetWorldToScreenTransform()
        {
            Vector2 viewportCenter = ViewportCenterDip;
            return Matrix3x2.CreateTranslation(-CameraWorld)
                * Matrix3x2.CreateScale(Zoom)
                * Matrix3x2.CreateTranslation(viewportCenter);
        }

        public Vector2 ScreenToWorld(Vector2 screenDip)
        {
            Vector2 viewportCenter = ViewportCenterDip;
            return (screenDip - viewportCenter) / Math.Max(0.0001f, Zoom) + CameraWorld;
        }

        public void PanByScreenDelta(Vector2 deltaScreenDip)
        {
            CameraWorld -= deltaScreenDip / Math.Max(0.0001f, Zoom);
        }

        public void ZoomAboutScreenPoint(Vector2 anchorScreenDip, float zoomFactor)
        {
            if (zoomFactor <= 0.0f)
            {
                return;
            }

            float oldZoom = Zoom;
            float newZoom = Math.Clamp(oldZoom * zoomFactor, MinZoom, MaxZoom);
            if (Math.Abs(newZoom - oldZoom) < 0.000001f)
            {
                return;
            }

            Vector2 viewportCenter = ViewportCenterDip;
            Vector2 worldBefore = (anchorScreenDip - viewportCenter) / Math.Max(0.0001f, oldZoom) + CameraWorld;

            Zoom = newZoom;
            CameraWorld = worldBefore - (anchorScreenDip - viewportCenter) / Math.Max(0.0001f, Zoom);
        }
    }
}

