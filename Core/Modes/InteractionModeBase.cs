using WindBoard.Core.Input;

namespace WindBoard.Core.Modes
{
    public abstract class InteractionModeBase : IInteractionMode
    {
        public abstract string Name { get; }

        public virtual void SwitchOn() { }
        public virtual void SwitchOff() { }

        public virtual void OnPointerDown(InputEventArgs args) { }
        public virtual void OnPointerMove(InputEventArgs args) { }
        public virtual void OnPointerUp(InputEventArgs args) { }
        public virtual void OnPointerHover(InputEventArgs args) { }
    }
}
