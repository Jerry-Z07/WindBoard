using System;
using System.Numerics;
using Windows.UI;
using WindBoard.Board.Editing;
using WindBoard.Interaction;

namespace WindBoard.Features.ScreenAnnotation.Services
{
    /// <summary>
    /// 托管屏幕批注会话的默认参数。
    /// </summary>
    internal sealed class ScreenAnnotationSessionHost
    {
        internal BoardSession Session { get; } = new();

        internal Color CanvasBackgroundColor => Color.FromArgb(0x00, 0x00, 0x00, 0x00);

        internal BoardTool DefaultTool => BoardTool.Pen;

        internal float DefaultPenBaseSize => 3.0f;

        internal Color DefaultPenColor => Color.FromArgb(0xFF, 0xFF, 0x24, 0x24);

        internal IBoardEraser DefaultEraser { get; } = new PixelStrokeEraser();

        /// <summary>
        /// 构造桌面批注固定视口预设，使世界坐标近似贴合屏幕坐标。
        /// </summary>
        internal ScreenAnnotationViewportPreset BuildViewportPreset(Vector2 viewportSizeDip)
        {
            Vector2 safeSize = new(
                Math.Max(1.0f, viewportSizeDip.X),
                Math.Max(1.0f, viewportSizeDip.Y));

            return new ScreenAnnotationViewportPreset(
                CameraWorld: safeSize / 2.0f,
                Zoom: 1.0f);
        }
    }

    /// <summary>
    /// 屏幕批注视口预设。
    /// </summary>
    internal readonly record struct ScreenAnnotationViewportPreset(Vector2 CameraWorld, float Zoom);
}
