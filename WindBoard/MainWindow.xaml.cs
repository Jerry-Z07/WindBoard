using System;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using WindBoard.Interaction;

namespace WindBoard
{
    public sealed partial class MainWindow : Window
    {
        private const double ClearCanvasSlideThumbInset = 6.0;
        private const double ClearCanvasSlideCompleteRatio = 0.90;
        private const int ClearCanvasSlideResetAnimationMs = 160;

        private bool _isEraserFlyoutOpen;
        private bool _isClearCanvasSlideEnabled;
        private uint? _clearCanvasSlidePointerId;
        private double _clearCanvasSlidePointerStartX;
        private double _clearCanvasSlideThumbStartX;
        private Storyboard? _clearCanvasSlideResetStoryboard;

        public MainWindow()
        {
            InitializeComponent();

            BoardCanvas.CommandStateChanged += (_, _) => UpdateCommandStates();

            // 主 Dock：工具切换（单选）
            SelectToolToggleButton.Click += (_, _) => ApplyToolSelection(BoardTool.Select);
            PenToolToggleButton.Click += (_, _) => ApplyToolSelection(BoardTool.Pen);
            EraserToggleButton.Click += OnEraserToolClicked;

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

            // 离开擦除模式时，收起擦除弹出层，避免残留在其它工具状态下。
            if (tool != BoardTool.Eraser)
            {
                TryHideEraserFlyout();
            }
        }

        private void UpdateCommandStates()
        {
            UndoButton.IsEnabled = BoardCanvas.CanUndo;
            RedoButton.IsEnabled = BoardCanvas.CanRedo;
            UpdateClearCanvasSlideState();
        }

        private void OnEraserToolClicked(object sender, RoutedEventArgs e)
        {
            // 逻辑约定：首次点击进入擦除；已在擦除模式下再次点击则弹出“清空画布”入口。
            bool alreadyEraser = BoardCanvas.Tool == BoardTool.Eraser;
            ApplyToolSelection(BoardTool.Eraser);

            if (!alreadyEraser)
            {
                return;
            }

            if (_isEraserFlyoutOpen)
            {
                TryHideEraserFlyout();
                return;
            }

            ResetClearCanvasSlide(false);
            UpdateClearCanvasSlideState();
            FlyoutBase.ShowAttachedFlyout(EraserToggleButton);
        }

        private void OnEraserFlyoutOpened(object sender, object e)
        {
            _isEraserFlyoutOpen = true;
            ResetClearCanvasSlide(false);
            UpdateClearCanvasSlideState();
        }

        private void OnEraserFlyoutClosed(object sender, object e)
        {
            _isEraserFlyoutOpen = false;
            ResetClearCanvasSlide(false);
        }

        private void OnClearCanvasThumbPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!_isClearCanvasSlideEnabled || ClearCanvasSlideThumbTransform is null || ClearCanvasSlideHost is null)
            {
                return;
            }

            if (_clearCanvasSlidePointerId is not null)
            {
                return;
            }

            _clearCanvasSlideResetStoryboard?.Stop();
            _clearCanvasSlideResetStoryboard = null;

            _clearCanvasSlidePointerId = e.Pointer.PointerId;
            _clearCanvasSlideThumbStartX = ClearCanvasSlideThumbTransform.X;
            _clearCanvasSlidePointerStartX = e.GetCurrentPoint(ClearCanvasSlideHost).Position.X;

