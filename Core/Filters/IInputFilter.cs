using WindBoard.Core.Input;
using WindBoard.Core.Modes;

namespace WindBoard.Core.Filters
{
    public interface IInputFilter
    {
        int Priority { get; }
        bool Handle(InputStage stage, InputEventArgs args, ModeController modeController);
    }
}
