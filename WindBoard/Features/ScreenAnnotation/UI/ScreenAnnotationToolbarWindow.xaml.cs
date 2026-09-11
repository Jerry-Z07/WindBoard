using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Graphics;
using Windows.UI;
using WindBoard.Features.ScreenAnnotation.Interop;
using WindBoard.Features.ScreenAnnotation.Models;
using WindBoard.Features.ScreenAnnotation.Services;
using WindBoard.Features.ScreenAnnotation.UI.Backdrop;
using WindBoard.Logging;
using WindBoard.Settings;
using WindBoard.UI.Common;

namespace WindBoard.Features.ScreenAnnotation.UI
{
    /// <summary>
    /// 屏幕批注悬浮工具栏。
    /// </summary>
    public sealed partial class ScreenAnnotationToolbarWindow : Window, IScreenAnnotationModeToolbar
    {
        // 展开态宽度构成：内容 Margin 6×2 + 拖拽把手 44 + 间距 4 + 分组分隔线 1 + 间距 4 + 按钮区 236（5×44 + 4×4）= 301；高度 = 元素 44 + Margin 6×2 = 56。
        private const double ExpandedToolbarWidthDip = 301;
        private const double ToolbarHeightDip = 56;
        private const uint DefaultWindowDpi = 96;

        private readonly ScreenAnnotationDisplayTarget _displayTarget;
        private ScreenAnnotationTransparentBackdrop? _transparentBackdrop;
        private ScreenAnnotationMode _selectedMode = ScreenAnnotationMode.PassThrough;
        private bool _isWindowInitialized;
        private bool _isCollapsed;
        private bool _isPenFlyoutOpen;
        private bool _isEraserFlyoutOpen;
        private bool _isShapeFlyoutOpen;
        private bool _isPenThicknessSliderSyncing;
        // 最近一次使用的形状模式（形状按钮首次点击进入；默认直线）。
        private ScreenAnnotationMode _lastShapeMode = ScreenAnnotationMode.Line;
        private bool _isEraserModeSyncing;
        private uint? _dragPointerId;
        private PointInt32 _dragStartCursor;
        private PointInt32 _dragStartWindowOrigin;
        private bool _dragMoved;
        private readonly ClearCanvasSlideController _clearCanvasSlideController;
        private ScreenAnnotationDrawingStateSnapshot _drawingState = new(
            PenColor: Color.FromArgb(0xFF, 0x00, 0x00, 0x00),
            PenBaseSize: 3.0f,
            EraserMode: ScreenAnnotationEraserMode.Pixel,
            CanClear: false);

        internal ScreenAnnotationToolbarWindow(ScreenAnnotationDisplayTarget displayTarget)
        {
            _displayTarget = displayTarget;

            InitializeComponent();
            _clearCanvasSlideController = new ClearCanvasSlideController(
                new ClearCanvasSlideController.UiRefs
                {
                    Host = ClearCanvasSlideHost,
                    Thumb = ClearCanvasSlideThumb,
                    ThumbTransform = ClearCanvasSlideThumbTransform,
                },
                canCompleteClear: () => _drawingState.CanClear,
                onCompleted: () =>
                {
                    ClearCanvasRequested?.Invoke();
                    TryHideEraserFlyout();
                });
            Activated += OnWindowActivated;
            Closed += OnWindowClosed;

            // 形状图标描边同步：初始（Loaded）与选中切换（SetSelectedMode）两路触发。
            ShapeButton.Loaded += (_, _) => UpdateShapeIconStroke();
        }

        /// <summary>
        /// 同步形状工具图标的描边笔刷（自绘 Path 不像 FontIcon 经内容前景继承跟随选中视觉状态）。
        /// </summary>
        /// <remarks>
        /// 工具栏为恒亮主题（根 Grid RequestedTheme=Light）：非选中=BaseHigh（黑），选中=纯白。
        /// 与主白板 <c>MainWindow.UpdateShapeIconStroke</c> 同构；触发点：ShapeButton.Loaded 与
        /// <see cref="SetSelectedMode"/>（选中切换）。
        /// </remarks>
        private void UpdateShapeIconStroke()
        {
            bool isChecked = ShapeButton.IsChecked == true;
            bool isLightTheme = ShapeButton.ActualTheme == ElementTheme.Light;
            ShapeIconPath.Stroke = isChecked
                ? ShapeIconStrokeCheckedBrush
                : isLightTheme
                    ? ShapeIconStrokeUncheckedLightThemeBrush
                    : ShapeIconStrokeUncheckedDarkThemeBrush;
        }

