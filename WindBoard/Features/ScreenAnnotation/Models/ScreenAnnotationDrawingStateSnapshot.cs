using Windows.UI;

namespace WindBoard.Features.ScreenAnnotation.Models
{
    /// <summary>
    /// 屏幕批注当前绘制状态快照。
    /// </summary>
    internal readonly record struct ScreenAnnotationDrawingStateSnapshot(
        Color PenColor,
        float PenBaseSize,
        ScreenAnnotationEraserMode EraserMode,
        bool CanClear);

    /// <summary>
    /// 屏幕批注擦除模式。
    /// </summary>
    internal enum ScreenAnnotationEraserMode
    {
        Pixel,
        WholeStroke,
    }
}
