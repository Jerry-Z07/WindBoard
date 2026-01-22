using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using WindBoard.Core.Input;
using InputEventArgs = WindBoard.Core.Input.InputEventArgs;

namespace WindBoard.Core.Modes
{
    public class EraserMode : InteractionModeBase
    {
        private readonly InkCanvas _canvas;
        private readonly Canvas _overlay;
        private readonly Border _cursorRect;
        private readonly Func<double> _zoomProvider;
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

        public EraserMode(InkCanvas canvas, Canvas overlay, Border cursorRect, Func<double> zoomProvider, double cursorOffsetY = 12.0)
        {
            _canvas = canvas;
            _overlay = overlay;
            _cursorRect = cursorRect;
            _zoomProvider = zoomProvider;
            _cursorOffsetY = cursorOffsetY;
        }

        public override string Name => "Eraser";

        public override void SwitchOn()
        {
            _canvas.EditingMode = InkCanvasEditingMode.EraseByPoint;
            _canvas.UseCustomCursor = true;
            _canvas.Cursor = Cursors.Arrow;
            _isPressed = false;
            _isMouseErasing = false;
            UpdateEraserVisual(null);
        }

        public override void SwitchOff()
        {
            _isPressed = false;
            _isMouseErasing = false;
            _overlay.Visibility = Visibility.Collapsed;
            _canvas.UseCustomCursor = false;
            _canvas.ClearValue(Control.CursorProperty);
        }

        public override void OnPointerDown(InputEventArgs args)
        {
            _isPressed = true;
            _isMouseErasing = args.DeviceType == InputDeviceType.Mouse;
            _canvas.Cursor = Cursors.Arrow;
            UpdateEraserVisual(args.CanvasPoint);
        }

        public override void OnPointerMove(InputEventArgs args)
        {
            if (_isPressed)
            {
                UpdateEraserVisual(args.CanvasPoint);
            }
        }

        public override void OnPointerUp(InputEventArgs args)
        {
            _isPressed = false;
            _isMouseErasing = false;
            _canvas.Cursor = Cursors.Arrow;
            UpdateEraserVisual(null);
        }

        private void UpdateEraserVisual(Point? center)
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

                _canvas.EraserShape = new RectangleStylusShape(_cachedWidthContent, _cachedHeightContent);
            }

            if (center.HasValue)
            {
                double left = center.Value.X - _cachedWidthContent / 2.0;
                double topBase = center.Value.Y - _cachedHeightContent / 2.0;
                double top = _isMouseErasing ? (topBase + _cachedOffsetYContent) : topBase;
                Canvas.SetLeft(_cursorRect, left);
                Canvas.SetTop(_cursorRect, top);
            }

            _overlay.Visibility = (_isPressed && _canvas.EditingMode == InkCanvasEditingMode.EraseByPoint)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private static bool IsClose(double a, double b)
        {
            if (double.IsNaN(a) || double.IsNaN(b)) return false;
            return Math.Abs(a - b) < 0.000001;
        }
    }
}
