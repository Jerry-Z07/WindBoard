using System;
using System.Globalization;
using System.IO;
using System.Text;
using FlaUI.Core.Capturing;

namespace WindBoard.UITests.Infrastructure
{
    /// <summary>
    /// 用例级诊断工件：步骤日志（steps.log）+ 失败截图（全屏 png）。
    /// 产物位置：{repo}\TestResults\uitests-artifacts\{类名}-{时间戳}\。
    /// </summary>
    public sealed class TestArtifacts
    {
        private readonly string _directory;
        private readonly StringBuilder _steps = new();

        public TestArtifacts(string testCaseName)
        {
            string? repoRoot = FindRepoRoot();
            _directory = Path.Combine(
                repoRoot ?? AppContext.BaseDirectory,
                "TestResults",
                "uitests-artifacts",
                $"{testCaseName}-{DateTime.Now:yyyyMMdd-HHmmss-fff}");
            Directory.CreateDirectory(_directory);
        }

        /// <summary>记录一步操作/断言（用例内按序调用）。</summary>
        public void Step(string message)
        {
            _steps.AppendLine(CultureInfo.InvariantCulture, $"[{DateTime.Now:HH:mm:ss.fff}] {message}");
            FlushSteps();
        }

        /// <summary>失败截图（尽力而为，不抛异常）。</summary>
        public void CaptureFailureScreenshot(string name)
        {
            try
            {
                string path = Path.Combine(_directory, $"{name}.png");
                using var image = Capture.Screen();
                image.ToFile(path);
                _steps.AppendLine(CultureInfo.InvariantCulture, $"[{DateTime.Now:HH:mm:ss.fff}] 已保存失败截图：{path}");
            }
            catch (Exception ex)
            {
                _steps.AppendLine(CultureInfo.InvariantCulture, $"[{DateTime.Now:HH:mm:ss.fff}] 截图失败：{ex.Message}");
            }
            finally
            {
                FlushSteps();
            }
        }

        private void FlushSteps()
        {
            try
            {
                File.WriteAllText(Path.Combine(_directory, "steps.log"), _steps.ToString());
            }
            catch
            {
                // 日志落盘失败不影响用例执行。
            }
        }

        private static string? FindRepoRoot()
        {
            DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
            for (int depth = 0; current is not null && depth < 8; depth++, current = current.Parent)
            {
                if (File.Exists(Path.Combine(current.FullName, "WindBoard.slnx")))
                {
                    return current.FullName;
                }
            }

            return null;
        }
    }
}
