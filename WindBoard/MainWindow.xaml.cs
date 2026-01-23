using Microsoft.UI.Xaml;

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

