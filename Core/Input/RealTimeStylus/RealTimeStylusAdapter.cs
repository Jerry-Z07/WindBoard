using System;
using System.Buffers;
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

                int pointCount = stylusPoints.Count;
                StylusPoint[] buffer = ArrayPool<StylusPoint>.Shared.Rent(pointCount);
                var packet = new StylusPacket(stage, buffer, pointCount, rawStylusInput.Timestamp, rawStylusInput.StylusDeviceId, isInAir);
                try
                {
                    stylusPoints.CopyTo(buffer, 0);
                    rawStylusInput.NotifyWhenProcessed(packet);
                }
                catch
                {
                    packet.Dispose();
                    throw;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RTS] Failed to queue stylus packet: {ex}");
            }
        }

        private void Deliver(object? callbackData, bool targetVerified)
        {
            if (callbackData is not StylusPacket packet)
            {
                return;
            }

            try
            {
                if (!targetVerified)
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

                // RTS 一个 packet 可能包含多个点；复用同一个 args 实例，避免高采样设备下 “每点 new args” 的 GC 压力。
                // 注意：args 在同一个 packet 内会被反复修改；如需在分发后持有，请使用 args.Clone() 获取快照。
                var args = new InputEventArgs
                {
                    DeviceType = deviceType,
                    PointerId = packet.StylusDeviceId,
                    Pressure = null,
                    HasPressureHardware = false,
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

                int count = packet.Count;
                if (count <= 0)
                {
                    return;
                }

                StylusPoint[] points = packet.Points;
                bool hasPressureHardware = StylusPressureHardware.HasPressureHardware(points[0].Description);
                for (int i = 0; i < count; i++)
                {
                    StylusPoint pt = points[i];
                    var canvasPoint = new Point(pt.X, pt.Y);
                    var viewportPoint = canvasToViewport?.Transform(canvasPoint) ?? canvasPoint;

                    args.CanvasPoint = canvasPoint;
                    args.ViewportPoint = viewportPoint;
                    args.HasPressureHardware = hasPressureHardware;
                    args.Pressure = hasPressureHardware ? TryReadPressure(pt) : null;
                    args.ContactSize = TryReadContactSize(pt);

                    // Down/Up packet 可能带多个点；保证 Down 只发一次、Up 只发一次，中间点作为 Move。
                    InputStage dispatchStage = GetDispatchStage(packet.Stage, i, count);

                    _dispatch(dispatchStage, args);
                }
            }
            finally
            {
                packet.Dispose();
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

        private static InputStage GetDispatchStage(InputStage packetStage, int index, int count)
        {
            return packetStage switch
            {
                InputStage.Down => index == 0 ? InputStage.Down : InputStage.Move,
                InputStage.Up => index < (count - 1) ? InputStage.Move : InputStage.Up,
                _ => packetStage
            };
        }

        private sealed class StylusPacket : IDisposable
        {
            private StylusPoint[]? _points;
            private int _count;

            public InputStage Stage { get; }
            public StylusPoint[] Points => _points ?? Array.Empty<StylusPoint>();
            public int Count => _count;
            public int Timestamp { get; }
            public int StylusDeviceId { get; }
            public bool IsInAir { get; }

            public StylusPacket(InputStage stage, StylusPoint[] points, int count, int timestamp, int stylusDeviceId, bool isInAir)
            {
                Stage = stage;
                _points = points;
                _count = Math.Clamp(count, 0, points.Length);
                Timestamp = timestamp;
                StylusDeviceId = stylusDeviceId;
                IsInAir = isInAir;
            }

            public long TimestampTicks => (long)Timestamp * TimeSpan.TicksPerMillisecond;

            public void Dispose()
            {
                StylusPoint[]? points = _points;
                if (points == null)
                {
                    return;
                }

                _points = null;
                _count = 0;

                // StylusPoint 数据不包含敏感信息；返回池时避免清零，降低高频输入开销。
                ArrayPool<StylusPoint>.Shared.Return(points, clearArray: false);
            }
        }
    }
}
