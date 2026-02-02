using System;
using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Vortice.Mathematics;
using UiColor = Windows.UI.Color;
using WindBoard.Board;
using WindBoard.Board.Commands;
using WindBoard.Board.Editing;
using WindBoard.Board.Viewport;
using WindBoard.Interaction;
using WindBoard.Rendering;
using WindBoard.Rendering.Board;

namespace WindBoard.Controls
{
    /// <summary>
    /// 画布控件：擦除光标（SVG）事件与显示逻辑相关代码。
    /// </summary>
    public sealed partial class BoardCanvasControl
    {
        private void AttachEraserCursorHandlers()
        {
            if (_isEraserCursorHandlersAttached)
            {
                return;
            }

            _cursorPointerEnteredHandler = OnCanvasPointerEnteredForEraserCursor;
            _cursorPointerExitedHandler = OnCanvasPointerExitedForEraserCursor;
            _cursorPointerMovedHandler = OnCanvasPointerMovedForEraserCursor;
            _cursorPointerPressedHandler = OnCanvasPointerPressedForEraserCursor;
            _cursorPointerReleasedHandler = OnCanvasPointerReleasedForEraserCursor;
            _cursorPointerCanceledHandler = OnCanvasPointerCanceledForEraserCursor;
            _cursorPointerCaptureLostHandler = OnCanvasPointerCaptureLostForEraserCursor;

            // 使用 handledEventsToo=true，避免输入控制器先把事件标记为 Handled 导致光标无法更新。
            CanvasPanel.AddHandler(UIElement.PointerEnteredEvent, _cursorPointerEnteredHandler, true);
            CanvasPanel.AddHandler(UIElement.PointerExitedEvent, _cursorPointerExitedHandler, true);
            CanvasPanel.AddHandler(UIElement.PointerMovedEvent, _cursorPointerMovedHandler, true);
            CanvasPanel.AddHandler(UIElement.PointerPressedEvent, _cursorPointerPressedHandler, true);
            CanvasPanel.AddHandler(UIElement.PointerReleasedEvent, _cursorPointerReleasedHandler, true);
            CanvasPanel.AddHandler(UIElement.PointerCanceledEvent, _cursorPointerCanceledHandler, true);
            CanvasPanel.AddHandler(UIElement.PointerCaptureLostEvent, _cursorPointerCaptureLostHandler, true);

            _isEraserCursorHandlersAttached = true;
        }

        private void DetachEraserCursorHandlers()
        {
            if (!_isEraserCursorHandlersAttached)
            {
                return;
            }

            if (_cursorPointerEnteredHandler is not null)
            {
                CanvasPanel.RemoveHandler(UIElement.PointerEnteredEvent, _cursorPointerEnteredHandler);
            }

            if (_cursorPointerExitedHandler is not null)
            {
                CanvasPanel.RemoveHandler(UIElement.PointerExitedEvent, _cursorPointerExitedHandler);
            }

            if (_cursorPointerMovedHandler is not null)
            {
                CanvasPanel.RemoveHandler(UIElement.PointerMovedEvent, _cursorPointerMovedHandler);
            }

            if (_cursorPointerPressedHandler is not null)
            {
                CanvasPanel.RemoveHandler(UIElement.PointerPressedEvent, _cursorPointerPressedHandler);
            }

            if (_cursorPointerReleasedHandler is not null)
            {
                CanvasPanel.RemoveHandler(UIElement.PointerReleasedEvent, _cursorPointerReleasedHandler);
            }

            if (_cursorPointerCanceledHandler is not null)
            {
                CanvasPanel.RemoveHandler(UIElement.PointerCanceledEvent, _cursorPointerCanceledHandler);
            }

            if (_cursorPointerCaptureLostHandler is not null)
            {
                CanvasPanel.RemoveHandler(UIElement.PointerCaptureLostEvent, _cursorPointerCaptureLostHandler);
            }

            _cursorPointerEnteredHandler = null;
            _cursorPointerExitedHandler = null;
            _cursorPointerMovedHandler = null;
            _cursorPointerPressedHandler = null;
            _cursorPointerReleasedHandler = null;
            _cursorPointerCanceledHandler = null;
            _cursorPointerCaptureLostHandler = null;
            _isEraserCursorHandlersAttached = false;

            _isPointerOverCanvas = false;
            _isPointerInContact = false;
            _lastPointerDeviceType = null;
            UpdateEraserCursorVisibility();
        }