        private static readonly SolidColorBrush ShapeIconStrokeCheckedBrush = new(Microsoft.UI.Colors.White);

        // BaseHigh 的主题值：亮主题=黑（90% 不透明），暗主题=白（100%）。工具栏恒亮，实际恒取亮值。
        private static readonly SolidColorBrush ShapeIconStrokeUncheckedLightThemeBrush = new(Color.FromArgb(0xE6, 0, 0, 0));
        private static readonly SolidColorBrush ShapeIconStrokeUncheckedDarkThemeBrush = new(Color.FromArgb(0xFF, 255, 255, 255));

        internal event EventHandler<ScreenAnnotationMode>? ModeRequested;

        internal event Action<Color>? PenColorRequested;

        internal event Action<float>? PenBaseSizeRequested;

        internal event Action<ScreenAnnotationEraserMode>? EraserModeRequested;

        internal event Action? ClearCanvasRequested;

        internal event EventHandler? ReturnToAppRequested;

        internal void SetSelectedMode(ScreenAnnotationMode mode)
        {
            _selectedMode = mode;
            PassThroughButton.IsChecked = mode == ScreenAnnotationMode.PassThrough;
            PenButton.IsChecked = mode == ScreenAnnotationMode.Pen;
            EraserButton.IsChecked = mode == ScreenAnnotationMode.Eraser;
            ShapeButton.IsChecked = mode.IsShapeMode();
            UpdateShapeIconStroke();

            // 记录最近一次使用的形状模式，供形状按钮下次一键回到该形状。
            if (mode.IsShapeMode())
            {
                _lastShapeMode = mode;
            }

            if (mode != ScreenAnnotationMode.Pen)
            {
                TryHidePenFlyout();
            }

            if (mode != ScreenAnnotationMode.Eraser)
            {
                TryHideEraserFlyout();
            }

            if (!mode.IsShapeMode())
            {
                TryHideShapeFlyout();
            }
        }

        internal void SyncDrawingState(ScreenAnnotationDrawingStateSnapshot state)
        {
            _drawingState = state;

            if (_isPenFlyoutOpen)
            {
                SyncPenFlyoutFromState();
            }

            if (_isEraserFlyoutOpen)
            {
                SyncEraserFlyoutFromState();
                return;
            }

            UpdateClearCanvasSlideState();
        }

        internal void EnsureInteractiveTopMost(IScreenAnnotationModeOverlay? overlay)
        {
            IntPtr hwnd = ScreenAnnotationWindowInterop.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            if (!ScreenAnnotationWindowInterop.TryPromoteWindowToTopMost(hwnd, out string? error))
            {
                AppLog.Warn("ScreenAnnotation.Interop", $"刷新工具栏顶层顺序失败：error='{error}'");
                return;
            }

            if (overlay is null || !overlay.TryGetWindowHandle(out IntPtr overlayHwnd))
            {
                return;
            }

            if (!ScreenAnnotationWindowInterop.TryPlaceWindowBehind(overlayHwnd, hwnd, out string? stackError))
            {
                AppLog.Warn("ScreenAnnotation.Interop", $"恢复工具栏与批注层相对层级失败：error='{stackError}'");
            }
        }

        internal bool TryGetWindowHandle(out IntPtr hwnd)
        {
            hwnd = ScreenAnnotationWindowInterop.GetWindowHandle(this);
            return hwnd != IntPtr.Zero;
        }

