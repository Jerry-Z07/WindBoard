using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using WindBoard.Core.Ink.Backend;
using WindBoard.Core.Input;

namespace WindBoard.Core.Modes
{
    public class SelectMode : InteractionModeBase
    {
        private const double DragThresholdDip = 4.0;

        private readonly InkCanvas _canvas;
        private readonly Func<IInkBackend?> _backendProvider;
        private readonly Action<Rect?>? _marqueeChanged;
        private readonly Action? _selectionChanged;

        private bool _pressed;
        private Point _startPoint;
        private bool _dragging;
        private bool _toggleSelection;

        public SelectMode(
            InkCanvas canvas,
            Func<IInkBackend?>? backendProvider = null,
            Action<Rect?>? marqueeChanged = null,
            Action? selectionChanged = null)
        {
            _canvas = canvas;
            _backendProvider = backendProvider ?? (() => null);
            _marqueeChanged = marqueeChanged;
            _selectionChanged = selectionChanged;
        }

        public override string Name => "Select";

        public override void SwitchOn()
        {
            _canvas.EditingMode = InkCanvasEditingMode.None;
            _canvas.UseCustomCursor = false;
            _canvas.ClearValue(Control.CursorProperty);
        }

        public override void SwitchOff()
        {
            _pressed = false;
            _dragging = false;
            _marqueeChanged?.Invoke(null);
        }

        public override void OnPointerDown(InputEventArgs args)
        {
            var backend = GetCustomBackend();
            if (backend == null) return;

            if (args.DeviceType == InputDeviceType.Mouse && !args.LeftButton) return;
            if (args.IsInAir) return;

            _pressed = true;
            _dragging = false;
            _toggleSelection = args.Ctrl;
            _startPoint = args.CanvasPoint;
            _marqueeChanged?.Invoke(new Rect(_startPoint, _startPoint));
        }

        public override void OnPointerMove(InputEventArgs args)
        {
            if (!_pressed) return;
            var backend = GetCustomBackend();
            if (backend == null) return;

            var current = args.CanvasPoint;
            if (!_dragging)
            {
                var v = current - _startPoint;
                if (v.LengthSquared < DragThresholdDip * DragThresholdDip)
                {
                    return;
                }

                _dragging = true;
            }

            var rect = new Rect(_startPoint, current);
            rect = NormalizeRect(rect);
            _marqueeChanged?.Invoke(rect);
        }

        public override void OnPointerUp(InputEventArgs args)
        {
            if (!_pressed) return;
            _pressed = false;

            var backend = GetCustomBackend();
            if (backend == null) return;

            bool changed;

            if (_dragging)
            {
                var rect = new Rect(_startPoint, args.CanvasPoint);
                rect = NormalizeRect(rect);
                backend.SelectInRect(rect, additive: _toggleSelection);
                changed = true;
            }
            else
            {
                changed = backend.SelectAtPoint(args.CanvasPoint, toggle: _toggleSelection);
            }

            _dragging = false;
            _marqueeChanged?.Invoke(null);

            if (changed)
            {
                _selectionChanged?.Invoke();
            }
        }

        private static Rect NormalizeRect(Rect rect)
        {
            if (rect.Width < 0)
            {
                rect = new Rect(rect.X + rect.Width, rect.Y, -rect.Width, rect.Height);
            }
            if (rect.Height < 0)
            {
                rect = new Rect(rect.X, rect.Y + rect.Height, rect.Width, -rect.Height);
            }

            return rect;
        }

        private CustomInkBackend? GetCustomBackend() => _backendProvider() as CustomInkBackend;
    }
}
