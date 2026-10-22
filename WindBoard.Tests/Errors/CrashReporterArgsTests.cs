using WindBoard.CrashReporter;

namespace WindBoard.Tests.Errors;

public sealed class CrashReporterArgsTests
{
    // CA1861：常量数组提为 static readonly，避免重复分配。
    private static readonly string[] MissingValueArgs = { "--report" };

    // 新增参数同样要覆盖“缺值”分支（末尾 token 是参数名，其后没有值）。
    private static readonly string[] MissingSummaryValueArgs = { "--exception-type" };

    [Fact]
    public void Parse_WithKnownArgs_ParsesValues_AndIgnoresUnknown()
    {
        string[] args =
        {
            "--report", @"C:\Temp\crash 1.txt",
            "--logs-dir", @"C:\Temp\Logs",
            "--source", "WinUIUnhandledException",
            "--unknown", "x",
        };

        CrashReporterArgs parsed = CrashReporterArgs.Parse(args);

        Assert.Equal(@"C:\Temp\crash 1.txt", parsed.ReportPath);
        Assert.Equal(@"C:\Temp\Logs", parsed.LogsDirectory);
        Assert.Equal("WinUIUnhandledException", parsed.Source);
        // 旧版主程序不传新参数：新字段保持默认空值（窗口按 "(unknown)" 占位渲染）。
        Assert.Equal(string.Empty, parsed.OccurredAt);
        Assert.Equal(string.Empty, parsed.ExceptionType);
        Assert.Equal(string.Empty, parsed.ExceptionMessage);
    }

    [Fact]
    public void Parse_WithSummaryArgs_ParsesValues_InMixedOrder()
    {
        string[] args =
        {
            "--exception-message", "boom",
            "--unknown", "x",
            "--occurred-at", "2026-09-23T10:00:00.0000000+08:00",
            "--report", @"C:\Temp\crash.txt",
            "--exception-type", "System.InvalidOperationException",
        };

        CrashReporterArgs parsed = CrashReporterArgs.Parse(args);

        Assert.Equal("boom", parsed.ExceptionMessage);
        Assert.Equal("2026-09-23T10:00:00.0000000+08:00", parsed.OccurredAt);
        Assert.Equal("System.InvalidOperationException", parsed.ExceptionType);
        Assert.Equal(@"C:\Temp\crash.txt", parsed.ReportPath);
        Assert.Equal(string.Empty, parsed.LogsDirectory);
        Assert.Equal(string.Empty, parsed.Source);
    }

    [Fact]
    public void Parse_WithAllSixKeys_ParsesEveryValue()
    {
        // 真实调用形态：主程序一次性传入全部 6 个参数（见 spec/frontend/crash-reporter-ui.md §6）。
        string[] args =
        {
            "--report", @"C:\Temp\crash.txt",
            "--logs-dir", @"C:\Temp\Logs",
            "--source", "AppDomainUnhandledException",
            "--occurred-at", "2026-09-23T10:00:00.0000000+08:00",
            "--exception-type", "System.InvalidOperationException",
            "--exception-message", "boom",
        };

        CrashReporterArgs parsed = CrashReporterArgs.Parse(args);

        Assert.Equal(@"C:\Temp\crash.txt", parsed.ReportPath);
        Assert.Equal(@"C:\Temp\Logs", parsed.LogsDirectory);
        Assert.Equal("AppDomainUnhandledException", parsed.Source);
        Assert.Equal("2026-09-23T10:00:00.0000000+08:00", parsed.OccurredAt);
        Assert.Equal("System.InvalidOperationException", parsed.ExceptionType);
        Assert.Equal("boom", parsed.ExceptionMessage);
    }

    [Fact]
    public void Parse_WithMissingSummaryValue_DoesNotThrow_AndKeepsDefaults()
    {
        CrashReporterArgs parsed = CrashReporterArgs.Parse(MissingSummaryValueArgs);

        Assert.NotNull(parsed);
        Assert.Equal(string.Empty, parsed.ExceptionType);
        Assert.Equal(string.Empty, parsed.OccurredAt);
        Assert.Equal(string.Empty, parsed.ExceptionMessage);
    }

    [Fact]
    public void Parse_WithMissingValue_DoesNotThrow_AndKeepsDefaults()
    {
        CrashReporterArgs parsed = CrashReporterArgs.Parse(MissingValueArgs);

        Assert.NotNull(parsed);
        Assert.Equal(string.Empty, parsed.ReportPath);
        Assert.Equal(string.Empty, parsed.LogsDirectory);
        Assert.Equal(string.Empty, parsed.Source);
    }

    [Fact]
    public void Parse_WithNull_ReturnsDefaults()
    {
        CrashReporterArgs parsed = CrashReporterArgs.Parse(null!);

        Assert.NotNull(parsed);
        Assert.Equal(string.Empty, parsed.ReportPath);
        Assert.Equal(string.Empty, parsed.LogsDirectory);
        Assert.Equal(string.Empty, parsed.Source);
        Assert.Equal(string.Empty, parsed.OccurredAt);
        Assert.Equal(string.Empty, parsed.ExceptionType);
        Assert.Equal(string.Empty, parsed.ExceptionMessage);
    }
}
