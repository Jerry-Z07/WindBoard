using System;
using System.Collections.Generic;
using System.Linq;
using WindBoard.Core.Filters;
using WindBoard.Core.Modes;

namespace WindBoard.Core.Input
{
    public class InputManager
    {
        private readonly ModeController _modeController;
        private readonly List<IInputFilter> _filters = new();

        public bool InputSuppressed { get; set; }

        public event EventHandler<InputEventArgs>? PointerDown;
        public event EventHandler<InputEventArgs>? PointerMove;
        public event EventHandler<InputEventArgs>? PointerUp;
        public event EventHandler<InputEventArgs>? PointerHover;

        public InputManager(ModeController modeController)
        {
            _modeController = modeController;
        }

        public void RegisterFilter(IInputFilter filter)
        {
            _filters.Add(filter);
            _filters.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

        public bool Dispatch(InputStage stage, InputEventArgs args)
        {
            if (InputSuppressed)
            {
                return false;
            }

            if (RunFilters(stage, args))
            {
                return true;
            }

            switch (stage)
            {
                case InputStage.Down:
                    PointerDown?.Invoke(this, args);
                    _modeController.HandlePointerDown(args);
                    break;
                case InputStage.Move:
                    PointerMove?.Invoke(this, args);
                    _modeController.HandlePointerMove(args);
                    break;
                case InputStage.Up:
                    PointerUp?.Invoke(this, args);
                    _modeController.HandlePointerUp(args);
                    _modeController.ClearActiveMode();
                    break;
                case InputStage.Hover:
                    PointerHover?.Invoke(this, args);
                    _modeController.HandlePointerHover(args);
                    break;
            }

            return true;
        }

        private bool RunFilters(InputStage stage, InputEventArgs args)
        {
            return _filters.Any(filter => filter.Handle(stage, args, _modeController));
        }
    }
}
