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
using WindBoard.Board.Commands;
using WindBoard.Board.Editing;
using WindBoard.Board.Elements;
using WindBoard.Board.Items;
using WindBoard.Board.Viewport;
using WindBoard.Interaction;
using WindBoard.Settings;
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
        // 绘制参数（design C 节）：工具/颜色/粗细/压感收敛为单一值对象，替代逐跳属性复制链。
        private ToolOptions _toolOptions = new(
            BoardTool.Pen, new Color4(0f, 0f, 0f, 1f), 3.0f, true);
        // 默认使用“像素级擦除”（局部擦除），用户可在 UI 中切换为整笔擦除。
        private IBoardEraser _eraser = new PixelStrokeEraser();
        private UiColor _canvasBackgroundColor = UiColor.FromArgb(0xFF, 0x2E, 0x2F, 0x33);
        private bool _allowViewportManipulation = true;
        private bool _allowSelectionInteraction = true;
        private ElementCardTheme _elementCardTheme = ElementCardTheme.Dark;
        private bool _isInitialized;
        private bool _isRenderingLoopActive;
        private bool _isRenderQueued;
        private long _lastRenderingLoopTick;
        private bool _wasWriting;
        private float _lastRenderedZoom = float.NaN;

        // “选中项 Dock - 置顶”支持再次点击撤销：
        // - 仅当“最近一次 Undo 栈栈顶命令”就是该置顶命令时才允许撤销，避免误撤销其它操作。
        // - 同时记录目标对象，避免切换选中后仍然显示“取消置顶”导致误操作。
        private IBoardCommand? _lastSelectionDockBringToFrontCommand;
        private IBoardInkItem[]? _lastSelectionDockBringToFrontItems;
        private BoardElement? _lastSelectionDockBringToFrontElement;

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

        /// <summary>
        /// 元素卡片主题（深/浅）：用于导入元素的卡片外观。
        /// </summary>
        internal ElementCardTheme ElementCardTheme
        {
            get => _elementCardTheme;
            set
            {
                if (_elementCardTheme == value)
                {
                    return;
                }

                _elementCardTheme = value;
                _sceneRenderer.ElementCardTheme = value;
                RequestRender();
            }
        }

        /// <summary>
        /// 当前工具（<see cref="ToolOptions"/> 的便捷读写口，沿用现有 UI 调用习惯）。
        /// </summary>
        internal BoardTool Tool
        {
            get => _toolOptions.Tool;
            set
            {
                if (_toolOptions.Tool == value)
                {
                    return;
                }

                BoardTool previousTool = _toolOptions.Tool;

                // 切换工具前结束当前动作，避免遗留捕获/状态。
                _input?.CancelActiveToolOperation();

                // 离开选择模式时清除已完成的选中状态，避免切回选择模式时残留旧选中。
                if (previousTool == BoardTool.Select && value != BoardTool.Select)
                {
                    _input?.ClearSelection();
                }

                _toolOptions = _toolOptions with { Tool = value };

                if (_input is not null)
                {
                    _input.ToolOptions = _toolOptions;
                }

                RaiseCommandStateChanged();
                RequestRender();
                UpdateEraserCursorVisibility();
            }
        }

        /// <summary>
        /// 绘制参数（工具/颜色/粗细/压感）。
        /// </summary>
        /// <remarks>
        /// - Tool 分量变化沿用原 Tool 切换语义（结束当前动作/清除选择/通知 UI）；
        ///   颜色/粗细/压感分量变化仅做值同步（仅影响后续新建笔迹），不触碰活动会话；
        /// - 不做整体相等短路：record struct 相等含 Color4 浮点位级比较，
        ///   不应作为副作用同步的依据（值同步本身无副作用，可安全重复执行）。
        /// </remarks>
        internal ToolOptions ToolOptions
        {
            get => _toolOptions;
            set
            {
                if (_toolOptions.Tool != value.Tool)
                {
                    Tool = value.Tool;
                }

                _toolOptions = value;

                if (_input is not null)
                {
                    _input.ToolOptions = _toolOptions;
                }
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
                ToolOptions = _toolOptions,
                Eraser = _eraser,
                EraserRadiusDip = GetEraserRadiusDipFromCursor(),
                AllowViewportManipulation = _allowViewportManipulation,
                AllowSelectionInteraction = _allowSelectionInteraction,
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

        /// <summary>
        /// 获取当前视口状态（用于导入元素的默认放置位置与尺寸计算）。
        /// </summary>
        internal void GetViewportState(out Vector2 cameraWorld, out float zoom)
        {
            cameraWorld = _viewport.CameraWorld;
            zoom = _viewport.Zoom;
        }

        /// <summary>
        /// 设置当前视口（用于屏幕批注等非交互初始化场景）。
        /// </summary>
        internal void SetView(Vector2 cameraWorld, float zoom)
        {
            _viewport.SetView(cameraWorld, zoom);
            RequestRender();
        }

        /// <summary>
        /// 设置交互开关（用于受限模式下关闭视口手势或选择能力）。
        /// </summary>
        internal void SetInteractionOptions(bool allowViewportManipulation, bool allowSelectionInteraction)
        {
            _allowViewportManipulation = allowViewportManipulation;
            _allowSelectionInteraction = allowSelectionInteraction;

            if (_input is null)
            {
                return;
            }

            _input.AllowViewportManipulation = _allowViewportManipulation;
            _input.AllowSelectionInteraction = _allowSelectionInteraction;
        }

        /// <summary>
        /// 选中指定元素（用于导入后自动进入选择并聚焦新对象）。
        /// </summary>
        internal void SetSelectedElement(BoardElement? element)
        {
            if (_input is null)
            {
                return;
            }

            // 外部强制选中前，先结束输入控制器可能存在的连续动作，避免残留捕获/状态。
            _input.CancelActiveToolOperation();

            _input.SetSelection(element);
            RequestRender();
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
            _sceneRenderer.ElementCardTheme = _elementCardTheme;

            _input = new BoardInputController(CanvasPanel, _session, _viewport, _eraser);
            _input.ToolOptions = _toolOptions;
            _input.Eraser = _eraser;
            _input.EraserRadiusDip = GetEraserRadiusDipFromCursor();
            _input.AllowViewportManipulation = _allowViewportManipulation;
            _input.AllowSelectionInteraction = _allowSelectionInteraction;
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
            AttachSelectionDockHandlers();
            UpdateEraserCursorVisibility();

            UpdateViewportSize();

            _isInitialized = true;

            RaiseCommandStateChanged();
            RequestRender();
        }

        private void AttachSelectionDockHandlers()
        {
            if (SelectionDockBorder is not null)
            {
                // Dock 尺寸变化（例如首次测量）时，需要重新定位到选择框下方。
                SelectionDockBorder.SizeChanged += (_, _) => UpdateSelectionOverlay();
            }

            if (SelectionBringToFrontButton is not null)
            {
                SelectionBringToFrontButton.Click += OnSelectionBringToFrontClicked;
            }

            if (SelectionDuplicateButton is not null)
            {
                SelectionDuplicateButton.Click += OnSelectionDuplicateClicked;
            }

            if (SelectionDeleteButton is not null)
            {
                SelectionDeleteButton.Click += OnSelectionDeleteClicked;
            }
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

        /// <summary>Windows.UI.Color → Color4（UI 侧组合 ToolOptions 时复用）。</summary>
        internal static Color4 ToColor4(UiColor color)
        {
            return new Color4(
                color.R / 255.0f,
                color.G / 255.0f,
                color.B / 255.0f,
                color.A / 255.0f);
        }

        /// <summary>Color4 → Windows.UI.Color（UI 侧读取 ToolOptions 做同步时复用；与 ToColor4 精确互逆）。</summary>
        internal static UiColor ToUiColor(Color4 color)
        {
            return UiColor.FromArgb(
                (byte)Math.Round(color.A * 255f),
                (byte)Math.Round(color.R * 255f),
                (byte)Math.Round(color.G * 255f),
                (byte)Math.Round(color.B * 255f));
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
            _input?.ValidateSelection();
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

                bool isViewportInteraction = _input?.IsContinuousViewportInteraction == true || _input?.IsWheelZooming == true;

                // 仅在“视口交互”（平移/捏合缩放/滚轮缩放）时降低分辨率；选择框选/移动笔迹等保持全分辨率，避免视觉错位。
                _renderer.SetInteractiveMode(isViewportInteraction);

                if (isViewportInteraction && _input?.IsContinuousViewportInteraction == true)
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
