using WindBoard.Features.ScreenAnnotation.Models;

namespace WindBoard.Features.ScreenAnnotation.UI
{
    /// <summary>
    /// 屏幕批注工具栏的纯逻辑辅助方法。
    /// </summary>
    internal static class ScreenAnnotationToolbarBehavior
    {
        internal static bool IsSecondaryClick(ScreenAnnotationMode currentMode, ScreenAnnotationMode requestedMode)
        {
            return currentMode == requestedMode;
        }
    }
}

