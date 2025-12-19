using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Input;
using System;

namespace WindBoard
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void RadioPen_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount >= 2)
            {
                e.Handled = true;
                // 使用 Dispatcher 延迟打开，防止 Popup 立即关闭
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                {
                    PopupPenSettings.IsOpen = true;
                }));
            }
        }

        private void RadioPen_Checked(object sender, RoutedEventArgs e)
        {
            // 切换到墨迹模式
            MyCanvas.EditingMode = InkCanvasEditingMode.Ink;
        }

        private void RadioEraser_Checked(object sender, RoutedEventArgs e)
        {
            // 切换到按笔画擦除模式
            MyCanvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
        }

        private void RadioSelect_Checked(object sender, RoutedEventArgs e)
        {
            // 切换到选择模式
            MyCanvas.EditingMode = InkCanvasEditingMode.Select;
        }

        private void Thickness_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && double.TryParse(rb.Tag?.ToString(), out double thickness))
            {
                var drawingAttributes = MyCanvas.DefaultDrawingAttributes;
                drawingAttributes.Width = thickness;
                drawingAttributes.Height = thickness;
            }
        }

        private void Color_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string colorCode)
            {
                try
                {
                    Color color = (Color)ColorConverter.ConvertFromString(colorCode);
                    MyCanvas.DefaultDrawingAttributes.Color = color;
                }
                catch (System.FormatException)
                {
                    // 处理颜色转换失败的情况，例如记录日志或使用默认颜色
                    MyCanvas.DefaultDrawingAttributes.Color = Colors.White;
                }
                
                // 选择颜色后关闭弹窗
                PopupPenSettings.IsOpen = false;
            }
        }
    }
}