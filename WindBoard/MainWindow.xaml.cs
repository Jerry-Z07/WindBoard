using System;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindBoard.Interaction;

namespace WindBoard
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            BoardCanvas.CommandStateChanged += (_, _) => UpdateCommandStates();

            // 主 Dock：工具切换（单选）
            SelectToolToggleButton.Click += (_, _) => ApplyToolSelection(BoardTool.Select);
            PenToolToggleButton.Click += (_, _) => ApplyToolSelection(BoardTool.Pen);
            EraserToggleButton.Click += (_, _) => ApplyToolSelection(BoardTool.Eraser);

            // 中部 Dock：撤销/重做
            UndoButton.Click += (_, _) => BoardCanvas.Undo();
            RedoButton.Click += (_, _) => BoardCanvas.Redo();

            // 左侧 Dock：窗口与入口
            MinimizeButton.Click += (_, _) => MinimizeWindow();
            ImportButton.Click += OnImportClicked;

            // 右侧 Dock：预留入口
            AddButton.Click += OnAddClicked;

            // 与 XAML 默认值对齐：应用启动时默认进入书写模式。
            ApplyToolSelection(BoardTool.Pen);

            UpdateCommandStates();

            Closed += (_, _) => BoardCanvas.Dispose();
        }

        private void ApplyToolSelection(BoardTool tool)
        {
            // ToggleButton 默认允许“再次点击取消勾选”，这里强制做成类似单选的行为。
            SelectToolToggleButton.IsChecked = tool == BoardTool.Select;
            PenToolToggleButton.IsChecked = tool == BoardTool.Pen;
            EraserToggleButton.IsChecked = tool == BoardTool.Eraser;

            BoardCanvas.Tool = tool;
        }

        private void UpdateCommandStates()
        {
            UndoButton.IsEnabled = BoardCanvas.CanUndo;
            RedoButton.IsEnabled = BoardCanvas.CanRedo;
        }

        private async void OnSettingsClicked(object sender, RoutedEventArgs e)
        {
            await ShowNotImplementedDialogAsync("设置");
        }

        private async void OnExportClicked(object sender, RoutedEventArgs e)
        {
            await ShowNotImplementedDialogAsync("导出");
        }

        private void OnExitClicked(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MinimizeWindow()
        {
            // WinUI 3 桌面端没有直接的 Window.Minimize，这里通过 AppWindow 的 Presenter 进行最小化。
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Minimize();
            }
        }

        private async void OnImportClicked(object sender, RoutedEventArgs e)
        {
            await ShowNotImplementedDialogAsync("导入");
        }

        private async void OnAddClicked(object sender, RoutedEventArgs e)
        {
            await ShowNotImplementedDialogAsync("新增/插入");
        }

        private async Task ShowNotImplementedDialogAsync(string featureName)
        {
            XamlRoot? xamlRoot = TryGetDialogXamlRoot();
            if (xamlRoot is null)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "功能开发中",
                Content = $"{featureName} 功能暂未实现，已预留入口，后续会逐步补齐。",
                CloseButtonText = "关闭",
                XamlRoot = xamlRoot,
            };

            await dialog.ShowAsync();
        }

        private XamlRoot? TryGetDialogXamlRoot()
        {
            // ContentDialog 在 WinUI 3 中必须指定 XamlRoot。
            if (Content is FrameworkElement root && root.XamlRoot is not null)
            {
                return root.XamlRoot;
            }

            return BoardCanvas.XamlRoot;
        }
    }
}
