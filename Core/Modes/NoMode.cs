using System.Windows.Controls;
using System.Windows.Ink;
using WindBoard.Core.Input;

namespace WindBoard.Core.Modes
{
    public class NoMode : InteractionModeBase
    {
        private readonly InkCanvas _canvas;

        public NoMode(InkCanvas canvas)
        {
            _canvas = canvas;
        }

        public override string Name => "None";

        public override void SwitchOn()
        {
            _canvas.EditingMode = InkCanvasEditingMode.None;
            _canvas.UseCustomCursor = false;
            _canvas.ClearValue(Control.CursorProperty);
        }
    }
}
