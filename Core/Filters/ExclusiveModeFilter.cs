using System;
using WindBoard.Core.Input;
using WindBoard.Core.Modes;

namespace WindBoard.Core.Filters
{
    public class ExclusiveModeFilter : InputFilterBase
    {
        private readonly IInteractionMode _noMode;
        private readonly Func<InputEventArgs, bool> _shouldBlock;

        public ExclusiveModeFilter(IInteractionMode noMode, Func<InputEventArgs, bool>? shouldBlock = null)
        {
            _noMode = noMode;
            _shouldBlock = shouldBlock ?? (_ => false);
        }

        public override int Priority => 50;

        public override bool Handle(InputStage stage, InputEventArgs args, ModeController modeController)
        {
            if (_shouldBlock(args))
            {
                modeController.ActivateMode(_noMode);
            }

            if (stage == InputStage.Up)
            {
                modeController.ClearActiveMode();
            }

            return false;
        }
    }
}
