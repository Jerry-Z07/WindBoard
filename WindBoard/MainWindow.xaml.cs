using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using WindBoard.Board.Editing;
using WindBoard.Interaction;
using WindBoard.Settings;

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

        private readonly BoardWorkspace _workspace = new();
        private readonly ObservableCollection<PageListItem> _pageItems = new();
        private bool _isUpdatingPageSelection;
        private SettingsWindow? _settingsWindow;

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

            // 右侧 Dock：页面切换与管理
            PagePrevButton.Click += (_, _) => _workspace.TryMoveToPreviousPage();
            PageIndicatorButton.Click += OnPageIndicatorButtonClicked;
            PageNextButton.Click += (_, _) => _workspace.TryMoveToNextPage();
            AddButton.Click += OnAddClicked;

            InitializePages();

            // 与 XAML 默认值对齐：应用启动时默认进入书写模式。
            ApplyToolSelection(BoardTool.Pen);

            UpdateCommandStates();

            ApplyAppSettingsToCanvas();
            AppSettingsService.Instance.Changed += OnAppSettingsChanged;

            Closed += (_, _) =>
            {
                AppSettingsService.Instance.Changed -= OnAppSettingsChanged;

                // 以主窗口为“应用主生命周期”窗口：主窗口退出时同步关闭设置窗口，
                // 避免设置窗口残留导致进程不退出。
                try
                {
                    _settingsWindow?.Close();
                }
                catch
                {
                    // 忽略关闭失败：不阻断主窗口退出流程
                }

                // 关闭前尽量落盘一次，避免防抖未触发导致设置丢失。
                try
                {
                    AppSettingsService.Instance.SaveAsync().GetAwaiter().GetResult();
                }
                catch
                {
                    // 忽略保存失败：不阻断关闭流程
                }

                BoardCanvas.Dispose();
            };
        }

        private void OnAppSettingsChanged(object? sender, EventArgs e)
        {
            // 变更可能来自不同线程（例如未来接入后台同步），这里统一切回 UI 线程更新控件。
            if (!DispatcherQueue.TryEnqueue(ApplyAppSettingsToCanvas))
            {
                ApplyAppSettingsToCanvas();
            }
        }

        private void ApplyAppSettingsToCanvas()
        {
            BoardCanvas.CanvasBackgroundColor = AppSettingsService.Instance.GetCanvasBackgroundColor();
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

        private void OnSettingsClicked(object sender, RoutedEventArgs e)
        {
            _settingsWindow ??= CreateSettingsWindow();
            _settingsWindow.Activate();
        }

        private SettingsWindow CreateSettingsWindow()
        {
            var window = new SettingsWindow();
            window.Closed += (_, _) => _settingsWindow = null;
            return window;
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

        private void OnAddClicked(object sender, RoutedEventArgs e)
        {
            // “+”约定为新增页面。
            _workspace.AddPage();
        }

        private void OnPageIndicatorButtonClicked(object sender, RoutedEventArgs e)
        {
            // 页面管理弹出层锚定到右侧 Dock 容器，保证弹出区域正对 Dock 上方。
            FlyoutBase.ShowAttachedFlyout(PagesDockBorder);
        }

        private void InitializePages()
        {
            // 绑定 UI 数据源
            PagesListView.ItemsSource = _pageItems;

            _workspace.PagesChanged += OnWorkspacePagesChanged;
            _workspace.CurrentPageChanged += OnWorkspaceCurrentPageChanged;

            RefreshPageItems();
            ApplyCurrentPageToCanvas();
        }

        private void OnWorkspacePagesChanged()
        {
            RefreshPageItems();
        }

        private void OnWorkspaceCurrentPageChanged()
        {
            ApplyCurrentPageToCanvas();
            UpdatePageNavigator();
            SelectCurrentPageInListView();
        }

        private void RefreshPageItems()
        {
            _pageItems.Clear();
            for (int i = 0; i < _workspace.Pages.Count; i++)
            {
                _pageItems.Add(new PageListItem(_workspace.Pages[i], number: i + 1));
            }

            UpdatePageNavigator();
            SelectCurrentPageInListView();
        }

        private void ApplyCurrentPageToCanvas()
        {
            // 将当前页会话绑定到画板，确保渲染/撤销重做与页面一致。
            BoardCanvas.BindSession(_workspace.CurrentPage.Session);
            UpdatePageNavigator();
            SelectCurrentPageInListView();
        }

        private void UpdatePageNavigator()
        {
            int total = _workspace.Pages.Count;
            int current = _workspace.CurrentIndex + 1;

            // 只有一页时，右侧 Dock 仅展示“+”，避免信息噪音。
            bool showPager = total > 1;
            PagePrevButton.Visibility = showPager ? Visibility.Visible : Visibility.Collapsed;
            PageNextButton.Visibility = showPager ? Visibility.Visible : Visibility.Collapsed;
            PageIndicatorButton.Visibility = showPager ? Visibility.Visible : Visibility.Collapsed;

            if (!showPager)
            {
                PagesFlyout.Hide();
                return;
            }

            PageIndicatorText.Text = $"{current} / {total}";
            PagePrevButton.IsEnabled = _workspace.CurrentIndex > 0;
            PageNextButton.IsEnabled = _workspace.CurrentIndex < total - 1;
        }

        private void SelectCurrentPageInListView()
        {
            if (_isUpdatingPageSelection)
            {
                return;
            }

            _isUpdatingPageSelection = true;
            try
            {
                PagesListView.SelectedIndex = _workspace.CurrentIndex;

                if (PagesListView.SelectedItem is not null)
                {
                    PagesListView.ScrollIntoView(PagesListView.SelectedItem);
                }
            }
            finally
            {
                _isUpdatingPageSelection = false;
            }
        }

        private void OnPagesListItemClick(object sender, ItemClickEventArgs e)
        {
            if (_isUpdatingPageSelection)
            {
                return;
            }

            if (e.ClickedItem is not PageListItem item)
            {
                return;
            }

            int index = _pageItems.IndexOf(item);
            if (index < 0)
            {
                return;
            }

            _workspace.SetCurrentIndex(index);
        }

        private void OnDeletePageClicked(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not PageListItem item)
            {
                return;
            }

            if (item.Page is not BoardPage page)
            {
                return;
            }

            _workspace.RemovePage(page);
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
