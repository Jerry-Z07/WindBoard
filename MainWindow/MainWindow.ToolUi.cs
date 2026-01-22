using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WindBoard
{
    public partial class MainWindow
    {
        private void RadioPen_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (RadioPen.IsChecked == true)
            {
                e.Handled = true;
                // Toggle 行为：已打开则关闭，否则打开
                if (PopupPenSettings.IsOpen)
                {
                    PopupPenSettings.IsOpen = false;
                    return;
                }
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                {
                    PopupPenSettings.IsOpen = true;
                }));
            }
        }

        private void RadioPen_Checked(object sender, RoutedEventArgs e)
        {
            if (_modeController == null || _inkMode == null) return;
            _modeController.SetCurrentMode(_inkMode);
            if (_popupEraserClear != null)
                _popupEraserClear.IsOpen = false;

            // 退出选择模式时，隐藏选中框与悬浮 Dock
            ClearInkCanvasSelectionPreserveEditingMode();
            SelectAttachment(null);
        }

        private void RadioEraser_Checked(object sender, RoutedEventArgs e)
        {
            if (_modeController == null || _eraserMode == null) return;
            _modeController.SetCurrentMode(_eraserMode);

            // 退出选择模式时，隐藏选中框与悬浮 Dock
            ClearInkCanvasSelectionPreserveEditingMode();
            SelectAttachment(null);
        }

        private void RadioSelect_Checked(object sender, RoutedEventArgs e)
        {
            if (_modeController == null || _selectMode == null) return;
            _modeController.SetCurrentMode(_selectMode);
            if (_popupEraserClear != null)
                _popupEraserClear.IsOpen = false;
        }

        private void Thickness_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && double.TryParse(rb.Tag?.ToString(), out double thickness))
            {
                if (_strokeService == null) return;
                _baseThickness = thickness;
                _strokeService.SetBaseThickness(thickness, _zoomPanService.Zoom);
            }
        }

        private void Color_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string colorCode)
            {
                if (_strokeService == null) return;
                try
                {
                    Color color = (Color)ColorConverter.ConvertFromString(colorCode);
                    _strokeService.SetColor(color);
                }
                catch (FormatException)
                {
                    _strokeService.SetColor(Colors.White);
                }

                PopupPenSettings.IsOpen = false;
            }
        }
    }
}

