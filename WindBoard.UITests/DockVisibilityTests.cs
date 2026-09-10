using FlaUI.Core.AutomationElements;
using WindBoard.UITests.Infrastructure;

namespace WindBoard.UITests
{
    /// <summary>
    /// 场景 3（Dock 显隐）：切换“显示撤销/重做”开关并断言主窗口状态。
    /// 断言锚点：Dock_UndoButton（撤销/重做面板隐藏时从 UIA 树消失，显示后回归可见）。
    /// 全程复用同一设置窗口与 Dock 设置页引用（见 CamouflageTests 的导航说明）。
    /// </summary>
    [Trait("Category", "E2E")]
    public sealed class DockVisibilityTests : UiTestBase
    {
        [Fact]
        public void ToggleUndoRedoVisibility_UpdatesMainWindowDock()
        {
            Window? settings = null;

            RunStep("打开设置 → 外观 → Dock 设置页", () =>
            {
                settings = ScenarioSteps.OpenSettingsWindow(App);
                ScenarioSteps.NavigateSettingsPage(App, settings, "SettingsWindow_Nav_Appearance");
                ScenarioSteps.ClickCard(App, settings, "Appearance_DockSettingsCard");
            });

            RunStep("关闭“显示撤销/重做”开关", () =>
            {
                AutomationElement toggle = ScenarioSteps.WaitForInWindow(App, settings!, "Dock_UndoRedoVisibleToggle");
                UiInteraction.SetToggle(toggle, targetOn: false);
            });

            RunStep("主窗口撤销按钮从界面消失", () =>
            {
                // 隐藏后 WinUI 将面板从 UIA 树移除：断言“找不到可见元素”。
                UiWait.ForCondition(
                    () => ScenarioSteps.FindVisibleNow(App, "Dock_UndoButton") is null,
                    "撤销按钮隐藏",
                    timeout: System.TimeSpan.FromSeconds(5));
            });

            RunStep("重新打开“显示撤销/重做”开关", () =>
            {
                AutomationElement toggle = ScenarioSteps.WaitForInWindow(App, settings!, "Dock_UndoRedoVisibleToggle");
                UiInteraction.SetToggle(toggle, targetOn: true);
            });

            RunStep("主窗口撤销按钮恢复可见", () =>
            {
                AutomationElement undo = UiWait.ForElement(
                    () => ScenarioSteps.FindVisibleNow(App, "Dock_UndoButton"),
                    "撤销按钮恢复可见",
                    timeout: System.TimeSpan.FromSeconds(5));
                Assert.False(undo.Properties.IsOffscreen.ValueOrDefault);
            });
        }
    }
}
