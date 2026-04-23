using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using WindBoard.Board.Editing;
using WindBoard.Interaction;
using WindBoard.Logging;
using WindBoard.Localization;
using WindBoard.Settings;
using WindBoard.UI.Common;

namespace WindBoard
{
    public sealed partial class MainWindow : Window
    {
        private bool _isEraserFlyoutOpen;
        private bool _isPenFlyoutOpen;
        private bool _isPenThicknessSliderSyncing;
        private readonly ClearCanvasSlideController _clearCanvasSlideController;

        // 擦除模式：默认像素擦除；整笔擦除作为可选项。
        private readonly IBoardEraser _pixelEraser = new PixelStrokeEraser();
        private readonly IBoardEraser _wholeStrokeEraser = new WholeStrokeEraser();

        private readonly BoardWorkspace _workspace = new();
        private readonly ObservableCollection<PageListItem> _pageItems = new();
        private bool _isUpdatingPageSelection;
        private AppWindowTitleBar? _appWindowTitleBar;
        private SettingsWindow? _settingsWindow;

        public MainWindow()
        {
            InitializeComponent();
            ConfigureTitleBar();
            _clearCanvasSlideController = new ClearCanvasSlideController(
                new ClearCanvasSlideController.UiRefs
                {
                    Host = ClearCanvasSlideHost,
                    Thumb = ClearCanvasSlideThumb,
                    ThumbTransform = ClearCanvasSlideThumbTransform,
                },
                canCompleteClear: () => BoardCanvas.CanClear,
                onCompleted: () =>
                {
                    BoardCanvas.ClearAll();
                    TryHideEraserFlyout();
                });

            // 与 XAML 默认值对齐：默认像素擦除。
            BoardCanvas.Eraser = _pixelEraser;

            BoardCanvas.CommandStateChanged += (_, _) => UpdateCommandStates();

            // 主 Dock：工具切换（单选）
            SelectToolToggleButton.Click += (_, _) => ApplyToolSelection(BoardTool.Select);
            PenToolToggleButton.Click += OnPenToolClicked;
            EraserToggleButton.Click += OnEraserToolClicked;

            // 中部 Dock：撤销/重做
            UndoButton.Click += (_, _) => BoardCanvas.Undo();
            RedoButton.Click += (_, _) => BoardCanvas.Redo();

            // 左侧 Dock：窗口与入口
            MinimizeButton.Click += OnMinimizeButtonClicked;
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

            ApplyAppSettingsToUi();
            Activated += (_, _) =>
            {
                TryApplyStartupWindowModeIfNeeded();
                SyncTitleBarVisibilityFromWindowState();
                ApplyCamouflageSettingsToWindow();
                TryStartAutoUpdateCheckOnce();
            };
            AppSettingsService.Instance.Changed += OnAppSettingsChanged;

            Closed += (_, _) =>
            {
                AppLog.Info("App", "主窗口关闭：开始清理资源");

                AppSettingsService.Instance.Changed -= OnAppSettingsChanged;
                StopScreenAnnotationForMainWindowClose();

                // 以主窗口为“应用主生命周期”窗口：主窗口退出时同步关闭设置窗口，
                // 避免设置窗口残留导致进程不退出。
                try
                {
                    _settingsWindow?.Close();
                }
                catch (Exception ex)
                {
                    // 忽略关闭失败：不阻断主窗口退出流程
                    AppLog.Warn("App", "关闭设置窗口失败", ex);
                }

                // 关闭前尽量落盘一次，避免防抖未触发导致设置丢失。
                try
                {
                    AppSettingsService.Instance.SaveAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    // 忽略保存失败：不阻断关闭流程
                    AppLog.Warn("Settings", "主窗口关闭时保存设置失败", ex);
                }

                try
                {
                    _camouflageFlow?.Dispose();
                }
                catch (Exception ex)
                {
                    // 忽略释放失败：不阻断关闭流程
                    AppLog.Debug("Camouflage", "释放 CamouflageFlow 失败", ex);
                }

                try
                {
                    BoardCanvas.Dispose();
                }
                catch (Exception ex)
                {
                    AppLog.Warn("App", "释放 BoardCanvas 失败", ex);
                }
            };
        }

