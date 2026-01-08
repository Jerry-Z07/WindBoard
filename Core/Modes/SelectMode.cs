using System.Windows.Controls;
using System.Windows.Ink;
using WindBoard.Core.Input;

namespace WindBoard.Core.Modes
{
    public class SelectMode : InteractionModeBase
    {
        private readonly InkCanvas _canvas;

        public SelectMode(InkCanvas canvas)
        {
            _canvas = canvas;
        }

        public override string Name => "Select";

        public override void SwitchOn()
        {
            _canvas.EditingMode = InkCanvasEditingMode.Select;
            _canvas.UseCustomCursor = false;
            _canvas.ClearValue(Control.CursorProperty);
        }
    }
}
