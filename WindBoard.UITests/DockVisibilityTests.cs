using System;
using FlaUI.Core.AutomationElements;
using WindBoard.UITests.Infrastructure;

namespace WindBoard.UITests
{
    /// <summary>
    /// 场景 3（Dock 显隐与隐藏项）：通过“设置 → 外观 → Dock 设置 → 编辑 Dock”修改内置项显隐，并断言主窗口状态。
    /// 断言锚点：Dock_UndoButton / Dock_ImportButton（隐藏时从 UIA 树消失，恢复后回归可见）。
    /// </summary>
    [Trait("Category", "E2E")]
    public sealed class DockVisibilityTests : UiTestBase
    {
        [Fact]
        public void HideAndRestoreUndoRedoItems_UpdatesMainWindowDock()
        {
            RunStep("设置 → 外观 → Dock 设置 → 编辑 Dock", () =>
            {
                ScenarioSteps.EnterDockEditMode(App);
            });

            RunStep("逐条隐藏撤销与重做", () =>
            {
                UiInteraction.InvokeOrClick(ScenarioSteps.WaitForInMainWindow(App, "Dock_Edit_Hide_undo"));
                UiInteraction.InvokeOrClick(ScenarioSteps.WaitForInMainWindow(App, "Dock_Edit_Hide_redo"));
            });

            RunStep("点击完成", () =>
            {
                ScenarioSteps.ExitDockEditMode(App);
            });

            RunStep("主窗口撤销/重做按钮从界面消失", () =>
            {
                UiWait.ForCondition(
                    () => ScenarioSteps.FindVisibleNow(App, "Dock_UndoButton") is null
                        && ScenarioSteps.FindVisibleNow(App, "Dock_RedoButton") is null,
                    "撤销/重做按钮隐藏",
                    timeout: TimeSpan.FromSeconds(5));
            });

            RunStep("重新进入编辑模式", () =>
            {
                ScenarioSteps.EnterDockEditMode(App);
            });

            RunStep("逐条恢复撤销与重做", () =>
            {
                UiInteraction.InvokeOrClick(ScenarioSteps.WaitForInMainWindow(App, "Dock_Edit_Restore_undo"));
                UiInteraction.InvokeOrClick(ScenarioSteps.WaitForInMainWindow(App, "Dock_Edit_Restore_redo"));
            });

            RunStep("点击完成", () =>
            {
                ScenarioSteps.ExitDockEditMode(App);
            });

            RunStep("主窗口撤销/重做按钮恢复可见", () =>
            {
                AutomationElement undo = UiWait.ForElement(
                    () => ScenarioSteps.FindVisibleNow(App, "Dock_UndoButton"),
                    "撤销按钮恢复可见",
                    timeout: TimeSpan.FromSeconds(5));
                Assert.False(undo.Properties.IsOffscreen.ValueOrDefault);

                AutomationElement redo = UiWait.ForElement(
                    () => ScenarioSteps.FindVisibleNow(App, "Dock_RedoButton"),
                    "重做按钮恢复可见",
                    timeout: TimeSpan.FromSeconds(5));
                Assert.False(redo.Properties.IsOffscreen.ValueOrDefault);
            });
        }

        [Fact]
        public void HideAndRestoreImportItem_UpdatesMainWindowDock()
        {
            RunStep("设置 → 外观 → Dock 设置 → 编辑 Dock", () =>
            {
                ScenarioSteps.EnterDockEditMode(App);
            });

            RunStep("隐藏“导入”项", () =>
            {
                AutomationElement hide = ScenarioSteps.WaitForInMainWindow(App, "Dock_Edit_Hide_import");
                UiInteraction.InvokeOrClick(hide);
            });

            RunStep("点击完成", () =>
            {
                ScenarioSteps.ExitDockEditMode(App);
            });

            RunStep("主窗口导入按钮从界面消失", () =>
            {
                UiWait.ForCondition(
                    () => ScenarioSteps.FindVisibleNow(App, "Dock_ImportButton") is null,
                    "导入按钮隐藏",
                    timeout: TimeSpan.FromSeconds(5));
            });

            RunStep("重新进入编辑模式", () =>
            {
                ScenarioSteps.EnterDockEditMode(App);
            });

            RunStep("恢复“导入”项", () =>
            {
                AutomationElement restore = ScenarioSteps.WaitForInMainWindow(App, "Dock_Edit_Restore_import");
                UiInteraction.InvokeOrClick(restore);
            });

            RunStep("点击完成", () =>
            {
                ScenarioSteps.ExitDockEditMode(App);
            });

            RunStep("主窗口导入按钮恢复可见", () =>
            {
                AutomationElement import = UiWait.ForElement(
                    () => ScenarioSteps.FindVisibleNow(App, "Dock_ImportButton"),
                    "导入按钮恢复可见",
                    timeout: TimeSpan.FromSeconds(5));
                Assert.False(import.Properties.IsOffscreen.ValueOrDefault);
            });
        }
    }
}
