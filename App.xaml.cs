using System.Windows;
using System.Windows.Media;
using WindBoard.Services;

namespace WindBoard;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        FontFamily? miSans = AppFonts.TryLoadMiSansFontFamily(AppContext.BaseDirectory);
        if (miSans is not null)
        {
            Resources[AppFonts.AppFontFamilyResourceKey] = miSans;
        }

        base.OnStartup(e);
    }
}

