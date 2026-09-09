using WindBoard.Features.ScreenAnnotation.Models;
using WindBoard.Interaction;

namespace WindBoard.Features.ScreenAnnotation.Services
{
    /// <summary>
    /// 屏幕批注运行态窗口状态。
    /// </summary>
    /// <remarks>
    /// PassThrough 不直接映射到 <see cref="BoardTool.Select"/>，
    /// 这里只记录“当前是否穿透”以及“最近一次绘图工具”，供窗口层切换命中测试时使用。
    /// </remarks>
    internal sealed class ScreenAnnotationWindowState
    {
        internal ScreenAnnotationMode Mode { get; private set; } = ScreenAnnotationMode.PassThrough;

        internal BoardTool ActiveCanvasTool { get; private set; } = BoardTool.Pen;

        internal bool IsPassThrough => Mode == ScreenAnnotationMode.PassThrough;

        internal void SetMode(ScreenAnnotationMode mode)
        {
            Mode = mode;

            switch (mode)
            {
                case ScreenAnnotationMode.Pen:
                    ActiveCanvasTool = BoardTool.Pen;
                    break;

                case ScreenAnnotationMode.Eraser:
                    ActiveCanvasTool = BoardTool.Eraser;
                    break;

                // 形状模式映射同名画板工具（design G）：经现有 ActiveCanvasTool 链路下传画布。
                case ScreenAnnotationMode.Line:
                    ActiveCanvasTool = BoardTool.Line;
                    break;

                case ScreenAnnotationMode.Rectangle:
                    ActiveCanvasTool = BoardTool.Rectangle;
                    break;

                case ScreenAnnotationMode.Ellipse:
                    ActiveCanvasTool = BoardTool.Ellipse;
                    break;

                case ScreenAnnotationMode.Arrow:
                    ActiveCanvasTool = BoardTool.Arrow;
                    break;
            }
        }
    }
}
