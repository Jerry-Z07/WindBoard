using WindBoard.Errors;

namespace WindBoard.Tests.Errors;

/// <summary>
/// 覆盖 <see cref="AppErrorService.NormalizeSingleLine"/>：崩溃链路传给崩溃窗口的摘要参数必须
/// 在进程调用边界上完成「换行折叠 + 截断」，且任何输入都不得抛异常（R7）。
/// </summary>
public sealed class AppErrorServiceTests
{
    // 说明：对应 AppErrorService.MaxExceptionMessageChars（见 design.md §2.2）。
    // 此处用字面量固化契约：上限调整时该测试必须同步评审。
    private const int NormalizedMessageCharLimit = 2000;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NormalizeSingleLine_WithEmptyMessage_ReturnsEmpty(string? message)
    {
        Assert.Equal(string.Empty, AppErrorService.NormalizeSingleLine(message));
    }

    [Theory]
    [InlineData("a\r\nb", "a b")]
    [InlineData("a\nb", "a b")]
    [InlineData("a\rb", "a b")]
    [InlineData("  首行\r\n次行  ", "首行 次行")]
    public void NormalizeSingleLine_CollapsesLineBreaks_AndTrims(string message, string expected)
    {
        // 崩溃窗口按「异常消息：<单行>」排版，消息内换行会破坏该结构（design.md §2.2）。
        Assert.Equal(expected, AppErrorService.NormalizeSingleLine(message));
    }

    [Fact]
    public void NormalizeSingleLine_AtCharLimit_KeepsMessageUnchanged()
    {
        string message = new string('a', NormalizedMessageCharLimit);

        Assert.Equal(message, AppErrorService.NormalizeSingleLine(message));
    }

    [Fact]
    public void NormalizeSingleLine_BeyondCharLimit_TruncatesToLimit()
    {
        string message = new string('a', NormalizedMessageCharLimit + 500);

        string normalized = AppErrorService.NormalizeSingleLine(message);

        Assert.Equal(NormalizedMessageCharLimit, normalized.Length);
        Assert.Equal(message[..NormalizedMessageCharLimit], normalized);
    }
}
