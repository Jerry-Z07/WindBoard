using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WindBoard.Board;
using WindBoard.Board.Editing;
using WindBoard.Board.Elements;
using WindBoard.Board.Items;
using WindBoard.Board.Viewport;
using WindBoard.Interaction.Tools;
using Vortice.Mathematics;

namespace WindBoard.Interaction
{
    internal sealed partial class BoardInputController
    {
        private const int WheelZoomIdleTimeoutMs = 150;
        private const int WheelZoomTimerIntervalMs = 50;

        /// <summary>
        /// 橡皮擦半径（DIP）：X/Y 分量分别表示水平/垂直半径。
        /// </summary>
        /// <remarks>
        /// 该值需要与擦除光标的视觉尺寸保持一致（由控件从光标尺寸同步），经 <see cref="EraserTool"/> 消费。
        /// </remarks>
        public Vector2 EraserRadiusDip
        {
            get => _eraserTool.RadiusDip;
            set => _eraserTool.RadiusDip = value;
        }

        private readonly SwapChainPanel _panel;
        private readonly BoardSession _session;
        private readonly BoardViewport _viewport;

        // 工具策略化（design B）：工具经注册表解析，运行态由各工具对象内聚；
        // 控制器只负责指针事件路由、活动 pointerId 跟踪与工具调度。
        private readonly BoardInputContext _context;
        private readonly BoardToolRegistry _toolRegistry = new();
        private readonly EraserTool _eraserTool;
        private readonly SelectTool _selectTool;

        private uint? _activePointerId;
        private uint? _panPointerId;
        private uint? _selectionPointerId;
        private uint? _marqueePointerId;
        private Vector2 _lastPanScreen = Vector2.Zero;
        private Vector2 _lastSelectionScreen = Vector2.Zero;
        private PointerDeviceType? _activeStrokeDeviceType;
        private readonly HashSet<uint> _activeTouchPointers = new();
        private bool _isManipulating;
        private bool _isManipulatingSelection;
        private bool _isInteracting;
        private bool _isWheelZooming;
        private bool _allowViewportManipulation = true;
        private bool _allowSelectionInteractions = true;
        private DateTimeOffset _lastWheelZoomAt;
        private DispatcherQueueTimer? _wheelZoomTimer;
        private Vector2 _pendingPanScreenDelta = Vector2.Zero;

        private enum TouchManipulationTarget
        {
            Viewport,
            Selection,
        }

        private TouchManipulationTarget _touchManipulationTarget = TouchManipulationTarget.Viewport;

        public BoardInputController(SwapChainPanel panel, BoardSession session, BoardViewport viewport, IBoardEraser? eraser = null)
        {
            _panel = panel;
            _session = session;
            _viewport = viewport;

            // 工具上下文与注册表：随控制器生命周期创建。
            // 注册动作发生在画布初始化时序内（控制器由 BoardCanvasControl 创建），
            // 集中在构造函数注册内置工具可避免多处创建点遗漏注册导致工具静默失效。
            _context = new BoardInputContext(viewport, session);
            _context.FrameInvalidated += () => FrameInvalidated?.Invoke();
            _toolRegistry.Register(new PenTool());
            _eraserTool = new EraserTool(eraser);
            _toolRegistry.Register(_eraserTool);
            _selectTool = new SelectTool(_context);
            _toolRegistry.Register(_selectTool);
            // 形状工具（design B）：同一个 ShapeTool 类按形状种类注册 4 个实例，
            // 控制器调度结构零改动（按下/移动/提交/取消统一经注册表解析）。
            _toolRegistry.Register(new ShapeTool(BoardShapeKind.Line));
            _toolRegistry.Register(new ShapeTool(BoardShapeKind.Rectangle));
            _toolRegistry.Register(new ShapeTool(BoardShapeKind.Ellipse));
            _toolRegistry.Register(new ShapeTool(BoardShapeKind.Arrow));
            _selectTool.SelectionChanged += () =>
            {
                FrameInvalidated?.Invoke();
                StateChanged?.Invoke();
            };
        }

