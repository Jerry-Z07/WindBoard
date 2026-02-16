using WindBoard.Updates;

namespace WindBoard.Tests.Updates;

public sealed class SemanticVersionTests
{
    [Fact]
    public void Compare_Should_Work_For_Stable_Versions()
    {
        Assert.True(SemanticVersion.TryParse("2.0.0", out SemanticVersion v2));
        Assert.True(SemanticVersion.TryParse("1.9.9", out SemanticVersion v1));
        Assert.True(v2.CompareTo(v1) > 0);
    }

    [Fact]
    public void Compare_Should_Treat_Prerelease_As_Lower()
    {
        Assert.True(SemanticVersion.TryParse("2.0.0", out SemanticVersion stable));
        Assert.True(SemanticVersion.TryParse("2.0.0-beta.1", out SemanticVersion prerelease));
        Assert.True(stable.CompareTo(prerelease) > 0);
    }

    [Fact]
    public void Compare_Should_Handle_Prerelease_Numeric_Identifiers()
    {
        Assert.True(SemanticVersion.TryParse("2.0.0-beta.2", out SemanticVersion b2));
        Assert.True(SemanticVersion.TryParse("2.0.0-beta.10", out SemanticVersion b10));
        Assert.True(b2.CompareTo(b10) < 0);
    }

    [Fact]
    public void Parse_Should_Ignore_V_Prefix_And_Build_Metadata()
    {
        Assert.True(SemanticVersion.TryParse("v2.0.0+commit.123", out SemanticVersion a));
        Assert.True(SemanticVersion.TryParse("2.0.0", out SemanticVersion b));
        Assert.Equal(0, a.CompareTo(b));
    }
}

