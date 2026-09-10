using System;
using System.IO;
using System.Linq;
using WindBoard.UITests.Infrastructure;

namespace WindBoard.UITests
{
    /// <summary>
    /// 场景 5（导出 PNG）：更多菜单 → 导出 → 默认选项（PNG/当前页）→
    /// 经系统保存对话框写入临时目录 → 断言文件生成。
    /// </summary>
    [Trait("Category", "E2E")]
    public sealed class ExportPngTests : UiTestBase
    {
        [Fact]
        public void ExportPng_WritesFileIntoTempDirectory()
        {
            // 文件名含 GUID：杜绝任何历史残留触发“确认覆盖”弹窗（IFileDialog 预创建行为依环境而异）。
            string pngPath = Path.Combine(Temp.RootDirectory, $"e2e-export-{Guid.NewGuid():N}.png");

            RunStep("打开更多菜单并点击导出", () =>
            {
                // 系统保存对话框只在宿主窗口激活时显示：触发前强制前置主窗口。
                UiInteraction.BringToFront(App.MainWindow);

                var moreButton = ScenarioSteps.WaitForInMainWindow(App, "Dock_MoreButton");
                UiInteraction.InvokeOrClick(moreButton);

                // MenuFlyout 为独立顶层弹层：全局按 AutomationId 查找（给足搜索时间）。
                var exportItem = ScenarioSteps.WaitForEverywhere(
                    App,
                    "Dock_ExportMenuItem",
                    timeout: TimeSpan.FromSeconds(20));
                UiInteraction.InvokeOrClick(exportItem);
            });

            RunStep("导出对话框确认（默认 PNG/当前页）", () =>
            {
                // ExportDialog 为代码构建的 ContentDialog，无 AutomationId：主按钮文案集中在 UiText。
                // 实测 ContentDialog 按钮的 InvokePattern 不触发应用逻辑，必须鼠标点击。
                var primaryButton = UiWait.ForElement(
                    () => ScenarioSteps.TryFindButtonEverywhere(App, UiText.ExportDialogPrimaryButton),
                    "导出对话框主按钮（下一步）");
                UiInteraction.ClickCenter(primaryButton);
            });

            RunStep("系统保存对话框写入临时路径并确认", () =>
            {
                ScenarioSteps.CompleteFileDialog(App, pngPath, save: true, Artifacts);
            });

            RunStep("若出现覆盖确认弹窗则确认（可选）", () =>
            {
                // IFileDialog 是否预创建 0 字节文件依环境而异：弹窗出现则点到消失，未出现直接跳过。
                FlaUI.Core.AutomationElements.AutomationElement? overwriteButton;
                try
                {
                    overwriteButton = UiWait.ForElement(
                        () => ScenarioSteps.TryFindOverwriteButton(App),
                        "覆盖确认弹窗按钮（可选）",
                        timeout: TimeSpan.FromSeconds(20));
                }
                catch (TimeoutException)
                {
                    Artifacts.Step("覆盖弹窗未出现（目标文件名唯一），跳过确认");
                    return;
                }

                Artifacts.Step($"覆盖按钮 rect={overwriteButton!.Properties.BoundingRectangle.Value}");

                UiWait.ForCondition(
                    () =>
                    {
                        var button = ScenarioSteps.TryFindOverwriteButton(App);
                        if (button is null)
                        {
                            return true;
                        }

                        UiInteraction.InvokeLegacyOrClick(button);
                        return false;
                    },
                    "覆盖确认完成",
                    timeout: TimeSpan.FromSeconds(15));
            });

            RunStep("断言 PNG 文件生成且非空", () =>
            {
                // 导出为异步写入：文件先创建后写入内容，轮询到“存在且非空”才算完成。
                UiWait.ForCondition(
                    () => File.Exists(pngPath) && new FileInfo(pngPath).Length > 0,
                    "导出 PNG 文件生成且非空",
                    timeout: TimeSpan.FromSeconds(15));
            });

            RunStep("关闭导出完成提示弹窗", () =>
            {
                // 完成提示为单按钮 ContentDialog（文案 Common_Close）：ContentDialog 需鼠标点击。
                var closeButton = UiWait.ForElement(
                    () => ScenarioSteps.TryFindButtonEverywhere(App, UiText.MessageBoxCloseButton),
                    "导出完成弹窗按钮",
                    timeout: TimeSpan.FromSeconds(5));
                UiInteraction.ClickCenter(closeButton);
            });
        }
    }
}
