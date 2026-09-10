using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using WindBoard.UITests.Infrastructure;

namespace WindBoard.UITests
{
    /// <summary>
    /// E2E 用例基类：设置隔离 → 启动应用；用例结束恢复环境。
    /// 每个测试方法一个实例（xUnit IAsyncLifetime），保证用例间互不残留。
    /// 默认通过构建开关隔离（csproj：RunUITests=true 才作为测试工程参与 dotnet test）。
    /// 失败截图通过 <see cref="RunStep"/> 在异常点捕获（比 Dispose 兜底更贴近现场）。
    /// </summary>
    public abstract class UiTestBase : IAsyncLifetime
    {
        protected WindBoardApp App { get; private set; } = null!;

        protected TempWorkspace Temp { get; private set; } = null!;

        protected TestArtifacts Artifacts { get; private set; } = null!;

        public Task InitializeAsync()
        {
            Artifacts = new TestArtifacts(GetType().Name);
            EnsureRunnable();

            Artifacts.Step("环境隔离：备份并移除 settings.json");
            SettingsBackup.SaveAndClear();

            Temp = new TempWorkspace();
            Artifacts.Step($"被测程序：{RepoRootLocator.TryFindAppExe()}");
            Artifacts.Step($"启动应用（临时目录：{Temp.RootDirectory}）");
            App = WindBoardApp.Start();
            Artifacts.Step("应用已启动，主窗口就绪");
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            try
            {
                App?.Dispose();
            }
            catch (Exception ex)
            {
                Artifacts?.Step($"应用清理异常（忽略）：{ex.Message}");
            }

            SettingsBackup.Restore();
            Temp?.Dispose();
            return Task.CompletedTask;
        }

        /// <summary>
        /// 以“步骤”为单位执行用例逻辑：记录步骤日志；异常时保存截图并原样抛出。
        /// </summary>
        protected void RunStep(string name, Action action)
        {
            Artifacts.Step($"▶ {name}");
            try
            {
                action();
                Artifacts.Step($"✔ {name}");
            }
            catch (Exception ex)
            {
                Artifacts.Step($"✘ {name}：{ex.GetType().Name}: {ex.Message}");
                Artifacts.CaptureFailureScreenshot(Sanitize(name));
                throw;
            }
        }

        /// <summary>正常退出当前实例并重新启动（用于设置持久化等跨进程断言）。</summary>
        protected void RestartApp()
        {
            Artifacts.Step("重启应用");
            App.Dispose();
            App = WindBoardApp.Start();
            Artifacts.Step("应用已重启，主窗口就绪");
        }

        private void EnsureRunnable()
        {
            // E2E 的“默认关闭”由构建开关保证（IsTestProject 条件化，见 csproj）；
            // 走到这里说明调用方已显式启用 E2E，环境缺失应直接失败并给出可读信息。
            if (RepoRootLocator.TryFindAppExe() is null)
            {
                throw new InvalidOperationException(
                    "未找到 WindBoard.exe：请先构建主工程（dotnet build WindBoard.slnx -c Release），或用 WINDBOARD_EXE 指定路径。");
            }
        }

        private static string Sanitize(string name)
        {
            var builder = new StringBuilder(name);
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                builder.Replace(c, '_');
            }

            return builder.ToString();
        }
    }
}
