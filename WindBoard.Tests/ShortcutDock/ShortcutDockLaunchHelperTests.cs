using System;
using WindBoard.ShortcutDock;
using Xunit;

namespace WindBoard.Tests.ShortcutDock;

public sealed class ShortcutDockLaunchHelperTests
{
    [Fact]
    public void NormalizeInput_TrimsUnquotesAndExpandsEnvironmentVariables()
    {
        try
        {
            Environment.SetEnvironmentVariable("WB_TEST_VAR", @"C:\Temp");

            string result = ShortcutDockLaunchHelper.NormalizeInput("  \"%WB_TEST_VAR%\\a.txt\"  ");

            Assert.Equal(@"C:\Temp\a.txt", result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WB_TEST_VAR", null);
        }
    }

    [Theory]
    [InlineData("example.com", "https", "example.com")]
    [InlineData("example.com/path", "https", "example.com")]
    [InlineData("http://example.com", "http", "example.com")]
    [InlineData("https://example.com/a/b", "https", "example.com")]
    public void TryNormalizeLinkUri_AddsHttpsWhenMissingScheme(string input, string expectedScheme, string expectedHost)
    {
        bool ok = ShortcutDockLaunchHelper.TryNormalizeLinkUri(input, out Uri? uri);

        Assert.True(ok);
        Assert.NotNull(uri);
        Assert.Equal(expectedScheme, uri!.Scheme);
        Assert.Equal(expectedHost, uri.Host);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\MyApp\\app.exe\" --foo bar", "", @"C:\Program Files\MyApp\app.exe", "--foo bar")]
    [InlineData("C:\\Program Files\\MyApp\\app.exe --foo", "", @"C:\Program Files\MyApp\app.exe", "--foo")]
    [InlineData("C:\\Program Files\\MyApp\\app.exe", "--foo", @"C:\Program Files\MyApp\app.exe", "--foo")]
    [InlineData("\"C:\\Program Files\\MyApp\\app.exe\" --foo", "--bar", @"C:\Program Files\MyApp\app.exe", "--bar")]
    public void NormalizeProgramLaunch_SplitsInlineArguments(
        string input,
        string explicitArgs,
        string expectedTarget,
        string expectedArgs)
    {
        ShortcutDockLaunchHelper.NormalizeProgramLaunch(input, explicitArgs, out string target, out string args);

        Assert.Equal(expectedTarget, target);
        Assert.Equal(expectedArgs, args);
    }
}
