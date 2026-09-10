using WindBoard.UITests.Infrastructure;

namespace WindBoard.UITests
{
    /// <summary>
    /// 场景 1（启动冒烟）：启动进程 → 主窗口出现 → 关键元素存在 → 正常退出。
    /// </summary>
    [Trait("Category", "E2E")]
    public sealed class SmokeTests : UiTestBase
    {
        [Fact]
        public void Launch_ShowsMainWindowWithKeyDockElements_ThenExitsCleanly()
        {
            RunStep("主窗口关键 Dock 元素存在（更多/导入）", () =>
            {
                UiWait.ForElement(
                    () => App.MainWindow.FindFirstDescendant(App.Cf.ByAutomationId("Dock_MoreButton")),
                    "“更多”按钮（Dock_MoreButton）");
                UiWait.ForElement(
                    () => App.MainWindow.FindFirstDescendant(App.Cf.ByAutomationId("Dock_ImportButton")),
                    "“导入”按钮（Dock_ImportButton）");
            });

            RunStep("正常退出：关闭主窗口后进程退出", () =>
            {
                App.Quit();
                UiWait.ForCondition(
                    () => App.HasExited,
                    "应用进程退出",
                    timeout: System.TimeSpan.FromSeconds(5));
            });
        }
    }
}