        /// <summary>
        /// 绘制参数（工具/颜色/粗细/压感），转发到 <see cref="BoardInputContext"/>。
        /// </summary>
        /// <remarks>
        /// 参数仅影响后续新建笔迹：工具在 Begin 时读取一次值拷贝快照，Move 不重读。
        /// </remarks>
        public ToolOptions ToolOptions
        {
            get => _context.ToolOptions;
            set => _context.ToolOptions = value;
        }

        /// <summary>当前工具（<see cref="ToolOptions"/> 的便捷读写口，语义不变）。</summary>
        public BoardTool Tool
        {
            get => _context.ToolOptions.Tool;
            set => _context.ToolOptions = _context.ToolOptions with { Tool = value };
        }

        /// <summary>
        /// 解析当前活动会话应使用的工具 id（注册表查询前的统一入口）。
        /// </summary>
        /// <remarks>
        /// 既有回退行为：Select 工具在"禁用选择"场景按画笔处理。按下/移动/提交/取消
        /// 四条调度路径统一经此解析，保证"Select 回退画笔"在会话全程一致（否则
        /// Begin 以画笔建立、Commit 却解析到 SelectTool，笔迹会被静默丢弃）。
        /// </remarks>
        private BoardTool ResolveActiveToolId()
        {
            return Tool == BoardTool.Select ? BoardTool.Pen : Tool;
        }

        /// <summary>
        /// 是否允许视口类交互（右键平移、滚轮缩放、双指拖动/捏合）。
        /// </summary>
        public bool AllowViewportManipulation
        {
            get => _allowViewportManipulation;
            set
            {
                if (_allowViewportManipulation == value)
                {
                    return;
                }

                _allowViewportManipulation = value;
                UpdateManipulationMode();
            }
        }

        /// <summary>
        /// 是否允许选择相关交互（框选、拖动选中对象等）。
        /// </summary>
        public bool AllowSelectionInteraction
        {
            get => _allowSelectionInteractions;
            set => _allowSelectionInteractions = value;
        }

        /// <summary>
        /// 擦除策略（整笔/像素擦除），转发到 <see cref="EraserTool"/>。
        /// </summary>
        public IBoardEraser Eraser
        {
            get => _eraserTool.Eraser;
            set => _eraserTool.Eraser = value;
        }

        /// <summary>
        /// 当前活动笔迹（画笔工具的预览条目）。
        /// </summary>
        /// <remarks>
        /// 预览条目由 <see cref="PenTool"/> 内聚维护，经 <see cref="BoardInputContext.PreviewItem"/>
        /// 挂载点暴露；此属性保持原公开语义（渲染层/控件读取），避免调用点改动。
        /// </remarks>
        public Stroke? ActiveStroke => _context.PreviewItem as Stroke;

        /// <summary>
        /// 当前选中的笔迹（选择工具）。
        /// </summary>
        /// <remarks>
        /// 兼容单选场景：当且仅当选中一条笔迹时返回该笔迹；多选时返回 null。
        /// 多选请使用 <see cref="SelectedStrokes"/>。选中集由 <see cref="SelectTool"/> 内聚维护。
        /// </remarks>
        public Stroke? SelectedStroke => _selectTool.SelectedStroke;

        /// <summary>
        /// 当前选中的笔迹集合（选择工具）。
        /// </summary>
        public IReadOnlyList<Stroke> SelectedStrokes => _selectTool.SelectedStrokes;

        /// <summary>
        /// 当前选中的元素（选择工具）。
        /// </summary>
        public BoardElement? SelectedElement => _selectTool.SelectedElement;

        /// <summary>是否正在进行擦除（转发到 <see cref="EraserTool"/> 运行态）。</summary>
        public bool IsErasing => _eraserTool.IsErasing;

        public bool IsWheelZooming => _isWheelZooming;

        private bool HasActiveToolInteraction => _context.PreviewItem is not null || _eraserTool.IsErasing;

        private bool HasPointerGesture => _panPointerId is not null || _selectionPointerId is not null || _marqueePointerId is not null;

