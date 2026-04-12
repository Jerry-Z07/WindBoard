using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using WindBoard.Board.Editing;
using WindBoard.Interaction;
using WindBoard.Settings;

namespace WindBoard
{
    /// <summary>
    /// 主窗口：清空画布滑动确认（滑块）相关代码。
    /// </summary>
    public sealed partial class MainWindow
    {
        private void OnClearCanvasThumbPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _clearCanvasSlideController.OnPointerPressed(e);
        }

        private void OnClearCanvasThumbPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            _clearCanvasSlideController.OnPointerMoved(e);
        }

        private void OnClearCanvasThumbPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _clearCanvasSlideController.OnPointerReleased(e);
        }

        private void OnClearCanvasThumbPointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            _clearCanvasSlideController.OnPointerCanceled(e);
        }

        private void OnClearCanvasThumbPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            _clearCanvasSlideController.OnPointerCaptureLost(e);
        }

        private void UpdateClearCanvasSlideState()
        {
            _clearCanvasSlideController.UpdateEnabledState(BoardCanvas.CanClear);
        }

        private void ResetClearCanvasSlide(bool animated)
        {
            _clearCanvasSlideController.Reset(animated);
        }

    }
}
