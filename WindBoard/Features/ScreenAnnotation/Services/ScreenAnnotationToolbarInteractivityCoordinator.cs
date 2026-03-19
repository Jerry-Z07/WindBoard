using System;
using WindBoard.Features.ScreenAnnotation.Models;

namespace WindBoard.Features.ScreenAnnotation.Services
{
    /// <summary>
    /// 统一协调屏幕批注模式切换时的工具栏可交互性。
    /// </summary>
    internal static class ScreenAnnotationToolbarInteractivityCoordinator
    {
        private const uint SwpNoZOrder = 0x0004;

        internal static void ApplyMode(
            ScreenAnnotationMode mode,
            ScreenAnnotationWindowState? windowState,
            IScreenAnnotationModeOverlay? overlay,
            IScreenAnnotationModeToolbar? toolbar)
        {
            if (overlay is not null)
            {
                overlay.ApplyMode(mode);
            }
            else
            {
                // 兜底分支：窗口尚未创建时也要同步逻辑状态，避免后续初始化拿到旧模式。
                windowState?.SetMode(mode);
            }

            if (toolbar is null)
            {
                return;
            }

            toolbar.SetSelectedMode(mode);

            // 无论当前是穿透还是书写态，都显式恢复“工具栏在批注层之上”的相对层级。
            toolbar.EnsureInteractiveTopMost(overlay);
        }

        internal static void EnsureToolbarInteractiveAfterOverlayActivation(
            IScreenAnnotationModeOverlay? overlay,
            IScreenAnnotationModeToolbar? toolbar)
        {
            toolbar?.EnsureInteractiveTopMost(overlay);
        }

        internal static void EnsureToolbarInteractiveAfterOverlayWindowPositionChanged(
            IScreenAnnotationModeOverlay? overlay,
            IScreenAnnotationModeToolbar? toolbar,
            IntPtr insertAfterHwnd,
            uint windowPosFlags)
        {
            if (overlay is null || toolbar is null)
            {
                return;
            }

            // 仅当原生消息明确包含 Z 序变化时才需要恢复相对层级，避免普通移动/尺寸变更造成噪声。
            if ((windowPosFlags & SwpNoZOrder) != 0)
            {
                return;
            }

            // 如果批注层已经被压到工具栏后面，则说明当前顺序正确，不再重复触发恢复，避免自激循环。
            if (toolbar.TryGetWindowHandle(out IntPtr toolbarHwnd)
                && toolbarHwnd != IntPtr.Zero
                && insertAfterHwnd == toolbarHwnd)
            {
                return;
            }

            toolbar.EnsureInteractiveTopMost(overlay);
        }
    }

    internal interface IScreenAnnotationModeOverlay
    {
        void ApplyMode(ScreenAnnotationMode mode);

        bool TryGetWindowHandle(out IntPtr hwnd);
    }

    internal interface IScreenAnnotationModeToolbar
    {
        void SetSelectedMode(ScreenAnnotationMode mode);

        void EnsureInteractiveTopMost(IScreenAnnotationModeOverlay? overlay);

        bool TryGetWindowHandle(out IntPtr hwnd);
    }
}
