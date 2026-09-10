using FlaUI.Core.AutomationElements;
using WindBoard.UITests.Infrastructure;

namespace WindBoard.UITests
{
    /// <summary>
    /// 场景 2（设置持久化）：修改设置项 → 关闭并重启应用 → 断言值保留。
    /// 断言对象：常规页“最小化进入屏幕批注”开关（SettingsPage_EnterScreenAnnotationToggle）。
    /// </summary>
    [Trait("Category", "E2E")]
    public sealed class SettingsPersistenceTests : UiTestBase
    {
        private const string ToggleId = "SettingsPage_EnterScreenAnnotationToggle";

        [Fact]
        public void ToggleGeneralSwitch_PersistsAcrossAppRestart()
        {
            RunStep("打开设置窗口并进入常规页", () =>
            {
                OpenGeneralSettings();
            });

            bool targetState = false;

            RunStep("切换“最小化进入屏幕批注”开关到相反状态", () =>
            {
                Window settings = OpenGeneralSettings();
                AutomationElement toggle = ScenarioSteps.WaitForInWindow(App, settings, ToggleId);

                targetState = !UiInteraction.GetToggleState(toggle);
                UiInteraction.SetToggle(toggle, targetState);
            });

            RunStep("等待设置落盘（settings.json 出现）", () =>
            {
                // 干净环境下应用启动不写 settings.json；首次修改经 350ms 防抖后创建文件，
                // 轮询文件出现作为“已触发保存”的信号。
                UiWait.ForCondition(
                    SettingsBackup.SettingsFileExists,
                    "settings.json 落盘",
                    timeout: System.TimeSpan.FromSeconds(5));
            });

            RunStep("重启应用", () =>
            {
                RestartApp();
            });

            RunStep("重启后断言开关状态保留", () =>
            {
                Window settings = OpenGeneralSettings();
                AutomationElement toggle = ScenarioSteps.WaitForInWindow(App, settings, ToggleId);
                Assert.Equal(targetState, UiInteraction.GetToggleState(toggle));

                // 恢复初始状态，减少对后续用例的隐式依赖（每用例本就有设置备份兜底）。
                UiInteraction.SetToggle(toggle, !targetState);
            });
        }

        private Window OpenGeneralSettings()
        {
            Window settings = ScenarioSteps.OpenSettingsWindow(App);
            ScenarioSteps.NavigateSettingsPage(App, settings, "SettingsWindow_Nav_General");
            return settings;
        }
    }
}