        private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
        {
            if (_isWindowInitialized)
            {
                return;
            }

            IntPtr hwnd = ScreenAnnotationWindowInterop.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var appWindow = ScreenAnnotationWindowInterop.TryGetAppWindow(hwnd);
            if (appWindow is null)
            {
                return;
            }

            RectInt32 bounds = BuildInitialToolbarBounds(hwnd);
            if (!ScreenAnnotationWindowInterop.TryConfigureBorderlessWindow(appWindow, bounds, out string? windowError))
            {
                AppLog.Warn("ScreenAnnotation.Interop", $"配置工具栏窗口失败：error='{windowError}'");
                CloseWindowAfterInitializationFailure();
                return;
            }

            if (!TryAttachTransparentBackdrop(hwnd, out string? backdropError))
            {
                AppLog.Warn("ScreenAnnotation.Interop", $"初始化工具栏透明背景失败：error='{backdropError}'");
                CloseWindowAfterInitializationFailure();
                return;
            }

            if (!ScreenAnnotationWindowInterop.TryPrepareToolbarWindow(hwnd, out string? interopError))
            {
                AppLog.Warn("ScreenAnnotation.Interop", $"初始化工具栏原生样式失败：error='{interopError}'");
                CloseWindowAfterInitializationFailure();
                return;
            }

            SetSelectedMode(ScreenAnnotationMode.PassThrough);
            _isWindowInitialized = true;
        }

        private void OnPassThroughButtonClicked(object sender, RoutedEventArgs e)
        {
            ModeRequested?.Invoke(this, ScreenAnnotationMode.PassThrough);
        }

        private void OnPenButtonClicked(object sender, RoutedEventArgs e)
        {
            bool alreadyPen = ScreenAnnotationToolbarBehavior.IsSecondaryClick(_selectedMode, ScreenAnnotationMode.Pen);
            ModeRequested?.Invoke(this, ScreenAnnotationMode.Pen);

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
            SyncPenFlyoutFromState();
            FlyoutBase.ShowAttachedFlyout(PenButton);
        }

        private void OnPenFlyoutOpened(object sender, object e)
        {
            _isPenFlyoutOpen = true;
            ApplyPenFlyoutSettings();
            SyncPenFlyoutFromState();
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

            _drawingState = _drawingState with { PenBaseSize = size };
            PenBaseSizeRequested?.Invoke(size);
            SetExclusiveToggleChecked(PenThicknessPanel, button);
        }

        private void OnPenThicknessSliderValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isPenThicknessSliderSyncing || PenThicknessSliderPanel.Visibility != Visibility.Visible)
            {
                return;
            }

            float size = (float)e.NewValue;
            _drawingState = _drawingState with { PenBaseSize = size };
            PenBaseSizeRequested?.Invoke(size);
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

