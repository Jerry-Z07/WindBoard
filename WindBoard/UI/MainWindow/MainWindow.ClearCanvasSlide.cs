using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using WindBoard.Board.Editing;
using WindBoard.Interaction;
using WindBoard.Localization;
using WindBoard.Settings;
using WindBoard.UI.Common;

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
            bool canClear = BoardCanvas.CanClear;
            _clearCanvasSlideController.UpdateEnabledState(canClear);

            // 键盘入口与滑块共享可用状态；禁用时把原因写进 HelpText，读屏用户才知道为什么按不动。
            ClearCanvasSlideKeyboardButton.IsEnabled = canClear;
            AutomationProperties.SetHelpText(
                ClearCanvasSlideKeyboardButton,
                canClear ? string.Empty : L10n.Get("MainWindow_ClearCanvas_Empty_Hint"));
            ClearCanvasSlideText.Text = canClear
                ? L10n.Get("MainWindow_ClearCanvas_Slide")
                : L10n.Get("MainWindow_ClearCanvas_Empty_Hint");
        }

        private async void OnClearCanvasKeyboardInvoke(object sender, RoutedEventArgs e)
        {
            XamlRoot? xamlRoot = TryGetDialogXamlRoot();
            if (xamlRoot is null || !BoardCanvas.CanClear)
            {
                return;
            }

            bool confirmed = await DialogHelpers.ShowConfirmAsync(
                xamlRoot,
                L10n.Get("MainWindow_ClearCanvas_Action"),
                L10n.Get("MainWindow_ClearCanvas_Confirm"),
                L10n.Get("Common_Clear"),
                L10n.Get("Common_Cancel"));
            if (!confirmed)
            {
                return;
            }

            BoardCanvas.ClearAll();
            TryHideEraserFlyout();
        }

        private void ResetClearCanvasSlide(bool animated)
        {
            _clearCanvasSlideController.Reset(animated);
        }

    }
}
