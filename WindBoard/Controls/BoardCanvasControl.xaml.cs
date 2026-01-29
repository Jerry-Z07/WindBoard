using System;
using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Vortice.Mathematics;
using UiColor = Windows.UI.Color;
using WindBoard.Board;
using WindBoard.Board.Editing;
using WindBoard.Board.Viewport;
using WindBoard.Interaction;
using WindBoard.Rendering;
using WindBoard.Rendering.Board;

namespace WindBoard.Controls
{
    public sealed partial class BoardCanvasControl : UserControl, IDisposable
    {
        private const int MaxInteractiveFps = 60;

        private DxSwapChainPanelRenderer? _renderer;
        private BoardSession _session = new();
        private readonly BoardViewport _viewport = new();
        private readonly BoardSceneRenderer _sceneRenderer = new();
        private BoardInputController? _input;
        private BoardTool _tool = BoardTool.Pen;
        // 默认使用“像素级擦除”（局部擦除），用户可在 UI 中切换为整笔擦除。
        private IBoardEraser _eraser = new PixelStrokeEraser();
        private UiColor _canvasBackgroundColor = UiColor.FromArgb(0xFF, 0x2E, 0x2F, 0x33);
        private bool _isInitialized;
        private bool _isRenderingLoopActive;
        private bool _isRenderQueued;
        private long _lastRenderingLoopTick;
        private bool _wasWriting;
        private float _lastRenderedZoom = float.NaN;

        // 擦除光标（SVG）显示状态：擦除工具下显示；鼠标/触控笔悬停显示，触摸按下/拖动（接触）时显示。
        private bool _isPointerOverCanvas;
        private bool _isPointerInContact;
        private PointerDeviceType? _lastPointerDeviceType;
        private bool _isEraserCursorHandlersAttached;
        private PointerEventHandler? _cursorPointerEnteredHandler;
        private PointerEventHandler? _cursorPointerExitedHandler;
        private PointerEventHandler? _cursorPointerMovedHandler;
        private PointerEventHandler? _cursorPointerPressedHandler;
        private PointerEventHandler? _cursorPointerReleasedHandler;
        private PointerEventHandler? _cursorPointerCanceledHandler;
        private PointerEventHandler? _cursorPointerCaptureLostHandler;

        public BoardCanvasControl()
        {
            InitializeComponent();

            Loaded += (_, _) => EnsureInitialized();
            Unloaded += (_, _) => Dispose();
        }

        public event EventHandler? CommandStateChanged;

        /// <summary>
        /// 画布背景色（用于渲染层清屏）。
        /// </summary>
        internal UiColor CanvasBackgroundColor
        {
            get => _canvasBackgroundColor;
            set
            {
                if (_canvasBackgroundColor == value)
                {
                    return;
                }

                _canvasBackgroundColor = value;
                ApplyCanvasBackgroundToRenderer();
                RequestRender();
            }
        }

        internal BoardTool Tool
        {
            get => _tool;
            set
            {
                if (_tool == value)
                {
                    return;
                }

                _tool = value;

                // 切换工具前结束当前动作，避免遗留捕获/状态。
                _input?.CancelActiveToolOperation();

                if (_input is not null)
                {
                    _input.Tool = _tool;
                }

                RaiseCommandStateChanged();
                RequestRender();
                UpdateEraserCursorVisibility();
            }
        }

        internal IBoardEraser Eraser
        {
            get => _eraser;
            set
            {
                _eraser = value ?? throw new ArgumentNullException(nameof(value));

                if (_input is not null)
                {
                    _input.Eraser = _eraser;
                }
            }
        }

        internal bool CanUndo => _session.CanUndo;

        internal bool CanRedo => _session.CanRedo;

        internal bool CanClear => _session.HasStrokes || _input?.ActiveStroke is not null;

        internal void Undo()
        {
            _input?.CancelActiveToolOperation();
            _session.Undo();
        }

        internal void Redo()
        {
            _input?.CancelActiveToolOperation();
            _session.Redo();
        }

