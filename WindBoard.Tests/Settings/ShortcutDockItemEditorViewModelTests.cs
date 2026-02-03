using WindBoard.Settings;
using WindBoard.Settings.Pages;
using Xunit;

namespace WindBoard.Tests.Settings;

public sealed class ShortcutDockItemEditorViewModelTests
{
    [Fact]
    public void ToSettings_TrimsAndNullsEmptyFields()
    {
        ShortcutDockItemEditorViewModel vm = ShortcutDockItemEditorViewModel.CreateDefault();
        vm.Side = " left ";
        vm.Type = " program ";
        vm.Path = "  C:\\Temp\\app.exe  ";
        vm.IconSource = " icon ";
        vm.IconPath = "   ";
        vm.Arguments = "   ";

        ShortcutDockItemSettings settings = vm.ToSettings();

        Assert.Equal("left", settings.Side);
        Assert.Equal("program", settings.Type);
        Assert.Equal("C:\\Temp\\app.exe", settings.Path);
        Assert.Equal("icon", settings.IconSource);
        Assert.Null(settings.IconPath);
        Assert.Null(settings.Arguments);
    }
}

