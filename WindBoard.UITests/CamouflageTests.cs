using FlaUI.Core.AutomationElements;
using WindBoard.UITests.Infrastructure;

namespace WindBoard.UITests
{
    /// <summary>
    /// 场景 4（伪装模式）：进入伪装态（改标题）→ 断言主窗口标题生效 → 退出恢复。
    /// 伪装实现为“窗口标题/图标替换”（CamouflageFlow 响应设置变更立即应用）。
    /// 注意：伪装页经 Frame.Navigate 进入，导航栏选中项仍停留在“常规”，
    /// 对已选中项 Select() 不会重新导航——因此全程复用同一设置窗口与页面引用。
    /// </summary>
    [Trait("Category", "E2E")]
    public sealed class CamouflageTests : UiTestBase
    {
        [Fact]
        public void EnableCamouflageTitle_AppliesAndRestores()
        {
            string camouflageTitle = $"E2E伪装{Guid.NewGuid():N}".Substring(0, 14);
            string initialTitle = App.MainWindow.Title;
            Artifacts.Step($"初始主窗口标题：'{initialTitle}'");

            Window? settings = null;

            RunStep("打开设置 → 常规 → 伪装设置页", () =>
            {
                settings = ScenarioSteps.OpenSettingsWindow(App);
                ScenarioSteps.NavigateSettingsPage(App, settings, "SettingsWindow_Nav_General");
                ScenarioSteps.ClickCard(App, settings, "SettingsPage_CamouflageCard");
            });

            RunStep("开启伪装并写入窗口标题", () =>
            {
                AutomationElement enabledToggle = ScenarioSteps.WaitForInWindow(App, settings!, "Camouflage_EnabledToggle");

                UiInteraction.SetToggle(enabledToggle, targetOn: true);
                Artifacts.Step($"开关切换后状态：{UiInteraction.GetToggleState(enabledToggle)}");

                // OptionsPanel 在开关打开后展示（SyncUiFromSettings 异步刷新），等待标题输入框出现。
                AutomationElement titleBox = ScenarioSteps.WaitForInWindow(App, settings!, "Camouflage_TitleTextBox");
                UiInteraction.SetValue(titleBox, camouflageTitle);
            });

            RunStep("主窗口标题变为伪装标题", () =>
            {
                UiWait.ForCondition(
                    () => App.MainWindow.Title == camouflageTitle,
                    $"主窗口标题变为 '{camouflageTitle}'",
                    timeout: System.TimeSpan.FromSeconds(5));
            });

            RunStep("关闭伪装后标题恢复", () =>
            {
                AutomationElement enabledToggle = ScenarioSteps.WaitForInWindow(App, settings!, "Camouflage_EnabledToggle");
                UiInteraction.SetToggle(enabledToggle, targetOn: false);

                // 退出伪装 = 恢复应用默认标题（CamouflageService 用 AppDisplayName 回退）。
                UiWait.ForCondition(
                    () => App.MainWindow.Title == initialTitle,
                    $"主窗口标题恢复为 '{initialTitle}'",
                    timeout: System.TimeSpan.FromSeconds(5));
            });
        }
    }
}
