using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WindBoard.Core.Input;
using InputEventArgs = WindBoard.Core.Input.InputEventArgs;

namespace WindBoard.Core.Modes
{
    public sealed class EraserMode : InteractionModeBase
    {
        private readonly FrameworkElement _inputSurface;
        private readonly Canvas _overlay;
        private readonly Border _cursorRect;
        private readonly Func<double> _zoomProvider;
        private readonly Action<Rect>? _eraseRectAction;
        private readonly double _cursorOffsetY;

        private double _baseWidth = 40.0;
        private double _baseHeight = 80.0;
        private double _baseCornerRadius = 6.0;
        private double _cachedZoom = double.NaN;
        private double _cachedWidthContent;
        private double _cachedHeightContent;
        private double _cachedOffsetYContent;
        private bool _isPressed;
        private bool _isMouseErasing;

        public EraserMode(
            FrameworkElement inputSurface,
            Canvas overlay,
            Border cursorRect,
            Func<double> zoomProvider,
            double cursorOffsetY = 12.0,
            Action<Rect>? eraseRectAction = null)
        {
            _inputSurface = inputSurface;
            _overlay = overlay;
            _cursorRect = cursorRect;
            _zoomProvider = zoomProvider;
            _cursorOffsetY = cursorOffsetY;
            _eraseRectAction = eraseRectAction;
        }

        public override string Name => "Eraser";

        public override void SwitchOn()
        {
            _inputSurface.Cursor = Cursors.Arrow;
            _isPressed = false;
            _isMouseErasing = false;
            _ = UpdateEraserVisual(null);
        }

        public override void SwitchOff()
        {
            _isPressed = false;
            _isMouseErasing = false;
            _overlay.Visibility = Visibility.Collapsed;
            _inputSurface.ClearValue(FrameworkElement.CursorProperty);
        }

        public override void OnPointerDown(InputEventArgs args)
        {
            _isPressed = true;
            _isMouseErasing = args.DeviceType == InputDeviceType.Mouse;
            _inputSurface.Cursor = Cursors.Arrow;
            Rect? rect = UpdateEraserVisual(args.CanvasPoint);
            if (rect.HasValue)
            {
                _eraseRectAction?.Invoke(rect.Value);
            }
        }

        public override void OnPointerMove(InputEventArgs args)
        {
            if (_isPressed)
            {
                Rect? rect = UpdateEraserVisual(args.CanvasPoint);
                if (rect.HasValue)
                {
                    _eraseRectAction?.Invoke(rect.Value);
                }
            }
        }

        public override void OnPointerUp(InputEventArgs args)
        {
            _isPressed = false;
            _isMouseErasing = false;
            _inputSurface.Cursor = Cursors.Arrow;
            _ = UpdateEraserVisual(null);
        }

        private Rect? UpdateEraserVisual(Point? center)
        {
            double zoom = _zoomProvider();
            if (zoom <= 0) zoom = 1;

            if (!IsClose(zoom, _cachedZoom))
            {
                _cachedZoom = zoom;
                _cachedWidthContent = _baseWidth / zoom;
                _cachedHeightContent = _baseHeight / zoom;
                _cachedOffsetYContent = _cursorOffsetY / zoom;

                double radiusContent = _baseCornerRadius / zoom;
                _cursorRect.Width = _cachedWidthContent;
                _cursorRect.Height = _cachedHeightContent;
                _cursorRect.CornerRadius = new CornerRadius(radiusContent);
            }

            Rect? rect = null;
            if (center.HasValue)
            {
                double left = center.Value.X - _cachedWidthContent / 2.0;
                double topBase = center.Value.Y - _cachedHeightContent / 2.0;
                double top = _isMouseErasing ? (topBase + _cachedOffsetYContent) : topBase;
                Canvas.SetLeft(_cursorRect, left);
                Canvas.SetTop(_cursorRect, top);
                rect = new Rect(left, top, _cachedWidthContent, _cachedHeightContent);
            }

            _overlay.Visibility = (_isPressed && _eraseRectAction != null)
                ? Visibility.Visible
                : Visibility.Collapsed;

            return rect;
        }

        private static bool IsClose(double a, double b)
        {
            if (double.IsNaN(a) || double.IsNaN(b)) return false;
            return Math.Abs(a - b) < 0.000001;
        }
    }
}