        private void OnAppSettingsChanged(object? sender, EventArgs e)
        {
            // 变更可能来自不同线程（例如未来接入后台同步），这里统一切回 UI 线程更新控件。
            if (!DispatcherQueue.TryEnqueue(ApplyAppSettingsToUi))
            {
                ApplyAppSettingsToUi();
            }
        }

        private void ApplyAppSettingsToUi()
        {
            Color canvasBackgroundColor = AppSettingsService.Instance.GetCanvasBackgroundColor();
            BoardCanvas.CanvasBackgroundColor = canvasBackgroundColor;
            BoardCanvas.ElementCardTheme = AppSettingsService.Instance.GetElementCardTheme();
            UpdateCanvasBackgroundBrush(canvasBackgroundColor);
            ApplyDockSettingsToUi();
            ApplyCamouflageSettingsToWindow();
            ApplyKeyboardShortcutsToUi();
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

            // 离开书写模式时，收起书写弹出层，避免残留在其它工具状态下。
            if (tool != BoardTool.Pen)
            {
                TryHidePenFlyout();
            }
        }

        private void UpdateCommandStates()
        {
            UndoButton.IsEnabled = BoardCanvas.CanUndo;
            RedoButton.IsEnabled = BoardCanvas.CanRedo;
            UpdateClearCanvasSlideState();
        }

        private void OnPenToolClicked(object sender, RoutedEventArgs e)
        {
            // 逻辑约定：首次点击进入书写；已在书写模式下再次点击则弹出“颜色/粗细”面板。
            bool alreadyPen = BoardCanvas.Tool == BoardTool.Pen;
            ApplyToolSelection(BoardTool.Pen);

            if (!alreadyPen)
            {
                return;
            }

            if (_isPenFlyoutOpen)
            {
                TryHidePenFlyout();
                return;
            }

            ApplyPenFlyoutSettings();
            SyncPenFlyoutFromCanvas();
            FlyoutBase.ShowAttachedFlyout(PenToolToggleButton);
        }

        private void OnPenFlyoutOpened(object sender, object e)
        {
            _isPenFlyoutOpen = true;
            ApplyPenFlyoutSettings();
            SyncPenFlyoutFromCanvas();
        }

        private void OnPenFlyoutClosed(object sender, object e)
        {
            _isPenFlyoutOpen = false;
        }

        private void OnPenThicknessClicked(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton button)
            {
                return;
            }

            if (!TryParseFloatTag(button.Tag, out float size))
            {
                return;
            }

            BoardCanvas.PenBaseSize = size;
            SetExclusiveToggleChecked(PenThicknessPanel, button);
        }

