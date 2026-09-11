using System;
using System.IO;
using WindBoard.UITests.Infrastructure;

namespace WindBoard.UITests
{
    /// <summary>
    /// 场景 6（导入 WBIX）：经打开对话框选择临时目录中的 2 页测试文件 →
    /// 断言导入成功（页面追加：页切换控件出现、页码文本变为 1 / 3）。
    /// </summary>
    [Trait("Category", "E2E")]
    public sealed class ImportWbixTests : UiTestBase
    {
        [Fact]
        public void ImportWbix_AppendsPages()
        {
            string wbixPath = string.Empty;

            RunStep("构造 2 页 WBIX 测试文件", () =>
            {
                wbixPath = WbixTestFileFactory.CreateWbix(Temp.RootDirectory, pageCount: 2);
            });

            RunStep("初始单页：页切换控件不可见", () =>
            {
                // 只有 1 页时右侧 Dock 仅显示“+”，页切换三件套 Collapsed（UpdatePageNavigator）。
                UiWait.ForCondition(
                    () => ScenarioSteps.FindVisibleNow(App, "Pages_NextButton") is null,
                    "页切换按钮隐藏",
                    timeout: TimeSpan.FromSeconds(3));
            });

            RunStep("点击导入按钮", () =>
            {
                // 系统打开对话框只在宿主窗口激活时显示：触发前强制前置主窗口。
                UiInteraction.BringToFront(App.MainWindow);

                var importButton = ScenarioSteps.WaitForInMainWindow(App, "Dock_ImportButton");
                UiInteraction.InvokeOrClick(importButton);
            });

            RunStep("系统打开对话框选择 WBIX 文件", () =>
            {
                ScenarioSteps.CompleteFileDialog(App, wbixPath, save: false, Artifacts);
            });

            RunStep("导入成功：页面追加为 3 页", () =>
            {
                // 导入包含 busy 弹窗与文件解析：给足等待时间。
                var nextButton = UiWait.ForElement(
                    () => ScenarioSteps.FindVisibleNow(App, "Pages_NextButton"),
                    "页切换按钮出现（多页）",
                    timeout: TimeSpan.FromSeconds(15));
                Assert.False(nextButton.Properties.IsOffscreen.ValueOrDefault);
            });

            RunStep("页码文本显示总页数为 3", () =>
            {
                // 追加导入后当前页会切到新导入的第一页（“2 / 3”），这里只断言总页数。
                var indicatorText = ScenarioSteps.WaitForInMainWindow(App, "Pages_IndicatorText", TimeSpan.FromSeconds(5));
                Assert.True(
                    (indicatorText.Name ?? string.Empty).EndsWith("/ 3", StringComparison.Ordinal),
                    $"页码文本应显示总页数 3，实际为 '{indicatorText.Name}'");
            });
        }
    }
}
