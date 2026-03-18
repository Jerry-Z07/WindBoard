using System;
using Windows.Graphics;
using WindBoard.Features.ScreenAnnotation.Interop;
using WindBoard.Features.ScreenAnnotation.Models;

namespace WindBoard.Features.ScreenAnnotation.Services
{
    /// <summary>
    /// 解析屏幕批注的目标显示器。
    /// </summary>
    internal sealed class ScreenAnnotationDisplayResolver
    {
        internal ScreenAnnotationDisplayTarget Resolve(IntPtr ownerHwnd)
        {
            if (!ScreenAnnotationWindowInterop.TryGetMonitorBounds(
                ownerHwnd,
                out RectInt32 bounds,
                out RectInt32 workArea,
                out nint monitorHandle,
                out string? error))
            {
                throw new InvalidOperationException(error ?? "Unable to resolve display target.");
            }

            return new ScreenAnnotationDisplayTarget(
                MonitorHandle: monitorHandle,
                Bounds: bounds,
                WorkArea: workArea);
        }
    }
}
