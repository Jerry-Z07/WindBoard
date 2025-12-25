using System.Windows;
using System.Windows.Media;
using System.IO;

namespace WindBoard;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        LoadFonts();
    }

    private void LoadFonts()
    {
        try
        {
            var fontsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", "fonts");
            if (!Directory.Exists(fontsDirectory))
            {
                fontsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Fonts");
            }

            if (Directory.Exists(fontsDirectory))
            {
                var fontFiles = Directory.GetFiles(fontsDirectory, "*.ttf");
                foreach (var fontFile in fontFiles)
                {
                    try
                    {
                        var fontFamily = new FontFamily(new Uri(fontFile), "./#" + Path.GetFileNameWithoutExtension(fontFile));
                        Resources[Path.GetFileNameWithoutExtension(fontFile) + "FontFamily"] = fontFamily;
                        
                        if (Path.GetFileNameWithoutExtension(fontFile).StartsWith("MiSans"))
                        {
                            Resources["MiSansFontFamily"] = fontFamily;
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch
        {
        }
    }
}

