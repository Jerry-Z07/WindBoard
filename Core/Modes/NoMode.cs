using System.Windows;
using WindBoard.Core.Input;

namespace WindBoard.Core.Modes
{
    public sealed class NoMode : InteractionModeBase
    {
        private readonly FrameworkElement _inputSurface;

        public NoMode(FrameworkElement inputSurface)
        {
            _inputSurface = inputSurface;
        }

        public override string Name => "None";

        public override void SwitchOn()
        {
            _inputSurface.ClearValue(FrameworkElement.CursorProperty);
        }
    }
}

