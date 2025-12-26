using WindBoard.Core.Input;

namespace WindBoard.Core.Modes
{
    public interface IInteractionMode
    {
        string Name { get; }
        void SwitchOn();
        void SwitchOff();
        void OnPointerDown(InputEventArgs args);
        void OnPointerMove(InputEventArgs args);
        void OnPointerUp(InputEventArgs args);
        void OnPointerHover(InputEventArgs args);
    }
}
