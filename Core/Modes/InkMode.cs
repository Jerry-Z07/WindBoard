using System.Windows.Controls;
using System.Windows.Ink;
using WindBoard.Core.Input;

namespace WindBoard.Core.Modes
{
    public class InkMode : InteractionModeBase
    {
        private readonly InkCanvas _canvas;

        public InkMode(InkCanvas canvas)
        {
            _canvas = canvas;
        }

        public override string Name => "Ink";

        public override void SwitchOn()
        {
            _canvas.EditingMode = InkCanvasEditingMode.Ink;
            _canvas.UseCustomCursor = false;
            _canvas.ClearValue(Control.CursorProperty);
        }
    }
}
