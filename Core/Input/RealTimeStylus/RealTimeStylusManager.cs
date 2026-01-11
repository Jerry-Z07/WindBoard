using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WindBoard.Core.Input;

namespace WindBoard.Core.Input.RealTimeStylus
{
    public class RealTimeStylusManager : IDisposable
    {
        private readonly UIElement _inputSurface;
        private readonly ScrollViewer _viewport;
        private readonly Action<InputStage, InputEventArgs> _dispatch;
        private RealTimeStylusAdapter? _adapter;

        public bool IsRunning { get; private set; }

        public bool IsSupported => Tablet.TabletDevices.Count > 0;

        public RealTimeStylusManager(UIElement inputSurface, ScrollViewer viewport, Action<InputStage, InputEventArgs> dispatch)
        {
            _inputSurface = inputSurface;
            _viewport = viewport;
            _dispatch = dispatch;
        }

        public bool TryStart()
        {
            if (IsRunning)
            {
                return true;
            }

            try
            {
                _adapter ??= new RealTimeStylusAdapter(_inputSurface, _viewport, _dispatch);
                var stylusPlugIns = StylusPlugInsAccessor.Get(_inputSurface);
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
                    var stylusPlugIns = StylusPlugInsAccessor.Get(_inputSurface);
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