        private void OnCanvasPointerEnteredForEraserCursor(object sender, PointerRoutedEventArgs e)
        {
            _isPointerOverCanvas = true;
            _lastPointerDeviceType = e.Pointer.PointerDeviceType;
            _isPointerInContact = e.GetCurrentPoint(CanvasPanel).IsInContact;
            UpdateEraserCursorPosition(e);
            UpdateEraserCursorVisibility();
        }

        private void OnCanvasPointerExitedForEraserCursor(object sender, PointerRoutedEventArgs e)
        {
            _isPointerOverCanvas = false;
            UpdateEraserCursorVisibility();
        }

        private void OnCanvasPointerMovedForEraserCursor(object sender, PointerRoutedEventArgs e)
        {
            _lastPointerDeviceType = e.Pointer.PointerDeviceType;
            _isPointerInContact = e.GetCurrentPoint(CanvasPanel).IsInContact;
            UpdateEraserCursorPosition(e);
            UpdateEraserCursorVisibility();
        }

        private void OnCanvasPointerPressedForEraserCursor(object sender, PointerRoutedEventArgs e)
        {
            // 触摸没有“悬停”概念：通过按下事件建立光标显示状态。
            _isPointerOverCanvas = true;
            _lastPointerDeviceType = e.Pointer.PointerDeviceType;
            _isPointerInContact = e.GetCurrentPoint(CanvasPanel).IsInContact;
            UpdateEraserCursorPosition(e);
            UpdateEraserCursorVisibility();
        }

        private void OnCanvasPointerReleasedForEraserCursor(object sender, PointerRoutedEventArgs e)
        {
            _lastPointerDeviceType = e.Pointer.PointerDeviceType;
            _isPointerInContact = e.GetCurrentPoint(CanvasPanel).IsInContact;
            UpdateEraserCursorVisibility();
        }

        private void OnCanvasPointerCanceledForEraserCursor(object sender, PointerRoutedEventArgs e)
        {
            _isPointerInContact = false;
            UpdateEraserCursorVisibility();
        }

        private void OnCanvasPointerCaptureLostForEraserCursor(object sender, PointerRoutedEventArgs e)
        {
            _isPointerInContact = false;
            _isPointerOverCanvas = false;
            UpdateEraserCursorVisibility();
        }

        private void UpdateEraserCursorVisibility()
        {
            if (EraserCursorImage is null)
            {
                return;
            }

            bool shouldShow = _tool == BoardTool.Eraser && _lastPointerDeviceType is not null;

            // 触摸/鼠标：只有按下（接触）时才显示；触控笔：悬停时显示。
            if (_lastPointerDeviceType == PointerDeviceType.Touch || _lastPointerDeviceType == PointerDeviceType.Mouse)
            {
                shouldShow &= _isPointerInContact;
            }
            else
            {
                shouldShow &= _isPointerOverCanvas;
            }

            EraserCursorImage.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateEraserCursorPosition(PointerRoutedEventArgs e)
        {
            if (EraserCursorImage is null)
            {
                return;
            }

            // 以图片中心对齐指针位置；后续可根据擦除大小改为“底部/边缘对齐”等更符合手感的锚点。
            Windows.Foundation.Point pos = e.GetCurrentPoint(CanvasPanel).Position;
            double width = EraserCursorImage.ActualWidth > 0 ? EraserCursorImage.ActualWidth : EraserCursorImage.Width;
            double height = EraserCursorImage.ActualHeight > 0 ? EraserCursorImage.ActualHeight : EraserCursorImage.Height;

            // 光标锚点：中心跟随指针（包含触摸）。
            Canvas.SetLeft(EraserCursorImage, pos.X - width / 2.0);
            Canvas.SetTop(EraserCursorImage, pos.Y - height / 2.0);
        }

        private Vector2 GetEraserRadiusDipFromCursor()
        {
            // 默认以光标控件的宽高作为擦除范围的“直径”，取一半作为半径。
            // 这样可以保证用户看到的光标大小与实际擦除范围一致。
            if (EraserCursorImage is null)
            {
                return new Vector2(24.0f, 36.0f);
            }

            double width = !double.IsNaN(EraserCursorImage.Width) && EraserCursorImage.Width > 0
                ? EraserCursorImage.Width
                : EraserCursorImage.ActualWidth;

            double height = !double.IsNaN(EraserCursorImage.Height) && EraserCursorImage.Height > 0
                ? EraserCursorImage.Height
                : EraserCursorImage.ActualHeight;

            width = Math.Max(1.0, width);
            height = Math.Max(1.0, height);

            return new Vector2((float)(width / 2.0), (float)(height / 2.0));
        }

    }
}
