using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace WindBoard.UITests.Infrastructure
{
    /// <summary>
    /// 测试进程初始化：在加载时（早于任何 UIA COM 调用与窗口创建）设置 DPI 感知。
    /// 背景：testhost 默认非 DPI-aware，在高 DPI（如 150%）下 UIA BoundingRectangle
    /// 返回虚拟化坐标，而 FlaUI Mouse（SendInput）使用物理像素坐标，导致鼠标点击
    /// 系统性偏移、ContentDialog 按钮等依赖坐标的交互全部落空。
    /// </summary>
    internal static class TestProcessInitializer
    {
        private const nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(nint value);

        [ModuleInitializer]
        internal static void Initialize()
        {
            try
            {
                _ = SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            }
            catch
            {
                // 设置失败（如已被隐式设置）：坐标偏移问题可能在后续步骤暴露，交由用例失败诊断。
            }
        }
    }
}
