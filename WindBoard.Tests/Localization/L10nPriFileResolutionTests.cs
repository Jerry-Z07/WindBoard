using WindBoard.Localization;

namespace WindBoard.Tests.Localization;

public sealed class L10nPriFileResolutionTests : IDisposable
{
    private readonly string _tempRoot;

    public L10nPriFileResolutionTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "WindBoard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // 忽略：测试清理失败不应影响结果
        }
    }

    [Fact]
    public void ResolvePriFileName_ReturnsAppPriFileName_WhenWindBoardPriExists()
    {
        File.WriteAllText(Path.Combine(_tempRoot, "WindBoard.pri"), "pri");

        Assert.Equal("WindBoard.pri", L10n.ResolvePriFileName(_tempRoot));
    }

    [Fact]
    public void ResolvePriFileName_ReturnsNull_WhenWindBoardPriMissing()
    {
        Assert.Null(L10n.ResolvePriFileName(_tempRoot));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolvePriFileName_ReturnsNull_WhenBaseDirectoryIsBlank(string baseDirectory)
    {
        Assert.Null(L10n.ResolvePriFileName(baseDirectory));
    }
}