            ClearCanvasSlideThumb?.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void OnClearCanvasThumbPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_clearCanvasSlidePointerId != e.Pointer.PointerId || ClearCanvasSlideThumbTransform is null || ClearCanvasSlideHost is null)
            {
                return;
            }

            double maxX = GetClearCanvasThumbMaxX();
            double currentX = e.GetCurrentPoint(ClearCanvasSlideHost).Position.X;
            double nextX = Math.Clamp(_clearCanvasSlideThumbStartX + (currentX - _clearCanvasSlidePointerStartX), 0, maxX);
            ClearCanvasSlideThumbTransform.X = nextX;
            e.Handled = true;
        }

        private void OnClearCanvasThumbPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_clearCanvasSlidePointerId != e.Pointer.PointerId)
            {
                return;
            }

            CompleteClearCanvasSlideGesture(shouldEvaluate: true);
            e.Handled = true;
        }

        private void OnClearCanvasThumbPointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            if (_clearCanvasSlidePointerId != e.Pointer.PointerId)
            {
                return;
            }

            CompleteClearCanvasSlideGesture(shouldEvaluate: false);
            e.Handled = true;
        }

        private void OnClearCanvasThumbPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (_clearCanvasSlidePointerId != e.Pointer.PointerId)
            {
                return;
            }

            CompleteClearCanvasSlideGesture(shouldEvaluate: false);
            e.Handled = true;
        }

        private void CompleteClearCanvasSlideGesture(bool shouldEvaluate)
        {
            _clearCanvasSlidePointerId = null;
            ClearCanvasSlideThumb?.ReleasePointerCaptures();

            if (ClearCanvasSlideThumbTransform is null)
            {
                return;
            }

            if (shouldEvaluate)
            {
                double maxX = GetClearCanvasThumbMaxX();
                bool reached = maxX > 0 && ClearCanvasSlideThumbTransform.X >= maxX * ClearCanvasSlideCompleteRatio;

                // 只有达到阈值时才执行清空，同时仍可通过撤销恢复。
                if (reached && BoardCanvas.CanClear)
                {
                    BoardCanvas.ClearAll();
                    TryHideEraserFlyout();
                    return;
                }
            }

            ResetClearCanvasSlide(true);
        }

        private void UpdateClearCanvasSlideState()
        {
            if (ClearCanvasSlideThumb is null || ClearCanvasSlideHost is null)
            {
                return;
            }

            bool canClear = BoardCanvas.CanClear;
            _isClearCanvasSlideEnabled = canClear;
            ClearCanvasSlideThumb.IsHitTestVisible = canClear;
            ClearCanvasSlideThumb.Opacity = canClear ? 1.0 : 0.55;
            ClearCanvasSlideHost.Opacity = canClear ? 1.0 : 0.55;

            if (!canClear && _clearCanvasSlidePointerId is not null)
            {
                // 过程中状态变更（例如清空/撤销后没有笔迹）时，强制结束拖动，避免卡住捕获。
                _clearCanvasSlidePointerId = null;
                ClearCanvasSlideThumb.ReleasePointerCaptures();
                ResetClearCanvasSlide(false);
            }
        }

        private double GetClearCanvasThumbMaxX()
        {
            if (ClearCanvasSlideHost is null || ClearCanvasSlideThumb is null)
            {
                return 0;
            }

            double hostWidth = ClearCanvasSlideHost.ActualWidth > 0 ? ClearCanvasSlideHost.ActualWidth : ClearCanvasSlideHost.Width;
            double thumbWidth = ClearCanvasSlideThumb.ActualWidth > 0 ? ClearCanvasSlideThumb.ActualWidth : ClearCanvasSlideThumb.Width;
            return Math.Max(0, hostWidth - thumbWidth - ClearCanvasSlideThumbInset * 2);
        }

        private void ResetClearCanvasSlide(bool animated)
        {
            if (ClearCanvasSlideThumbTransform is null)
            {
                return;
            }

            _clearCanvasSlideResetStoryboard?.Stop();
            _clearCanvasSlideResetStoryboard = null;

            if (!animated)
            {
                ClearCanvasSlideThumbTransform.X = 0;
                return;
            }

            var storyboard = new Storyboard();
            var animation = new DoubleAnimation
            {
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(ClearCanvasSlideResetAnimationMs)),
                EnableDependentAnimation = true,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };

            Storyboard.SetTarget(animation, ClearCanvasSlideThumbTransform);
            Storyboard.SetTargetProperty(animation, "X");
            storyboard.Children.Add(animation);
            _clearCanvasSlideResetStoryboard = storyboard;
            storyboard.Begin();
        }

        private void TryHideEraserFlyout()
        {
            FlyoutBase? flyout = FlyoutBase.GetAttachedFlyout(EraserToggleButton);
            flyout?.Hide();
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
