using System;
using System.Numerics;

namespace WindBoard.Board.Viewport
{
    internal sealed class BoardViewport
    {
        private const float MinZoom = 0.05f;
        private const float MaxZoom = 32.0f;

        // 导出场景需要更大的缩放范围：
        // - 固定分辨率导出可能需要极小缩放（内容很大）或极大缩放（内容很小）；
        // - 交互缩放仍使用 MinZoom/MaxZoom，避免用户误操作导致视图不可控。
        private const float ExportMinZoom = 0.0001f;
        private const float ExportMaxZoom = 4096.0f;

        public float Zoom { get; private set; } = 1.0f;

        public Vector2 CameraWorld { get; private set; } = Vector2.Zero;

        public Vector2 ViewportSizeDip { get; private set; } = new(1.0f, 1.0f);

        public Vector2 ViewportCenterDip => ViewportSizeDip / 2.0f;

        /// <summary>
        /// 设置相机与缩放（导出/恢复视图等“非交互”场景使用）。
        /// </summary>
        /// <remarks>
        /// 注意：交互（平移/缩放）仍建议使用 <see cref="PanByScreenDelta"/> 与 <see cref="ZoomAboutScreenPoint"/>，
        /// 以便复用其边界与锚点逻辑。
        /// </remarks>
        internal void SetView(Vector2 cameraWorld, float zoom)
        {
            CameraWorld = cameraWorld;
            Zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        }

        /// <summary>
        /// 设置相机与缩放（导出场景使用）。
        /// </summary>
        /// <remarks>
        /// 导出时需要允许更宽的缩放范围，否则在“固定画面导出”中：
        /// - 内容极小时会被 <see cref="MaxZoom"/> 限制，导致无法铺满画面；
        /// - 内容极大时会被 <see cref="MinZoom"/> 限制，导致内容被裁切。
        /// </remarks>
        internal void SetViewForExport(Vector2 cameraWorld, float zoom)
        {
            CameraWorld = cameraWorld;
            Zoom = Math.Clamp(zoom, ExportMinZoom, ExportMaxZoom);
        }

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

        public void GetVisibleWorldBounds(out Vector2 minWorld, out Vector2 maxWorld)
        {
            Vector2 worldTopLeft = ScreenToWorld(Vector2.Zero);
            Vector2 worldBottomRight = ScreenToWorld(ViewportSizeDip);

            minWorld = new Vector2(
                Math.Min(worldTopLeft.X, worldBottomRight.X),
                Math.Min(worldTopLeft.Y, worldBottomRight.Y));

            maxWorld = new Vector2(
                Math.Max(worldTopLeft.X, worldBottomRight.X),
                Math.Max(worldTopLeft.Y, worldBottomRight.Y));
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
