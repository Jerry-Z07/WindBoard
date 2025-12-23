using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WindBoard.UI
{
    public partial class SettingsWindow : Window
    {
        private MainWindow _mainWindow;

        public SettingsWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            
            // 初始化颜色选择器为当前背景色
            if (_mainWindow.MyCanvas.Background is SolidColorBrush brush)
            {
                ColorPicker.Color = brush.Color;
            }
        }

        private void PresetColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string colorCode)
            {
                try
                {
                    Color color = (Color)ColorConverter.ConvertFromString(colorCode);
                    ColorPicker.Color = color; // 这会触发 ColorChanged 事件
                }
                catch (FormatException)
                {
                    // 忽略无效颜色
                }
            }
        }

        private void ColorPicker_ColorChanged(object sender, RoutedPropertyChangedEventArgs<Color> e)
        {
            if (_mainWindow != null)
            {
                _mainWindow.SetBackgroundColor(e.NewValue);
            }
        }
    }
}
