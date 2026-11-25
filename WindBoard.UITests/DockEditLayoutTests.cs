using System.Drawing;
using FlaUI.Core.AutomationElements;
using WindBoard.UITests.Infrastructure;

namespace WindBoard.UITests
{
    /// <summary>
    /// Dock 编辑层的布局回归：左侧内置组必须贴左、页面组必须贴右，不能互相覆盖。
    /// </summary>
    [Trait("Category", "E2E")]
    public sealed class DockEditLayoutTests : UiTestBase
    {
        [Fact]
        public void EditMode_LeftAndPagesGroups_DoNotOverlap()
        {
            RunStep("设置 → 外观 → Dock 设置 → 编辑 Dock", () =>
            {
                ScenarioSteps.EnterDockEditMode(App);
            });

            RunStep("左组、主组与页面组依次排列且不重叠", () =>
            {
                AutomationElement left = ScenarioSteps.WaitForInMainWindow(App, "Dock_Edit_LeftItemsList");
                AutomationElement tools = ScenarioSteps.WaitForInMainWindow(App, "Dock_Edit_ToolsList");
                AutomationElement undoRedo = ScenarioSteps.WaitForInMainWindow(App, "Dock_Edit_UndoRedoList");
                AutomationElement pages = ScenarioSteps.WaitForInMainWindow(App, "Dock_Edit_PagesList");

                Rectangle leftRect = left.Properties.BoundingRectangle.Value;
                Rectangle toolsRect = tools.Properties.BoundingRectangle.Value;
                Rectangle undoRedoRect = undoRedo.Properties.BoundingRectangle.Value;
                Rectangle pagesRect = pages.Properties.BoundingRectangle.Value;

                Assert.True(
                    leftRect.Right <= toolsRect.Left,
                    $"左侧 Dock 组 {leftRect} 与主 Dock 工具组 {toolsRect} 重叠。");
                Assert.True(
                    undoRedoRect.Right <= pagesRect.Left,
                    $"主 Dock 撤销/重做组 {undoRedoRect} 与页面 Dock 组 {pagesRect} 重叠。");
            });

            RunStep("点击完成", () =>
            {
                ScenarioSteps.ExitDockEditMode(App);
            });
        }
    }
}
