using Microsoft.UI.Xaml;
using WindBoard.Interaction;

namespace WindBoard
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            BoardCanvas.CommandStateChanged += (_, _) => UpdateCommandStates();

            UndoButton.Click += (_, _) => BoardCanvas.Undo();
            RedoButton.Click += (_, _) => BoardCanvas.Redo();
            ClearButton.Click += (_, _) => BoardCanvas.ClearAll();

            // 目前先做“整笔擦除”，后续可通过设置系统切换为局部擦除：这里仅保留工具切换入口。
            EraserToggleButton.Checked += (_, _) => BoardCanvas.Tool = BoardTool.Eraser;
            EraserToggleButton.Unchecked += (_, _) => BoardCanvas.Tool = BoardTool.Pen;

            UpdateCommandStates();

            Closed += (_, _) => BoardCanvas.Dispose();
        }

        private void UpdateCommandStates()
        {
            UndoButton.IsEnabled = BoardCanvas.CanUndo;
            RedoButton.IsEnabled = BoardCanvas.CanRedo;
            ClearButton.IsEnabled = BoardCanvas.CanClear;
        }
    }
}
