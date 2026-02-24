using WindBoard.Features.Dock.Models;
using WindBoard.Features.Dock.UI;
using Xunit;

namespace WindBoard.Tests.Features.Dock;

public sealed class ShortcutDockItemEditorViewModelTests
{
    [Fact]
    public void ToSettings_TrimsAndNullsEmptyFields()
    {
        ShortcutDockItemEditorViewModel vm = ShortcutDockItemEditorViewModel.CreateDefault();
        vm.Side = " left ";
        vm.Type = " program ";
        vm.DisplayName = "  我的应用  ";
        vm.Path = "  C:\\Temp\\app.exe  ";
        vm.IconSource = " font ";
        vm.IconPath = "   ";
        vm.IconSymbol = "  Add  ";
        vm.Arguments = "   ";

        ShortcutDockItemSettings settings = vm.ToSettings();

        Assert.Equal("left", settings.Side);
        Assert.Equal("program", settings.Type);
        Assert.Equal("我的应用", settings.DisplayName);
        Assert.Equal("C:\\Temp\\app.exe", settings.Path);
        Assert.Equal("font", settings.IconSource);
        Assert.Null(settings.IconPath);
        Assert.Equal("Add", settings.IconSymbol);
        Assert.Null(settings.Arguments);
    }

    [Fact]
    public void TypeChanged_RaisesPropertyChanged_ForDerivedProperties()
    {
        ShortcutDockItemEditorViewModel vm = ShortcutDockItemEditorViewModel.CreateDefault();
        var changed = new HashSet<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.Type = ShortcutDockItemTypes.Program;

        Assert.Contains(nameof(ShortcutDockItemEditorViewModel.Type), changed);
        Assert.Contains(nameof(ShortcutDockItemEditorViewModel.ArgumentsPanelVisibility), changed);
        Assert.Contains(nameof(ShortcutDockItemEditorViewModel.PathBrowseVisibility), changed);
        Assert.Contains(nameof(ShortcutDockItemEditorViewModel.PathHeader), changed);
        Assert.Contains(nameof(ShortcutDockItemEditorViewModel.PathPlaceholder), changed);
    }
}
