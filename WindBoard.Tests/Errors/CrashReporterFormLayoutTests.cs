using WindBoard.CrashReporter;

namespace WindBoard.Tests.Errors;

public sealed class CrashReporterFormLayoutTests
{
    [Fact]
    public void BuildSummaryText_RendersFourFieldLines_InFixedOrder()
    {
        var args = new CrashReporterArgs
        {
            ExceptionType = "System.InvalidOperationException",
            ExceptionMessage = "boom",
            OccurredAt = "2026-09-23T10:00:00.0000000+08:00",
            Source = "WinUIUnhandledException",
        };

        string text = CrashReporterForm.BuildSummaryText(args);

        string[] lines = SplitLines(text);
        Assert.Equal(4, lines.Length);

        Assert.StartsWith("异常类型：", lines[0], StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException", lines[0], StringComparison.Ordinal);

        Assert.StartsWith("异常消息：", lines[1], StringComparison.Ordinal);
        Assert.Contains("boom", lines[1], StringComparison.Ordinal);

        Assert.StartsWith("发生时间：", lines[2], StringComparison.Ordinal);
        Assert.Contains("2026-09-23T10:00:00.0000000+08:00", lines[2], StringComparison.Ordinal);

        Assert.StartsWith("崩溃来源：", lines[3], StringComparison.Ordinal);
        Assert.Contains("WinUIUnhandledException", lines[3], StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSummaryText_WithEmptyFields_UsesUnknownPlaceholder()
    {
        string text = CrashReporterForm.BuildSummaryText(new CrashReporterArgs());

        string[] lines = SplitLines(text);
        Assert.Equal(4, lines.Length);
        Assert.All(lines, line => Assert.Contains("(unknown)", line, StringComparison.Ordinal));
    }

    [Fact]
    public void BuildFallbackDiagnosticsText_IncludesSummaryFieldsAndPaths()
    {
        var args = new CrashReporterArgs
        {
            ReportPath = @"C:\Temp\crash.txt",
            LogsDirectory = @"C:\Temp\Logs",
            Source = "AppDomainUnhandledException",
            OccurredAt = "2026-09-23T10:00:00.0000000+08:00",
            ExceptionType = "System.InvalidOperationException",
            ExceptionMessage = "boom",
        };

        string text = CrashReporterForm.BuildFallbackDiagnosticsText(args);

        Assert.Contains("source='AppDomainUnhandledException'", text, StringComparison.Ordinal);
        Assert.Contains(@"report='C:\Temp\crash.txt'", text, StringComparison.Ordinal);
        Assert.Contains(@"logsDir='C:\Temp\Logs'", text, StringComparison.Ordinal);
        Assert.Contains("occurredAt='2026-09-23T10:00:00.0000000+08:00'", text, StringComparison.Ordinal);
        Assert.Contains("exceptionType='System.InvalidOperationException'", text, StringComparison.Ordinal);
        Assert.Contains("exceptionMessage='boom'", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFallbackDiagnosticsText_WithEmptyFields_KeepsKeyValueShape()
    {
        string text = CrashReporterForm.BuildFallbackDiagnosticsText(new CrashReporterArgs());

        // 值为空也必须保留键：避免用户复制到一段没有任何定位信息的兜底文本。
        Assert.Contains("source=''", text, StringComparison.Ordinal);
        Assert.Contains("report=''", text, StringComparison.Ordinal);
        Assert.Contains("logsDir=''", text, StringComparison.Ordinal);
        Assert.Contains("occurredAt=''", text, StringComparison.Ordinal);
        Assert.Contains("exceptionType=''", text, StringComparison.Ordinal);
        Assert.Contains("exceptionMessage=''", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CrashReporterProject_DeclaresPerMonitorV2HighDpiMode()
    {
        DirectoryInfo? repoRoot = RepoRootLocator.Find();
        Assert.NotNull(repoRoot);

        string projectDirectory = Path.Combine(repoRoot!.FullName, "WindBoard.CrashReporter");
        string projectFilePath = Path.Combine(projectDirectory, "WindBoard.CrashReporter.csproj");
        Assert.True(File.Exists(projectFilePath), $"未找到崩溃提示工程文件：{projectFilePath}");

        string text = File.ReadAllText(projectFilePath);
        Assert.Contains("<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>", text, StringComparison.Ordinal);

        // 官方不建议用 app.manifest 配置 DPI（与应用配置冲突且触发 WFO0003），此处固化为回归断言。
        Assert.False(
            File.Exists(Path.Combine(projectDirectory, "app.manifest")),
            "不应新增 app.manifest：DPI 感知模式须经项目文件配置。");
    }

    private static string[] SplitLines(string text)
    {
        // 摘要固定 4 行；RemoveEmptyEntries 用于忽略行尾换行产生的空条目。
        return text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }
}
