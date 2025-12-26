using WindBoard.Core.Input;
using WindBoard.Core.Modes;

namespace WindBoard.Core.Filters
{
    public abstract class InputFilterBase : IInputFilter
    {
        public virtual int Priority => 0;

        public virtual bool Handle(InputStage stage, InputEventArgs args, ModeController modeController)
        {
            return false;
        }
    }
}
