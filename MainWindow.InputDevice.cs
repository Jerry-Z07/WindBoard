using System;
using System.Windows;
using System.Windows.Input;

namespace WindBoard
{
    public partial class MainWindow
    {
        public enum InputDeviceType { Mouse, Stylus, Touch }

        public class DeviceInputEventArgs : EventArgs
        {
            public InputDeviceType DeviceType { get; set; }
            public Point CanvasPoint { get; set; }
            public Point ViewportPoint { get; set; }
            public int? TouchId { get; set; }

            public double? Pressure { get; set; }
            public bool IsInAir { get; set; }

            public bool LeftButton { get; set; }
            public bool RightButton { get; set; }
            public bool MiddleButton { get; set; }
            public bool Ctrl { get; set; }
            public bool Shift { get; set; }
            public bool Alt { get; set; }

            public long TimestampTicks { get; set; }
        }

        public event EventHandler<DeviceInputEventArgs>? DeviceDown;
        public event EventHandler<DeviceInputEventArgs>? DeviceMove;
        public event EventHandler<DeviceInputEventArgs>? DeviceUp;
        public event EventHandler<DeviceInputEventArgs>? DeviceHover;

        public event EventHandler<DeviceInputEventArgs>? PointerDown;
        public event EventHandler<DeviceInputEventArgs>? PointerMove;
        public event EventHandler<DeviceInputEventArgs>? PointerUp;
        public event EventHandler<DeviceInputEventArgs>? PointerHover;

        private void RaiseDeviceDown(DeviceInputEventArgs args)
        {
            DeviceDown?.Invoke(this, args);
            PointerDown?.Invoke(this, args);
        }

        private void RaiseDeviceMove(DeviceInputEventArgs args)
        {
            DeviceMove?.Invoke(this, args);
            PointerMove?.Invoke(this, args);
        }

        private void RaiseDeviceUp(DeviceInputEventArgs args)
        {
            DeviceUp?.Invoke(this, args);
            PointerUp?.Invoke(this, args);
        }

        private void RaiseDeviceHover(DeviceInputEventArgs args)
        {
            DeviceHover?.Invoke(this, args);
            PointerHover?.Invoke(this, args);
        }

        private void RaiseDeviceDown(Point canvas, Point viewport, InputDeviceType type, int? touchId = null)
        {
            var mods = Keyboard.Modifiers;
            var args = new DeviceInputEventArgs
            {
                DeviceType = type,
                CanvasPoint = canvas,
                ViewportPoint = viewport,
                TouchId = touchId,
                Pressure = null,
                IsInAir = false,
                LeftButton = type == InputDeviceType.Mouse && Mouse.LeftButton == MouseButtonState.Pressed,
                RightButton = type == InputDeviceType.Mouse && Mouse.RightButton == MouseButtonState.Pressed,
                MiddleButton = type == InputDeviceType.Mouse && Mouse.MiddleButton == MouseButtonState.Pressed,
                Ctrl = (mods & ModifierKeys.Control) != 0,
                Shift = (mods & ModifierKeys.Shift) != 0,
                Alt = (mods & ModifierKeys.Alt) != 0,
                TimestampTicks = DateTime.UtcNow.Ticks
            };
            RaiseDeviceDown(args);
        }

        private void RaiseDeviceMove(Point canvas, Point viewport, InputDeviceType type, int? touchId = null)
        {
            var mods = Keyboard.Modifiers;
            var args = new DeviceInputEventArgs
            {
                DeviceType = type,
                CanvasPoint = canvas,
                ViewportPoint = viewport,
                TouchId = touchId,
                Pressure = null,
                IsInAir = false,
                LeftButton = type == InputDeviceType.Mouse && Mouse.LeftButton == MouseButtonState.Pressed,
                RightButton = type == InputDeviceType.Mouse && Mouse.RightButton == MouseButtonState.Pressed,
                MiddleButton = type == InputDeviceType.Mouse && Mouse.MiddleButton == MouseButtonState.Pressed,
                Ctrl = (mods & ModifierKeys.Control) != 0,
                Shift = (mods & ModifierKeys.Shift) != 0,
                Alt = (mods & ModifierKeys.Alt) != 0,
                TimestampTicks = DateTime.UtcNow.Ticks
            };
            RaiseDeviceMove(args);
        }

        private void RaiseDeviceUp(Point canvas, Point viewport, InputDeviceType type, int? touchId = null)
        {
            var mods = Keyboard.Modifiers;
            var args = new DeviceInputEventArgs
            {
                DeviceType = type,
                CanvasPoint = canvas,
                ViewportPoint = viewport,
                TouchId = touchId,
                Pressure = null,
                IsInAir = false,
                LeftButton = type == InputDeviceType.Mouse && Mouse.LeftButton == MouseButtonState.Pressed,
                RightButton = type == InputDeviceType.Mouse && Mouse.RightButton == MouseButtonState.Pressed,
                MiddleButton = type == InputDeviceType.Mouse && Mouse.MiddleButton == MouseButtonState.Pressed,
                Ctrl = (mods & ModifierKeys.Control) != 0,
                Shift = (mods & ModifierKeys.Shift) != 0,
                Alt = (mods & ModifierKeys.Alt) != 0,
                TimestampTicks = DateTime.UtcNow.Ticks
            };
            RaiseDeviceUp(args);
        }

        private void RaiseDeviceHover(Point canvas, Point viewport, InputDeviceType type, int? touchId = null, bool isInAir = false)
        {
            var mods = Keyboard.Modifiers;
            var args = new DeviceInputEventArgs
            {
                DeviceType = type,
                CanvasPoint = canvas,
                ViewportPoint = viewport,
                TouchId = touchId,
                Pressure = null,
                IsInAir = isInAir,
                LeftButton = false,
                RightButton = false,
                MiddleButton = false,
                Ctrl = (mods & ModifierKeys.Control) != 0,
                Shift = (mods & ModifierKeys.Shift) != 0,
                Alt = (mods & ModifierKeys.Alt) != 0,
                TimestampTicks = DateTime.UtcNow.Ticks
            };
            RaiseDeviceHover(args);
        }
    }
}
