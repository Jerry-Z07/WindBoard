using WindBoard.Settings;
using Xunit;

namespace WindBoard.Tests.Settings;

public sealed class KeyboardShortcutGestureTests
{
    [Fact]
    public void TryParse_NormalizesCaseAndSpaces()
    {
        Assert.True(KeyboardShortcutGesture.TryParse(" control + shift + z ", out KeyboardShortcutGesture gesture));
        Assert.Equal("Ctrl+Shift+Z", gesture.ToSettingString());
    }

    [Fact]
    public void TryParse_ParsesDigits()
    {
        Assert.True(KeyboardShortcutGesture.TryParse("Ctrl+1", out KeyboardShortcutGesture gesture));
        Assert.Equal("Ctrl+1", gesture.ToSettingString());
        Assert.True(gesture.IsValidForApp());
    }

    [Fact]
    public void TryParse_ParsesFunctionKeys()
    {
        Assert.True(KeyboardShortcutGesture.TryParse("Ctrl+F5", out KeyboardShortcutGesture gesture));
        Assert.Equal("Ctrl+F5", gesture.ToSettingString());
        Assert.True(gesture.IsValidForApp());
    }

    [Fact]
    public void IsValidForApp_RejectsSingleKey()
    {
        Assert.True(KeyboardShortcutGesture.TryParse("Z", out KeyboardShortcutGesture gesture));
        Assert.False(gesture.IsValidForApp());
    }

    [Fact]
    public void IsValidForApp_RejectsShiftOnly()
    {
        Assert.True(KeyboardShortcutGesture.TryParse("Shift+Z", out KeyboardShortcutGesture gesture));
        Assert.False(gesture.IsValidForApp());
    }

    [Fact]
    public void IsValidForApp_AcceptsAlt()
    {
        Assert.True(KeyboardShortcutGesture.TryParse("Alt+Z", out KeyboardShortcutGesture gesture));
        Assert.True(gesture.IsValidForApp());
        Assert.Equal("Alt+Z", gesture.ToSettingString());
    }

    [Fact]
    public void TryParse_Fails_WhenMultipleKeysProvided()
    {
        Assert.False(KeyboardShortcutGesture.TryParse("Ctrl+Z+Y", out _));
    }
}

