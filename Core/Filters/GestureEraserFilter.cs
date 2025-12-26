using System;
using WindBoard.Core.Input;
using WindBoard.Core.Modes;

namespace WindBoard.Core.Filters
{
    public class GestureEraserFilter : InputFilterBase
    {
        private readonly IInteractionMode _eraserMode;
        private readonly double _sizeThreshold;
        private bool _gestureActive;

        public GestureEraserFilter(IInteractionMode eraserMode, double sizeThreshold = 45.0)
        {
            _eraserMode = eraserMode;
            _sizeThreshold = sizeThreshold;
        }

        public override int Priority => 100;

        public override bool Handle(InputStage stage, InputEventArgs args, ModeController modeController)
        {
            if (args.DeviceType != InputDeviceType.Touch)
            {
                if (_gestureActive && stage == InputStage.Up)
                {
                    _gestureActive = false;
                    modeController.ClearActiveMode();
                }
                return false;
            }

            if (stage == InputStage.Down || stage == InputStage.Move)
            {
                double size = Math.Max(args.ContactSize?.Width ?? 0, args.ContactSize?.Height ?? 0);
                if (size >= _sizeThreshold)
                {
                    _gestureActive = true;
                    modeController.ActivateMode(_eraserMode);
                }
            }

            if (stage == InputStage.Up && _gestureActive)
            {
                _gestureActive = false;
                modeController.ClearActiveMode();
            }

            return false;
        }
    }
}
