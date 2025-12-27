using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Input.StylusPlugIns;
using WindBoard.Core.Input;

namespace WindBoard.Core.Input.RealTimeStylus
{
    public class RealTimeStylusManager : IDisposable
    {
        private readonly InkCanvas _canvas;
        private readonly ScrollViewer _viewport;
        private readonly Action<InputStage, InputEventArgs> _dispatch;
        private RealTimeStylusAdapter? _adapter;

        public bool IsRunning { get; private set; }

        public bool IsSupported => Tablet.TabletDevices.Count > 0;

        public RealTimeStylusManager(InkCanvas canvas, ScrollViewer viewport, Action<InputStage, InputEventArgs> dispatch)
        {
            _canvas = canvas;
            _viewport = viewport;
            _dispatch = dispatch;
        }

        private static StylusPlugInCollection GetStylusPlugIns(UIElement element)
        {
            var propertyInfo = typeof(UIElement).GetProperty("StylusPlugIns", BindingFlags.NonPublic | BindingFlags.Instance);
            if (propertyInfo != null && propertyInfo.GetValue(element) is StylusPlugInCollection collection)
            {
                return collection;
            }
            throw new InvalidOperationException("无法访问 StylusPlugIns 属性");
        }

        public bool TryStart()
        {
            if (IsRunning)
            {
                return true;
            }

            try
            {
                _adapter ??= new RealTimeStylusAdapter(_canvas, _viewport, _dispatch);
                var stylusPlugIns = GetStylusPlugIns(_canvas);
                if (!stylusPlugIns.Contains(_adapter))
                {
                    stylusPlugIns.Add(_adapter);
                }

                IsRunning = true;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RTS] Failed to start RealTimeStylus: {ex}");
                _adapter = null;
                IsRunning = false;
                return false;
            }
        }

        public void Stop()
        {
            if (!IsRunning)
            {
                return;
            }

            if (_adapter != null)
            {
                try
                {
                    var stylusPlugIns = GetStylusPlugIns(_canvas);
                    stylusPlugIns.Remove(_adapter);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[RTS] Failed to remove RealTimeStylus adapter: {ex}");
                }
            }

            IsRunning = false;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
