using WindBoard.Core.Input;

namespace WindBoard.Core.Modes
{
    public class ModeController
    {
        public IInteractionMode? CurrentMode { get; private set; }
        public IInteractionMode? ActiveMode { get; private set; }

        public void SetCurrentMode(IInteractionMode? mode)
        {
            if (ReferenceEquals(CurrentMode, mode))
            {
                return;
            }

            CurrentMode?.SwitchOff();
            CurrentMode = mode;
            CurrentMode?.SwitchOn();
        }

        public void ActivateMode(IInteractionMode? mode)
        {
            if (ReferenceEquals(ActiveMode, mode))
            {
                return;
            }

            ActiveMode?.SwitchOff();
            ActiveMode = mode;
            ActiveMode?.SwitchOn();
        }

        public void ClearActiveMode()
        {
            if (ActiveMode == null)
            {
                return;
            }

            ActiveMode.SwitchOff();
            ActiveMode = null;
            CurrentMode?.SwitchOn();
        }

        public void HandlePointerDown(InputEventArgs args)
        {
            (ActiveMode ?? CurrentMode)?.OnPointerDown(args);
        }

        public void HandlePointerMove(InputEventArgs args)
        {
            (ActiveMode ?? CurrentMode)?.OnPointerMove(args);
        }

        public void HandlePointerUp(InputEventArgs args)
        {
            (ActiveMode ?? CurrentMode)?.OnPointerUp(args);
        }

        public void HandlePointerHover(InputEventArgs args)
        {
            (ActiveMode ?? CurrentMode)?.OnPointerHover(args);
        }
    }
}