        internal void ClearAll()
        {
            _input?.CancelActiveToolOperation();
            _session.ClearAll();
        }

        /// <summary>
        /// 绑定编辑会话（用于多页面切换）。
        /// 
        /// 注意：
        /// - 需要重新创建 <see cref="BoardInputController"/>，因为它在构造时持有会话引用。
        /// - 切换时会终止当前交互，避免指针捕获/临时状态残留。
        /// </summary>
        internal void BindSession(BoardSession session)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            if (ReferenceEquals(_session, session))
            {
                return;
            }

            // 未初始化时仅替换引用，让 EnsureInitialized 使用新的会话创建输入控制器。
            if (!_isInitialized)
            {
                _session = session;
                return;
            }

            // 切页时强制结束当前动作，避免跨页面遗留捕获/状态。
            _input?.CancelActiveToolOperation();
            SetRenderingLoopActive(false);

            _session.StateChanged -= OnSessionStateChanged;
            _session = session;
            _session.StateChanged += OnSessionStateChanged;

            // 会话切换后需要重建输入控制器（内部引用不可变）。
            if (_input is not null)
            {
                _input.StateChanged -= OnInputStateChanged;
                _input.FrameInvalidated -= OnFrameInvalidated;
                _input.InteractionStateChanged -= OnInteractionStateChanged;
                _input.Detach();
            }

            _input = new BoardInputController(CanvasPanel, _session, _viewport, _eraser)
            {
                Tool = _tool,
                Eraser = _eraser,
                EraserRadiusDip = GetEraserRadiusDipFromCursor(),
            };
            _input.Attach();
            _input.StateChanged += OnInputStateChanged;
            _input.FrameInvalidated += OnFrameInvalidated;
            _input.InteractionStateChanged += OnInteractionStateChanged;

            // 避免把旧页面缓存背景“带到”新页面。
            _renderer?.InvalidateCachedBackground();
            _wasWriting = false;

            RaiseCommandStateChanged();
            RequestRender();
            UpdateEraserCursorVisibility();
        }

        private void EnsureInitialized()
        {
            if (_isInitialized)
            {
                return;
            }

            _renderer = new DxSwapChainPanelRenderer(CanvasPanel);
            _renderer.Initialize();
            ApplyCanvasBackgroundToRenderer();

            _input = new BoardInputController(CanvasPanel, _session, _viewport, _eraser);
            _input.Tool = _tool;
            _input.Eraser = _eraser;
            _input.EraserRadiusDip = GetEraserRadiusDipFromCursor();
            _input.Attach();

            CanvasPanel.SizeChanged += OnCanvasSizeChanged;
            CanvasPanel.CompositionScaleChanged += OnCanvasCompositionScaleChanged;

            if (CanvasPanel.XamlRoot is not null)
            {
                CanvasPanel.XamlRoot.Changed += OnXamlRootChanged;
            }

            _session.StateChanged += OnSessionStateChanged;
            _input.StateChanged += OnInputStateChanged;
            _input.FrameInvalidated += OnFrameInvalidated;
            _input.InteractionStateChanged += OnInteractionStateChanged;

            AttachEraserCursorHandlers();
            UpdateEraserCursorVisibility();

            UpdateViewportSize();

            _isInitialized = true;

            RaiseCommandStateChanged();
            RequestRender();
        }

        private void ApplyCanvasBackgroundToRenderer()
        {
            if (_renderer is null)
            {
                return;
            }

            // 背景色变更需要同时影响：
            // - 正常 Render 的清屏色
            // - 背景缓存（cached background）的清屏色
            // - 平移滚动优化时的脏区填充色
            _renderer.ClearColor = ToColor4(_canvasBackgroundColor);
            _renderer.InvalidateCachedBackground();
        }

        private static Color4 ToColor4(UiColor color)
        {
            return new Color4(
                color.R / 255.0f,
                color.G / 255.0f,
                color.B / 255.0f,
                color.A / 255.0f);
        }

