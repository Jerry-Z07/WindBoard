using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Input.StylusPlugIns;
using System.Windows.Media;
using WindBoard.Core.Input;

namespace WindBoard.Core.Input.RealTimeStylus
{
    internal sealed class RealTimeStylusAdapter : StylusPlugIn
    {
        private readonly InkCanvas _canvas;
        private readonly ScrollViewer _viewport;
        private readonly Action<InputStage, InputEventArgs> _dispatch;
        private readonly Dictionary<int, InputDeviceType> _deviceTypeCache = new();

        public RealTimeStylusAdapter(InkCanvas canvas, ScrollViewer viewport, Action<InputStage, InputEventArgs> dispatch)
        {
            _canvas = canvas;
            _viewport = viewport;
            _dispatch = dispatch;
        }

        protected override void OnStylusDown(RawStylusInput rawStylusInput)
        {
            QueuePacket(InputStage.Down, rawStylusInput, isInAir: false);
        }

        protected override void OnStylusMove(RawStylusInput rawStylusInput)
        {
            QueuePacket(InputStage.Move, rawStylusInput, isInAir: false);
        }

        protected override void OnStylusUp(RawStylusInput rawStylusInput)
        {
            QueuePacket(InputStage.Up, rawStylusInput, isInAir: false);
        }

        protected override void OnStylusDownProcessed(object? callbackData, bool targetVerified)
        {
            Deliver(callbackData, targetVerified);
        }

        protected override void OnStylusMoveProcessed(object? callbackData, bool targetVerified)
        {
            Deliver(callbackData, targetVerified);
        }

        protected override void OnStylusUpProcessed(object? callbackData, bool targetVerified)
        {
            Deliver(callbackData, targetVerified);
        }

        private void QueuePacket(InputStage stage, RawStylusInput rawStylusInput, bool isInAir)
        {
            try
            {
                var stylusPoints = rawStylusInput.GetStylusPoints();
                if (stylusPoints.Count == 0)
                {
                    return;
                }

                var buffer = new StylusPoint[stylusPoints.Count];
                stylusPoints.CopyTo(buffer, 0);

                rawStylusInput.NotifyWhenProcessed(
                    new StylusPacket(stage, buffer, rawStylusInput.Timestamp, rawStylusInput.StylusDeviceId, isInAir));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RTS] Failed to queue stylus packet: {ex}");
            }
        }

        private void Deliver(object? callbackData, bool targetVerified)
        {
            if (!targetVerified)
            {
                return;
            }

            if (callbackData is not StylusPacket packet)
            {
                return;
            }

            GeneralTransform? canvasToViewport = null;
            try
            {
                canvasToViewport = _canvas.TransformToVisual(_viewport);
            }
            catch
            {
                // ignored: fallback to canvas coordinates if transform is not ready
            }

            var mods = Keyboard.Modifiers;
            bool ctrl = (mods & ModifierKeys.Control) != 0;
            bool shift = (mods & ModifierKeys.Shift) != 0;
            bool alt = (mods & ModifierKeys.Alt) != 0;
            var deviceType = ResolveDeviceType(packet.StylusDeviceId);

            // 触摸仍交由 WPF Touch 管道处理，避免重复分发
            if (deviceType == InputDeviceType.Touch)
            {
                return;
            }

            // 性能：RTS 每个 packet 可能包含多个点；逐点 new InputEventArgs 会产生大量分配并放大 UI 线程压力。
            // 该项目当前未使用压力（InkMode/DefaultDrawingAttributes IgnorePressure=true），因此不读取 PressureFactor，避免额外跨层开销。
            // 注意：同一个 args 实例会在循环内被复用；本项目的输入管道为同步消费，不应在外部缓存该对象引用。
            var args = new InputEventArgs
            {
                DeviceType = deviceType,
                PointerId = packet.StylusDeviceId,
                Pressure = null,
                IsInAir = packet.IsInAir,
                LeftButton = false,
                RightButton = false,
                MiddleButton = false,
                Ctrl = ctrl,
                Shift = shift,
                Alt = alt,
                TimestampTicks = packet.TimestampTicks,
                ContactSize = null
            };

            foreach (var pt in packet.Points)
            {
                var canvasPoint = new Point(pt.X, pt.Y);
                var viewportPoint = canvasToViewport?.Transform(canvasPoint) ?? canvasPoint;

                args.CanvasPoint = canvasPoint;
                args.ViewportPoint = viewportPoint;
                args.ContactSize = TryReadContactSize(pt);

                _dispatch(packet.Stage, args);
            }
        }

        private static double? TryReadPressure(StylusPoint pt)
        {
            var desc = pt.Description;
            if (!desc.HasProperty(StylusPointProperties.NormalPressure))
            {
                return null;
            }

            try
            {
                return pt.PressureFactor;
            }
            catch
            {
                return null;
            }
        }

        private static Size? TryReadContactSize(StylusPoint pt)
        {
            var desc = pt.Description;
            if (!desc.HasProperty(StylusPointProperties.Width) || !desc.HasProperty(StylusPointProperties.Height))
            {
                return null;
            }

            try
            {
                double width = pt.GetPropertyValue(StylusPointProperties.Width);
                double height = pt.GetPropertyValue(StylusPointProperties.Height);
                if (width <= 0 || height <= 0)
                {
                    return null;
                }
                return new Size(width, height);
            }
            catch
            {
                return null;
            }
        }

        private InputDeviceType ResolveDeviceType(int stylusDeviceId)
        {
            if (_deviceTypeCache.TryGetValue(stylusDeviceId, out var cached))
            {
                return cached;
            }

            var mapped = InputDeviceType.Stylus;
            try
            {
                foreach (TabletDevice tablet in Tablet.TabletDevices)
                {
                    foreach (StylusDevice stylus in tablet.StylusDevices)
                    {
                        if (stylus.Id == stylusDeviceId)
                        {
                            mapped = tablet.Type == TabletDeviceType.Touch
                                ? InputDeviceType.Touch
                                : InputDeviceType.Stylus;
                            _deviceTypeCache[stylusDeviceId] = mapped;
                            return mapped;
                        }
                    }
                }
            }
            catch
            {
            }

            _deviceTypeCache[stylusDeviceId] = mapped;
            return mapped;
        }

        private sealed class StylusPacket
        {
            public InputStage Stage { get; }
            public StylusPoint[] Points { get; }
            public int Timestamp { get; }
            public int StylusDeviceId { get; }
            public bool IsInAir { get; }

            public StylusPacket(InputStage stage, StylusPoint[] points, int timestamp, int stylusDeviceId, bool isInAir)
            {
                Stage = stage;
                Points = points;
                Timestamp = timestamp;
                StylusDeviceId = stylusDeviceId;
                IsInAir = isInAir;
            }

            public long TimestampTicks => (long)Timestamp * TimeSpan.TicksPerMillisecond;
        }
    }
}
