using System;
using System.Numerics;
using Windows.UI;
using WindBoard.Board.Editing;
using WindBoard.Features.ScreenAnnotation.Models;
using WindBoard.Interaction;
using WindBoard.Settings;

namespace WindBoard.Features.ScreenAnnotation.Services
{
    /// <summary>
    /// 托管屏幕批注会话的默认参数。
    /// </summary>
    internal sealed class ScreenAnnotationSessionHost
    {
        private static readonly Color FallbackPenColor = Color.FromArgb(0xFF, 0x00, 0x00, 0x00);

        private readonly Color _defaultPenColor;
        private readonly float _defaultPenBaseSize;

        internal ScreenAnnotationSessionHost()
            : this(AppSettingsService.Instance.GetPenSettingsSnapshot())
        {
        }

        internal ScreenAnnotationSessionHost(PenSettingsSnapshot penSettingsSnapshot)
        {
            if (penSettingsSnapshot is null)
            {
                throw new ArgumentNullException(nameof(penSettingsSnapshot));
            }

            _defaultPenColor = ResolveDefaultPenColor(penSettingsSnapshot);
            _defaultPenBaseSize = ResolveDefaultPenBaseSize(penSettingsSnapshot);
        }

        internal BoardSession Session { get; } = new();

        internal Color CanvasBackgroundColor => Color.FromArgb(0x00, 0x00, 0x00, 0x00);

        internal BoardTool DefaultTool => BoardTool.Pen;

        internal float DefaultPenBaseSize => _defaultPenBaseSize;

        internal Color DefaultPenColor => _defaultPenColor;

        internal ScreenAnnotationEraserMode DefaultEraserMode => ScreenAnnotationEraserMode.Pixel;

        internal IBoardEraser DefaultEraser { get; } = new PixelStrokeEraser();

        internal ScreenAnnotationDrawingStateSnapshot CreateInitialDrawingStateSnapshot()
        {
            return new ScreenAnnotationDrawingStateSnapshot(
                PenColor: DefaultPenColor,
                PenBaseSize: DefaultPenBaseSize,
                EraserMode: DefaultEraserMode,
                CanClear: false);
        }

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

        private static Color ResolveDefaultPenColor(PenSettingsSnapshot penSettingsSnapshot)
        {
            foreach (string? hex in penSettingsSnapshot.PaletteHexes)
            {
                if (ColorHex.TryParse(hex, out Color color))
                {
                    return Color.FromArgb(0xFF, color.R, color.G, color.B);
                }
            }

            return FallbackPenColor;
        }

        private static float ResolveDefaultPenBaseSize(PenSettingsSnapshot penSettingsSnapshot)
        {
            if (penSettingsSnapshot.ThicknessPresets.Length >= 2)
            {
                return penSettingsSnapshot.ThicknessPresets[1];
            }

            return 3.0f;
        }
    }

    /// <summary>
    /// 屏幕批注视口预设。
    /// </summary>
    internal readonly record struct ScreenAnnotationViewportPreset(Vector2 CameraWorld, float Zoom);
}
