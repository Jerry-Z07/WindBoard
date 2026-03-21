using System;
using Microsoft.UI.Xaml;

namespace WindBoard.Features.ScreenAnnotation.Models
{
    /// <summary>
    /// 屏幕批注启动参数。
    /// </summary>
    internal sealed class ScreenAnnotationStartOptions
    {
        /// <summary>
        /// 主窗口引用：用于退出桌面模式后恢复和激活。
        /// </summary>
        internal required Window OwnerWindow { get; init; }

        /// <summary>
        /// 主窗口句柄。
        /// </summary>
        internal IntPtr OwnerHwnd { get; init; }

        /// <summary>
        /// 是否在进入桌面模式后最小化主窗口。
        /// </summary>
        internal bool MinimizeOwnerWindow { get; init; } = true;

        /// <summary>
        /// 启动来源：用于日志排查。
        /// </summary>
        internal string Source { get; init; } = "unknown";
    }
}
