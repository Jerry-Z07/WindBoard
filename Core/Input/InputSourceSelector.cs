using WindBoard.Core.Input.RealTimeStylus;

namespace WindBoard.Core.Input
{
    public enum InputSourceKind
    {
        Wpf,
        RealTimeStylus
    }

    public class InputSourceSelector
    {
        private readonly RealTimeStylusManager _stylusManager;

        public InputSourceKind ActiveSource { get; private set; } = InputSourceKind.Wpf;

        public bool IsRealTimeStylusActive => ActiveSource == InputSourceKind.RealTimeStylus && _stylusManager.IsRunning;

        public bool ShouldHandleWpfStylus => ActiveSource == InputSourceKind.Wpf || !_stylusManager.IsRunning;

        public InputSourceSelector(RealTimeStylusManager stylusManager)
        {
            _stylusManager = stylusManager;
        }

        public void InitializeAuto()
        {
            if (_stylusManager.IsSupported && _stylusManager.TryStart())
            {
                ActiveSource = InputSourceKind.RealTimeStylus;
            }
            else
            {
                ActiveSource = InputSourceKind.Wpf;
            }
        }

        public bool SwitchTo(InputSourceKind source)
        {
            if (source == InputSourceKind.RealTimeStylus)
            {
                if (_stylusManager.IsSupported && _stylusManager.TryStart())
                {
                    ActiveSource = InputSourceKind.RealTimeStylus;
                    return true;
                }

                ActiveSource = InputSourceKind.Wpf;
                return false;
            }

            _stylusManager.Stop();
            ActiveSource = InputSourceKind.Wpf;
            return true;
        }
    }
}