        private void OnPenThicknessSliderValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isPenThicknessSliderSyncing)
            {
                return;
            }

            if (PenThicknessSliderPanel.Visibility != Visibility.Visible)
            {
                return;
            }

            BoardCanvas.PenBaseSize = (float)e.NewValue;
        }

        private void OnPenColorClicked(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton button)
            {
                return;
            }

            if (button.Tag is not string hex || !ColorHex.TryParse(hex, out Color color))
            {
                return;
            }

            BoardCanvas.PenColor = color;
            SetExclusiveToggleChecked(PenColorGrid, button);
        }

        private void ApplyPenFlyoutSettings()
        {
            // 每次打开 Flyout 时按设置重建一次：
            // - 色板数量 3~24 可变，且允许空色块
            // - 粗细可在“三档预设 / 滑条”之间切换
            PenSettingsSnapshot snapshot = AppSettingsService.Instance.GetPenSettingsSnapshot();

            ApplyPenPaletteToFlyout(snapshot.PaletteHexes);
            ApplyPenThicknessToFlyout(snapshot);
        }

        private void ApplyPenPaletteToFlyout(IReadOnlyList<string?> paletteHexes)
        {
            PenColorGrid.Children.Clear();
            PenColorGrid.RowDefinitions.Clear();
            PenColorGrid.ColumnDefinitions.Clear();

            int count = paletteHexes.Count;
            if (count <= 0)
            {
                return;
            }

            int columns = ComputePaletteColumns(count);
            int rows = (int)Math.Ceiling(count / (double)columns);

            for (int c = 0; c < columns; c++)
            {
                PenColorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }

            for (int r = 0; r < rows; r++)
            {
                PenColorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            for (int i = 0; i < count; i++)
            {
                ToggleButton button = CreatePenColorSwatchButton(paletteHexes[i]);
                int row = i / columns;
                int col = i % columns;
                Grid.SetRow(button, row);
                Grid.SetColumn(button, col);
                PenColorGrid.Children.Add(button);
            }
        }

        private ToggleButton CreatePenColorSwatchButton(string? hex)
        {
            var button = new ToggleButton
            {
                Style = GetRequiredStyle("SharedPenColorSwatchToggleButtonStyle"),
                ClickMode = ClickMode.Release,
            };
            button.Click += OnPenColorClicked;

            var ellipse = new Ellipse { Margin = new Thickness(2) };

            if (ColorHex.TryParse(hex, out Color color))
            {
                string normalized = ColorHex.ToHexRgb(color);
                button.Tag = normalized;
                button.IsEnabled = true;
                ellipse.Fill = new SolidColorBrush(Color.FromArgb(0xFF, color.R, color.G, color.B));
            }
            else
            {
                // 空色块：保留描边但禁用点击，避免选中到“无颜色”。
                button.Tag = null;
                button.IsEnabled = false;
                ellipse.Fill = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            }

            button.Content = ellipse;
            return button;
        }

        private void ApplyPenThicknessToFlyout(PenSettingsSnapshot snapshot)
        {
            PenThicknessPresetsPanel.Visibility = snapshot.UseThicknessSlider
                ? Visibility.Collapsed
                : Visibility.Visible;
            PenThicknessSliderPanel.Visibility = snapshot.UseThicknessSlider
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (!snapshot.UseThicknessSlider)
            {
                BuildPenThicknessPresetButtons(snapshot.ThicknessPresets);
            }
        }

        private void BuildPenThicknessPresetButtons(float[] presets)
        {
            PenThicknessPanel.Children.Clear();

            for (int i = 0; i < presets.Length; i++)
            {
                float size = presets[i];
                var button = new ToggleButton
                {
                    Tag = size,
                    Style = GetRequiredStyle("SharedPenThicknessToggleButtonStyle"),
                };
                button.Click += OnPenThicknessClicked;

                // 用线段粗细表达“档位粗细”。
                var line = new Line
                {
                    X1 = 12,
                    Y1 = 22,
                    X2 = 32,
                    Y2 = 22,
                    Stroke = new SolidColorBrush(Color.FromArgb(0xFF, 0, 0, 0)),
                    StrokeThickness = size,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                };

                button.Content = line;
                PenThicknessPanel.Children.Add(button);
            }
        }

        private static int ComputePaletteColumns(int count)
        {
            int columns = (int)Math.Ceiling(Math.Sqrt(count));
            columns = Math.Clamp(columns, 3, 6);
            return columns;
        }

        private void SyncPenFlyoutFromCanvas()
        {
            // 书写 Flyout 可能在工具切换/设置恢复等场景下被动打开，这里统一以画布当前值为准做一次同步。
            Color currentColor = BoardCanvas.PenColor;
            foreach (UIElement element in PenColorGrid.Children)
            {
                if (element is ToggleButton button
                    && button.Tag is string hex
                    && ColorHex.TryParse(hex, out Color color))
                {
                    button.IsChecked = color.A == currentColor.A
                        && color.R == currentColor.R
                        && color.G == currentColor.G
                        && color.B == currentColor.B;
                 }
             }

            float currentSize = BoardCanvas.PenBaseSize;

            if (PenThicknessSliderPanel.Visibility == Visibility.Visible)
            {
                _isPenThicknessSliderSyncing = true;
                try
                {
                    double clamped = Math.Clamp(currentSize, PenThicknessSlider.Minimum, PenThicknessSlider.Maximum);
                    if (Math.Abs(PenThicknessSlider.Value - clamped) > 0.001)
                    {
                        PenThicknessSlider.Value = clamped;
                    }
                }
                finally
                {
                    _isPenThicknessSliderSyncing = false;
                }

                return;
            }

            foreach (UIElement element in PenThicknessPanel.Children)
            {
                if (element is ToggleButton button && TryParseFloatTag(button.Tag, out float size))
                {
                    button.IsChecked = Math.Abs(currentSize - size) < 0.001f;
                }
            }
        }

        private static bool TryParseFloatTag(object? tag, out float value)
        {
            value = 0;

            if (tag is null)
            {
                return false;
            }

            return float.TryParse(tag.ToString(), out value);
        }

        private static void SetExclusiveToggleChecked(Panel panel, ToggleButton checkedButton)
        {
            foreach (UIElement child in panel.Children)
            {
                if (child is ToggleButton button)
                {
                    button.IsChecked = ReferenceEquals(button, checkedButton);
                }
            }
        }

        private static Style GetRequiredStyle(string key)
        {
            if (Application.Current.Resources[key] is Style style)
            {
                return style;
            }

            throw new InvalidOperationException($"找不到共享样式资源：{key}");
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

        private void OnEraserModeChecked(object sender, RoutedEventArgs e)
        {
            // 入口位于擦除 Flyout 中：仅切换擦除算法，不影响当前工具状态。
            if (PixelEraserRadioButton?.IsChecked == true)
            {
                BoardCanvas.Eraser = _pixelEraser;
                return;
            }

            if (StrokeEraserRadioButton?.IsChecked == true)
            {
                BoardCanvas.Eraser = _wholeStrokeEraser;
            }
        }


        private void TryHideEraserFlyout()
        {
            FlyoutBase? flyout = FlyoutBase.GetAttachedFlyout(EraserToggleButton);
            flyout?.Hide();
        }

        private void TryHidePenFlyout()
        {
            FlyoutBase? flyout = FlyoutBase.GetAttachedFlyout(PenToolToggleButton);
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
            await StartExportAsync();
        }

        private void OnExitClicked(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MinimizeWindow()
        {
            // WinUI 3 桌面端没有直接的 Window.Minimize：
            // 1) 普通窗口（Overlapped Presenter）时可直接调用 OverlappedPresenter.Minimize。
            // 2) 临时全屏等模式下 Presenter 不是 OverlappedPresenter，会导致最小化按钮无效；这里增加 Win32 兜底。
            try
            {
                AppWindow? appWindow = TryGetAppWindow();
                if (appWindow?.Presenter is OverlappedPresenter presenter)
                {
                    presenter.Minimize();
                    return;
                }

                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                if (hwnd == IntPtr.Zero)
                {
                    AppLog.Warn("Dock", "窗口最小化失败：无法获取窗口句柄（可能尚未就绪）");
                    return;
                }

                // 兜底：直接最小化 HWND（避免全屏 Presenter 下无法最小化）。
                if (ShowWindow(hwnd, SwMinimize))
                {
                    return;
                }

                int lastError = Marshal.GetLastWin32Error();
                AppLog.Warn("Dock", $"窗口最小化失败：ShowWindow 返回 false，lastError={lastError}");
            }
            catch (Exception ex)
            {
                AppLog.Warn("Dock", "窗口最小化失败：发生异常", ex);
            }
        }

        private const int SwMinimize = 6; // SW_MINIMIZE

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private async void OnImportClicked(object sender, RoutedEventArgs e)
        {
            await StartImportAsync();
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
                Title = L10n.Get("MainWindow_FeatureWip_Title"),
                Content = L10n.Format("MainWindow_FeatureWip_Content_Fmt", featureName),
                CloseButtonText = L10n.Get("Common_Close"),
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
