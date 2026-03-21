namespace WindBoard.Features.ScreenAnnotation.Models
{
    /// <summary>
    /// 屏幕批注模式：
    /// - PassThrough：窗口继续可见，但输入穿透到底层应用；
    /// - Pen / Eraser：批注层接管输入，并映射到现有画板工具。
    /// </summary>
    internal enum ScreenAnnotationMode
    {
        PassThrough,
        Pen,
        Eraser,
    }
}