            Color normalized = Color.FromArgb(0xFF, color.R, color.G, color.B);
            _drawingState = _drawingState with { PenColor = normalized };
            PenColorRequested?.Invoke(normalized);
            SetExclusiveToggleChecked(PenColorGrid, button);
        }

        private void ApplyPenFlyoutSettings()
        {
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

            foreach (float size in presets)
            {
                var button = new ToggleButton
                {
                    Tag = size,
                    Style = GetRequiredStyle("SharedPenThicknessToggleButtonStyle"),
                };
                button.Click += OnPenThicknessClicked;

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
            return Math.Clamp(columns, 3, 6);
        }

        private void SyncPenFlyoutFromState()
        {
            Color currentColor = _drawingState.PenColor;
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

            float currentSize = _drawingState.PenBaseSize;
            if (PenThicknessSliderPanel.Visibility == Visibility.Visible)
            {
                _isPenThicknessSliderSyncing = true;
                try
                {
                    double clamped = Math.Clamp(currentSize, (float)PenThicknessSlider.Minimum, (float)PenThicknessSlider.Maximum);
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
            return tag is not null && float.TryParse(tag.ToString(), out value);
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

        private void OnEraserButtonClicked(object sender, RoutedEventArgs e)
        {
            bool alreadyEraser = ScreenAnnotationToolbarBehavior.IsSecondaryClick(_selectedMode, ScreenAnnotationMode.Eraser);
            ModeRequested?.Invoke(this, ScreenAnnotationMode.Eraser);

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
            SyncEraserFlyoutFromState();
            FlyoutBase.ShowAttachedFlyout(EraserButton);
        }

        private void OnEraserFlyoutOpened(object sender, object e)
        {
            _isEraserFlyoutOpen = true;
            ResetClearCanvasSlide(false);
            SyncEraserFlyoutFromState();
        }

        private void OnEraserFlyoutClosed(object sender, object e)
        {
            _isEraserFlyoutOpen = false;
            ResetClearCanvasSlide(false);
        }

        private void OnEraserModeChecked(object sender, RoutedEventArgs e)
        {
            if (_isEraserModeSyncing)
            {
                return;
            }

            ScreenAnnotationEraserMode mode = PixelEraserRadioButton?.IsChecked == true
                ? ScreenAnnotationEraserMode.Pixel
                : ScreenAnnotationEraserMode.WholeStroke;

            _drawingState = _drawingState with { EraserMode = mode };
            EraserModeRequested?.Invoke(mode);
        }

        private void SyncEraserFlyoutFromState()
        {
            _isEraserModeSyncing = true;
            try
            {
                bool isPixel = _drawingState.EraserMode == ScreenAnnotationEraserMode.Pixel;
                if (PixelEraserRadioButton is not null)
                {
                    PixelEraserRadioButton.IsChecked = isPixel;
                }

                if (StrokeEraserRadioButton is not null)
                {
                    StrokeEraserRadioButton.IsChecked = !isPixel;
                }
            }
            finally
            {
                _isEraserModeSyncing = false;
            }

            UpdateClearCanvasSlideState();
        }

        private void TryHideEraserFlyout()
        {
            FlyoutBase? flyout = FlyoutBase.GetAttachedFlyout(EraserButton);
            flyout?.Hide();
        }

        private void OnShapeButtonClicked(object sender, RoutedEventArgs e)
        {
            // 逻辑约定：首次点击进入最近使用的形状工具；已在形状模式下再次点击则弹出形状切换面板。
            bool alreadyShape = _selectedMode.IsShapeMode();
            ModeRequested?.Invoke(this, _lastShapeMode);

            if (!alreadyShape)
            {
                return;
            }

            if (_isShapeFlyoutOpen)
            {
                TryHideShapeFlyout();
                return;
            }

            SyncShapeFlyoutFromMode();
            FlyoutBase.ShowAttachedFlyout(ShapeButton);
        }

        private void OnShapeFlyoutOpened(object sender, object e)
        {
            _isShapeFlyoutOpen = true;
            SyncShapeFlyoutFromMode();
        }

        private void OnShapeFlyoutClosed(object sender, object e)
        {
            _isShapeFlyoutOpen = false;
        }

        private void OnShapeKindClicked(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton button || button.Tag is not string kind)
            {
                return;
            }

            // 切换具体形状模式（经 Flow 链路映射为同名 BoardTool；颜色/粗细沿用批注画笔参数）。
            ScreenAnnotationMode mode = kind switch
            {
                "Rectangle" => ScreenAnnotationMode.Rectangle,
                "Ellipse" => ScreenAnnotationMode.Ellipse,
                "Arrow" => ScreenAnnotationMode.Arrow,
                _ => ScreenAnnotationMode.Line,
            };

            ModeRequested?.Invoke(this, mode);
            SetExclusiveToggleChecked(ShapeKindPanel, button);

            // 选定形状后收起面板，让用户立刻开始批注。
            TryHideShapeFlyout();
        }

        private void SyncShapeFlyoutFromMode()
        {
            // 形状 Flyout 可能在模式切换等场景下被动打开，这里统一以当前选中模式为准同步选中态。
            ScreenAnnotationMode current = _selectedMode;
            foreach (UIElement element in ShapeKindPanel.Children)
            {
                if (element is ToggleButton button && button.Tag is string kind)
                {
                    button.IsChecked = kind switch
                    {
                        "Rectangle" => current == ScreenAnnotationMode.Rectangle,
                        "Ellipse" => current == ScreenAnnotationMode.Ellipse,
                        "Arrow" => current == ScreenAnnotationMode.Arrow,
                        _ => current == ScreenAnnotationMode.Line,
                    };
                }
            }
        }

        private void TryHideShapeFlyout()
        {
            FlyoutBase? flyout = FlyoutBase.GetAttachedFlyout(ShapeButton);
            flyout?.Hide();
        }

        private void TryHidePenFlyout()
        {
            FlyoutBase? flyout = FlyoutBase.GetAttachedFlyout(PenButton);
            flyout?.Hide();
        }

        private void OnReturnToAppButtonClicked(object sender, RoutedEventArgs e)
        {
            ReturnToAppRequested?.Invoke(this, EventArgs.Empty);
        }

        private RectInt32 BuildInitialToolbarBounds(IntPtr hwnd)
        {
            // 工具栏 XAML 按有效像素设计，但顶层 AppWindow 需要物理像素矩形；
            // 这里统一按窗口当前 DPI 把设计尺寸转换后再交给原生窗口。
            uint dpi = ResolveWindowDpi(hwnd);
            int width = ScreenAnnotationWindowInterop.ConvertDipSizeToPixels(ExpandedToolbarWidthDip, dpi);
            int height = ScreenAnnotationWindowInterop.ConvertDipSizeToPixels(ToolbarHeightDip, dpi);
            int margin = ScreenAnnotationWindowInterop.ConvertDipCoordinateToPixels(ScreenAnnotationDisplayTarget.DefaultToolbarMargin, dpi);
            return _displayTarget.GetInitialToolbarBounds(width, height, margin);
        }

        private static uint ResolveWindowDpi(IntPtr hwnd)
        {
            if (ScreenAnnotationWindowInterop.TryGetWindowDpi(hwnd, out uint dpi, out string? error))
            {
                return dpi;
            }

            AppLog.Warn("ScreenAnnotation.Interop", $"读取工具栏窗口 DPI 失败，将回退到默认 DPI：error='{error}'");
            return DefaultWindowDpi;
        }

        private void OnClearCanvasThumbPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _clearCanvasSlideController.OnPointerPressed(e);
        }

        private void OnClearCanvasThumbPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            _clearCanvasSlideController.OnPointerMoved(e);
        }

        private void OnClearCanvasThumbPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _clearCanvasSlideController.OnPointerReleased(e);
        }

        private void OnClearCanvasThumbPointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            _clearCanvasSlideController.OnPointerCanceled(e);
        }

        private void OnClearCanvasThumbPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            _clearCanvasSlideController.OnPointerCaptureLost(e);
        }

        private void UpdateClearCanvasSlideState()
        {
            _clearCanvasSlideController.UpdateEnabledState(_drawingState.CanClear);
        }

        private void ResetClearCanvasSlide(bool animated)
        {
            _clearCanvasSlideController.Reset(animated);
        }

        private void OnDragHandlePointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not FrameworkElement element)
            {
                return;
            }

            if (!ScreenAnnotationWindowInterop.TryGetCursorScreenPosition(out PointInt32 cursorPosition))
            {
                return;
            }

            IntPtr hwnd = ScreenAnnotationWindowInterop.GetWindowHandle(this);
            if (!ScreenAnnotationWindowInterop.TryGetWindowRect(hwnd, out RectInt32 windowBounds))
            {
                return;
            }

            _dragPointerId = e.Pointer.PointerId;
            _dragStartCursor = cursorPosition;
            _dragStartWindowOrigin = new PointInt32(windowBounds.X, windowBounds.Y);
            _dragMoved = false;

            element.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void OnDragHandlePointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_dragPointerId != e.Pointer.PointerId)
            {
                return;
            }

            if (!ScreenAnnotationWindowInterop.TryGetCursorScreenPosition(out PointInt32 cursorPosition))
            {
                return;
            }

            int deltaX = cursorPosition.X - _dragStartCursor.X;
            int deltaY = cursorPosition.Y - _dragStartCursor.Y;
            if (!_dragMoved && (Math.Abs(deltaX) > 3 || Math.Abs(deltaY) > 3))
            {
                _dragMoved = true;
            }

            var appWindow = ScreenAnnotationWindowInterop.TryGetAppWindow(this);
            if (appWindow is null)
            {
                return;
            }

            IntPtr hwnd = ScreenAnnotationWindowInterop.GetWindowHandle(this);
            if (!ScreenAnnotationWindowInterop.TryGetWindowRect(hwnd, out RectInt32 currentWindowBounds))
            {
                return;
            }

            RectInt32 area = _displayTarget.WorkArea.Width > 0 && _displayTarget.WorkArea.Height > 0
                ? _displayTarget.WorkArea
                : _displayTarget.Bounds;

            int maxX = area.X + Math.Max(0, area.Width - currentWindowBounds.Width);
            int maxY = area.Y + Math.Max(0, area.Height - currentWindowBounds.Height);

            int newX = Math.Clamp(_dragStartWindowOrigin.X + deltaX, area.X, maxX);
            int newY = Math.Clamp(_dragStartWindowOrigin.Y + deltaY, area.Y, maxY);

            appWindow.Move(new PointInt32(newX, newY));
            e.Handled = true;
        }

        private void OnDragHandlePointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_dragPointerId != e.Pointer.PointerId)
            {
                return;
            }

            if (sender is FrameworkElement element)
            {
                element.ReleasePointerCapture(e.Pointer);
            }

            bool shouldToggle = !_dragMoved;
            ResetDragState();

            if (shouldToggle)
            {
                ToggleCollapsed();
            }

            e.Handled = true;
        }

        private void OnDragHandlePointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            ResetDragState();
        }

        private void OnDragHandlePointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            ResetDragState();
        }

        private void ResetDragState()
        {
            _dragPointerId = null;
            _dragMoved = false;
        }

        private void ToggleCollapsed()
        {
            _isCollapsed = !_isCollapsed;
            Visibility toolAreaVisibility = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
            ToolGroupDivider.Visibility = toolAreaVisibility;
            ToolButtonsPanel.Visibility = toolAreaVisibility;
        }

        private void OnBackdropWindowMessageObserved(object? sender, ScreenAnnotationWindowMessageEventArgs e)
        {
            if (!_isWindowInitialized || e.MessageId != ScreenAnnotationWindowInterop.WmDpiChanged)
            {
                return;
            }

            if (!ScreenAnnotationWindowInterop.TryReadDpiChangedSuggestedRect(e.LParam, out RectInt32 suggestedBounds))
            {
                AppLog.Warn("ScreenAnnotation.Interop", "读取工具栏 DPI 变化建议矩形失败。");
                return;
            }

            RectInt32 clampedBounds = _displayTarget.ClampBoundsToVisibleArea(suggestedBounds);
            if (!ScreenAnnotationWindowInterop.TryMoveAndResizeWindow(e.Hwnd, clampedBounds, out string? error))
            {
                AppLog.Warn("ScreenAnnotation.Interop", $"处理工具栏 DPI 变化失败：error='{error}'");
                return;
            }

            e.Result = IntPtr.Zero;
            e.Handled = true;
        }

        private bool TryAttachTransparentBackdrop(IntPtr hwnd, out string? error)
        {
            if (_transparentBackdrop is not null)
            {
                error = null;
                return true;
            }

            try
            {
                // 工具栏也需要挂透明 backdrop，否则即便 XAML 根节点透明，WinUI 顶层窗口仍可能退化成黑底。
                var backdrop = new ScreenAnnotationTransparentBackdrop(hwnd);
                backdrop.WindowMessageObserved += OnBackdropWindowMessageObserved;
                SystemBackdrop = backdrop;
                _transparentBackdrop = backdrop;
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private void OnWindowClosed(object sender, WindowEventArgs args)
        {
            if (_transparentBackdrop is not null)
            {
                _transparentBackdrop.WindowMessageObserved -= OnBackdropWindowMessageObserved;
            }

            SystemBackdrop = null;
            _transparentBackdrop = null;
        }

        private void CloseWindowAfterInitializationFailure()
        {
            try
            {
                Close();
            }
            catch (Exception ex)
            {
                AppLog.Warn("ScreenAnnotation.Interop", "关闭初始化失败的工具栏窗口时发生异常。", ex);
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

        void IScreenAnnotationModeToolbar.SetSelectedMode(ScreenAnnotationMode mode)
        {
            SetSelectedMode(mode);
        }

        void IScreenAnnotationModeToolbar.EnsureInteractiveTopMost(IScreenAnnotationModeOverlay? overlay)
        {
            EnsureInteractiveTopMost(overlay);
        }

        bool IScreenAnnotationModeToolbar.TryGetWindowHandle(out IntPtr hwnd)
        {
            return TryGetWindowHandle(out hwnd);
        }
    }
}