        private void OnXamlRootChanged(Microsoft.UI.Xaml.XamlRoot sender, XamlRootChangedEventArgs args)
        {
            _renderer?.Resize();
            RequestRender();
        }

        private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateViewportSize();
            _renderer?.Resize();
            RequestRender();
        }

        private void OnCanvasCompositionScaleChanged(SwapChainPanel sender, object args)
        {
            _renderer?.Resize();
            RequestRender();
        }

        private void UpdateViewportSize()
        {
            float w = (float)Math.Max(1.0, CanvasPanel.ActualWidth);
            float h = (float)Math.Max(1.0, CanvasPanel.ActualHeight);
            _viewport.UpdateViewportSize(new Vector2(w, h));
        }

        private void OnRendering(object? sender, object e)
        {
            if (!ShouldRenderInLoop())
            {
                return;
            }

            RenderFrame();
        }

        private bool ShouldRenderInLoop()
        {
            if (!_isRenderingLoopActive)
            {
                return true;
            }

            long minDelta = Stopwatch.Frequency / MaxInteractiveFps;
            if (minDelta <= 0)
            {
                return true;
            }

            long now = Stopwatch.GetTimestamp();
            if (_lastRenderingLoopTick != 0 && now - _lastRenderingLoopTick < minDelta)
            {
                return false;
            }

            _lastRenderingLoopTick = now;
            return true;
        }

        private void RaiseCommandStateChanged()
        {
            CommandStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnSessionStateChanged()
        {
            _renderer?.InvalidateCachedBackground();
            RaiseCommandStateChanged();
            RequestRender();
        }

        private void OnInputStateChanged()
        {
            UpdateWritingCacheState();
            RaiseCommandStateChanged();
            RequestRender();
        }

        private void OnFrameInvalidated()
        {
            RequestRender();
        }

        private void OnInteractionStateChanged(bool isInteracting)
        {
            if (!_isInitialized || _renderer is null)
            {
                return;
            }

            if (isInteracting)
            {
                // 书写时保持全分辨率，避免笔迹模糊；仅在平移/捏合缩放等视口操作时降低分辨率以减轻 GPU 压力。
                if (_input?.ActiveStroke is not null || _input?.IsErasing == true)
                {
                    SetRenderingLoopActive(false);
                    _renderer.SetInteractiveMode(false);
                    RequestRender();
                    return;
                }

                _renderer.SetInteractiveMode(true);

                if (_input?.IsContinuousViewportInteraction == true)
                {
                    SetRenderingLoopActive(true);
                }
                else
                {
                    SetRenderingLoopActive(false);
                    RequestRender();
                }
                return;
            }

            SetRenderingLoopActive(false);
            _renderer.SetInteractiveMode(false);
            RequestRender();
        }

        private void SetRenderingLoopActive(bool active)
        {
            if (!_isInitialized || _isRenderingLoopActive == active)
            {
                return;
            }

            if (active)
            {
                _lastRenderingLoopTick = 0;
                CompositionTarget.Rendering += OnRendering;
            }
            else
            {
                CompositionTarget.Rendering -= OnRendering;
            }

            _isRenderingLoopActive = active;
        }

        private void RequestRender()
        {
            if (!_isInitialized || _renderer is null)
            {
                return;
            }

            if (_isRenderingLoopActive)
            {
                return;
            }

            if (_isRenderQueued)
            {
                return;
            }

            _isRenderQueued = true;
            bool enqueued = DispatcherQueue.TryEnqueue(() =>
            {
                _isRenderQueued = false;

                if (!_isInitialized || _renderer is null || _isRenderingLoopActive)
                {
                    return;
                }

                RenderFrame();
            });

            if (!enqueued)
            {
                _isRenderQueued = false;

                if (!_isInitialized || _renderer is null || _isRenderingLoopActive)
                {
                    return;
                }

                RenderFrame();
            }
        }

        private void RenderFrame()
        {
            if (!_isInitialized || _renderer is null)
            {
                return;
            }

            Stroke? activeStroke = _input?.ActiveStroke;
            if (activeStroke is not null)
            {
                if (_input?.TryConsumeStrokeDirtyRect(out Rect dirtyRectDip) == true)
                {
                    _renderer.RenderWithCachedBackgroundDirtyRect(
                        dirtyRectDip,
                        drawBackground: ctx => _sceneRenderer.DrawBackground(ctx, _session.Document, _viewport),
                        drawOverlay: ctx => _sceneRenderer.DrawActiveStroke(ctx, activeStroke, _viewport));
                }
                else
                {
                    _renderer.RenderWithCachedBackground(
                        drawBackground: ctx => _sceneRenderer.DrawBackground(ctx, _session.Document, _viewport),
                        drawOverlay: ctx => _sceneRenderer.DrawActiveStroke(ctx, activeStroke, _viewport));
                }

                _lastRenderedZoom = _viewport.Zoom;
                return;
            }

            Vector2 panDeltaDip = _input?.ConsumePanScreenDelta() ?? Vector2.Zero;
            if (panDeltaDip.LengthSquared() > 0.0001f
                && !float.IsNaN(_lastRenderedZoom)
                && Math.Abs(_viewport.Zoom - _lastRenderedZoom) < 0.000001f)
            {
                bool presented = _renderer.TryRenderWithScroll(
                    panDeltaDip,
                    (ctx, dirtyDip) => _sceneRenderer.DrawBackgroundInScreenRect(ctx, _session.Document, _viewport, dirtyDip));

                if (presented)
                {
                    _lastRenderedZoom = _viewport.Zoom;
                    return;
                }
            }

            if (_isRenderingLoopActive
                && panDeltaDip.LengthSquared() <= 0.0001f
                && !float.IsNaN(_lastRenderedZoom)
                && Math.Abs(_viewport.Zoom - _lastRenderedZoom) < 0.000001f)
            {
                return;
            }

            _renderer.Render(ctx => _sceneRenderer.Draw(ctx, _session.Document, null, _viewport));
            _lastRenderedZoom = _viewport.Zoom;
        }

        private void UpdateWritingCacheState()
        {
            bool isWriting = _input?.ActiveStroke is not null;
            if (_wasWriting == isWriting)
            {
                return;
            }

            _wasWriting = isWriting;

            if (_renderer is null)
            {
                return;
            }

            if (isWriting)
            {
                _renderer.InvalidateCachedBackground();
                return;
            }

            _renderer.ReleaseCachedBackground();
        }

        private void AttachEraserCursorHandlers()
        {
            if (_isEraserCursorHandlersAttached)
            {
                return;
            }

            _cursorPointerEnteredHandler = OnCanvasPointerEnteredForEraserCursor;
            _cursorPointerExitedHandler = OnCanvasPointerExitedForEraserCursor;
            _cursorPointerMovedHandler = OnCanvasPointerMovedForEraserCursor;
            _cursorPointerPressedHandler = OnCanvasPointerPressedForEraserCursor;
            _cursorPointerReleasedHandler = OnCanvasPointerReleasedForEraserCursor;
            _cursorPointerCanceledHandler = OnCanvasPointerCanceledForEraserCursor;
            _cursorPointerCaptureLostHandler = OnCanvasPointerCaptureLostForEraserCursor;

            // 使用 handledEventsToo=true，避免输入控制器先把事件标记为 Handled 导致光标无法更新。
            CanvasPanel.AddHandler(UIElement.PointerEnteredEvent, _cursorPointerEnteredHandler, true);
            CanvasPanel.AddHandler(UIElement.PointerExitedEvent, _cursorPointerExitedHandler, true);
            CanvasPanel.AddHandler(UIElement.PointerMovedEvent, _cursorPointerMovedHandler, true);
            CanvasPanel.AddHandler(UIElement.PointerPressedEvent, _cursorPointerPressedHandler, true);
            CanvasPanel.AddHandler(UIElement.PointerReleasedEvent, _cursorPointerReleasedHandler, true);
            CanvasPanel.AddHandler(UIElement.PointerCanceledEvent, _cursorPointerCanceledHandler, true);
            CanvasPanel.AddHandler(UIElement.PointerCaptureLostEvent, _cursorPointerCaptureLostHandler, true);

            _isEraserCursorHandlersAttached = true;
        }

        private void DetachEraserCursorHandlers()
        {
            if (!_isEraserCursorHandlersAttached)
            {
                return;
            }

            if (_cursorPointerEnteredHandler is not null)
            {
                CanvasPanel.RemoveHandler(UIElement.PointerEnteredEvent, _cursorPointerEnteredHandler);
            }

            if (_cursorPointerExitedHandler is not null)
            {
                CanvasPanel.RemoveHandler(UIElement.PointerExitedEvent, _cursorPointerExitedHandler);
            }

            if (_cursorPointerMovedHandler is not null)
            {
                CanvasPanel.RemoveHandler(UIElement.PointerMovedEvent, _cursorPointerMovedHandler);
            }

            if (_cursorPointerPressedHandler is not null)
            {
                CanvasPanel.RemoveHandler(UIElement.PointerPressedEvent, _cursorPointerPressedHandler);
            }

            if (_cursorPointerReleasedHandler is not null)
            {
                CanvasPanel.RemoveHandler(UIElement.PointerReleasedEvent, _cursorPointerReleasedHandler);
            }

            if (_cursorPointerCanceledHandler is not null)
            {
                CanvasPanel.RemoveHandler(UIElement.PointerCanceledEvent, _cursorPointerCanceledHandler);
            }

            if (_cursorPointerCaptureLostHandler is not null)
            {
                CanvasPanel.RemoveHandler(UIElement.PointerCaptureLostEvent, _cursorPointerCaptureLostHandler);
            }

            _cursorPointerEnteredHandler = null;
            _cursorPointerExitedHandler = null;
            _cursorPointerMovedHandler = null;
            _cursorPointerPressedHandler = null;
            _cursorPointerReleasedHandler = null;
            _cursorPointerCanceledHandler = null;
            _cursorPointerCaptureLostHandler = null;
            _isEraserCursorHandlersAttached = false;

            _isPointerOverCanvas = false;
            _isPointerInContact = false;
            _lastPointerDeviceType = null;
            UpdateEraserCursorVisibility();
        }

        private void OnCanvasPointerEnteredForEraserCursor(object sender, PointerRoutedEventArgs e)
        {
            _isPointerOverCanvas = true;
            _lastPointerDeviceType = e.Pointer.PointerDeviceType;
            _isPointerInContact = e.GetCurrentPoint(CanvasPanel).IsInContact;
            UpdateEraserCursorPosition(e);
            UpdateEraserCursorVisibility();
        }

        private void OnCanvasPointerExitedForEraserCursor(object sender, PointerRoutedEventArgs e)
        {
            _isPointerOverCanvas = false;
            UpdateEraserCursorVisibility();
        }

        private void OnCanvasPointerMovedForEraserCursor(object sender, PointerRoutedEventArgs e)
        {
            _lastPointerDeviceType = e.Pointer.PointerDeviceType;
            _isPointerInContact = e.GetCurrentPoint(CanvasPanel).IsInContact;
            UpdateEraserCursorPosition(e);
            UpdateEraserCursorVisibility();
        }

        private void OnCanvasPointerPressedForEraserCursor(object sender, PointerRoutedEventArgs e)
        {
            // 触摸没有“悬停”概念：通过按下事件建立光标显示状态。
            _isPointerOverCanvas = true;
            _lastPointerDeviceType = e.Pointer.PointerDeviceType;
            _isPointerInContact = e.GetCurrentPoint(CanvasPanel).IsInContact;
            UpdateEraserCursorPosition(e);
            UpdateEraserCursorVisibility();
        }

        private void OnCanvasPointerReleasedForEraserCursor(object sender, PointerRoutedEventArgs e)
        {
            _lastPointerDeviceType = e.Pointer.PointerDeviceType;
            _isPointerInContact = e.GetCurrentPoint(CanvasPanel).IsInContact;
            UpdateEraserCursorVisibility();
        }

        private void OnCanvasPointerCanceledForEraserCursor(object sender, PointerRoutedEventArgs e)
        {
            _isPointerInContact = false;
            UpdateEraserCursorVisibility();
        }

        private void OnCanvasPointerCaptureLostForEraserCursor(object sender, PointerRoutedEventArgs e)
        {
            _isPointerInContact = false;
            _isPointerOverCanvas = false;
            UpdateEraserCursorVisibility();
        }

        private void UpdateEraserCursorVisibility()
        {
            if (EraserCursorImage is null)
            {
                return;
            }

            bool shouldShow = _tool == BoardTool.Eraser && _lastPointerDeviceType is not null;

            // 触摸/鼠标：只有按下（接触）时才显示；触控笔：悬停时显示。
            if (_lastPointerDeviceType == PointerDeviceType.Touch || _lastPointerDeviceType == PointerDeviceType.Mouse)
            {
                shouldShow &= _isPointerInContact;
            }
            else
            {
                shouldShow &= _isPointerOverCanvas;
            }

            EraserCursorImage.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateEraserCursorPosition(PointerRoutedEventArgs e)
        {
            if (EraserCursorImage is null)
            {
                return;
            }

            // 以图片中心对齐指针位置；后续可根据擦除大小改为“底部/边缘对齐”等更符合手感的锚点。
            Windows.Foundation.Point pos = e.GetCurrentPoint(CanvasPanel).Position;
            double width = EraserCursorImage.ActualWidth > 0 ? EraserCursorImage.ActualWidth : EraserCursorImage.Width;
            double height = EraserCursorImage.ActualHeight > 0 ? EraserCursorImage.ActualHeight : EraserCursorImage.Height;

            // 光标锚点：中心跟随指针（包含触摸）。
            Canvas.SetLeft(EraserCursorImage, pos.X - width / 2.0);
            Canvas.SetTop(EraserCursorImage, pos.Y - height / 2.0);
        }

        private Vector2 GetEraserRadiusDipFromCursor()
        {
            // 默认以光标控件的宽高作为擦除范围的“直径”，取一半作为半径。
            // 这样可以保证用户看到的光标大小与实际擦除范围一致。
            if (EraserCursorImage is null)
            {
                return new Vector2(24.0f, 36.0f);
            }

            double width = !double.IsNaN(EraserCursorImage.Width) && EraserCursorImage.Width > 0
                ? EraserCursorImage.Width
                : EraserCursorImage.ActualWidth;

            double height = !double.IsNaN(EraserCursorImage.Height) && EraserCursorImage.Height > 0
                ? EraserCursorImage.Height
                : EraserCursorImage.ActualHeight;

            width = Math.Max(1.0, width);
            height = Math.Max(1.0, height);

            return new Vector2((float)(width / 2.0), (float)(height / 2.0));
        }

        public void Dispose()
        {
            if (!_isInitialized)
            {
                return;
            }

            SetRenderingLoopActive(false);

            DetachEraserCursorHandlers();

            CanvasPanel.SizeChanged -= OnCanvasSizeChanged;
            CanvasPanel.CompositionScaleChanged -= OnCanvasCompositionScaleChanged;

            if (CanvasPanel.XamlRoot is not null)
            {
                CanvasPanel.XamlRoot.Changed -= OnXamlRootChanged;
            }

            _session.StateChanged -= OnSessionStateChanged;

            if (_input is not null)
            {
                _input.StateChanged -= OnInputStateChanged;
                _input.FrameInvalidated -= OnFrameInvalidated;
                _input.InteractionStateChanged -= OnInteractionStateChanged;
                _input.Detach();
            }

            _input = null;

            _sceneRenderer.Dispose();

            _renderer?.Dispose();
            _renderer = null;

            _isInitialized = false;
        }
    }
}