        private bool HasViewportGesture => _panPointerId is not null || _isManipulating;

        private bool HasSelectionGesture => _selectionPointerId is not null || _isManipulatingSelection || _marqueePointerId is not null;

        public bool IsContinuousViewportInteraction => HasViewportGesture;

        public bool IsContinuousSelectionInteraction => HasSelectionGesture;

        /// <summary>渲染层读取框选矩形（转发到 <see cref="SelectTool"/>）。</summary>
        public bool TryGetSelectionMarqueeRectDip(out Rect marqueeRectDip)
        {
            return _selectTool.TryGetMarqueeRectDip(out marqueeRectDip);
        }

        public Vector2 ConsumePanScreenDelta()
        {
            Vector2 delta = _pendingPanScreenDelta;
            _pendingPanScreenDelta = Vector2.Zero;
            return delta;
        }

        public bool TryConsumeStrokeDirtyRect(out Rect dirtyRectDip)
        {
            return _context.TryConsumeStrokeDirtyRect(out dirtyRectDip);
        }

        /// <summary>
        /// 校验当前选择是否仍存在于文档中（例如撤销/重做导致笔迹移除时清理选择）。
        /// </summary>
        public void ValidateSelection()
        {
            _selectTool.ValidateSelection();
        }

        public void ClearSelection()
        {
            _selectTool.ClearSelection();
        }

        public void SetSelection(Stroke? stroke)
        {
            _selectTool.SetSelection(stroke);
        }

        public void SetSelectionStrokes(IReadOnlyList<Stroke>? strokes)
        {
            _selectTool.SetSelectionStrokes(strokes);
        }

        public void SetSelection(BoardElement? element)
        {
            _selectTool.SetSelection(element);
        }

        public event Action? StateChanged;

        public event Action? FrameInvalidated;

        public event Action<bool>? InteractionStateChanged;

        public void Attach()
        {
            _panel.PointerPressed += OnCanvasPointerPressed;
            _panel.PointerMoved += OnCanvasPointerMoved;
            _panel.PointerReleased += OnCanvasPointerReleased;
            _panel.PointerCanceled += OnCanvasPointerCanceled;
            _panel.PointerCaptureLost += OnCanvasPointerCaptureLost;
            _panel.PointerWheelChanged += OnCanvasPointerWheelChanged;

            // 触摸：单指画线；双指/多指拖动+捏合缩放（Pinch Zoom）
            UpdateManipulationMode();
            _panel.ManipulationStarting += OnCanvasManipulationStarting;
            _panel.ManipulationDelta += OnCanvasManipulationDelta;
            _panel.ManipulationCompleted += OnCanvasManipulationCompleted;
        }

        public void Detach()
        {
            _panel.PointerPressed -= OnCanvasPointerPressed;
            _panel.PointerMoved -= OnCanvasPointerMoved;
            _panel.PointerReleased -= OnCanvasPointerReleased;
            _panel.PointerCanceled -= OnCanvasPointerCanceled;
            _panel.PointerCaptureLost -= OnCanvasPointerCaptureLost;
            _panel.PointerWheelChanged -= OnCanvasPointerWheelChanged;

            _panel.ManipulationStarting -= OnCanvasManipulationStarting;
            _panel.ManipulationDelta -= OnCanvasManipulationDelta;
            _panel.ManipulationCompleted -= OnCanvasManipulationCompleted;

            if (_wheelZoomTimer is not null)
            {
                _wheelZoomTimer.Stop();
                _wheelZoomTimer.Tick -= OnWheelZoomTimerTick;
                _wheelZoomTimer = null;
            }

            _isWheelZooming = false;
            _lastWheelZoomAt = default;
        }

        private void UpdateManipulationMode()
        {
            _panel.ManipulationMode = _allowViewportManipulation
                ? ManipulationModes.TranslateX | ManipulationModes.TranslateY | ManipulationModes.Scale | ManipulationModes.Rotate
                : ManipulationModes.None;
        }


    }
}
