namespace WindBoard.Features.ScreenAnnotation.Models
{
    /// <summary>
    /// 屏幕批注模式：
    /// - PassThrough：窗口继续可见，但输入穿透到底层应用；
    /// - Pen / Eraser / 形状（Line/Rectangle/Ellipse/Arrow）：批注层接管输入，并映射到现有画板工具。
    /// </summary>
    internal enum ScreenAnnotationMode
    {
        PassThrough,
        Pen,
        Eraser,
        Line,
        Rectangle,
        Ellipse,
        Arrow,
    }

    /// <summary>
    /// 形状类模式辅助判断（形状按钮承载 4 个形状工具的选中态，与主白板 IsShapeTool 语义一致）。
    /// </summary>
    internal static class ScreenAnnotationModeExtensions
    {
        internal static bool IsShapeMode(this ScreenAnnotationMode mode)
        {
            return mode is ScreenAnnotationMode.Line
                or ScreenAnnotationMode.Rectangle
                or ScreenAnnotationMode.Ellipse
                or ScreenAnnotationMode.Arrow;
        }
    }
}
