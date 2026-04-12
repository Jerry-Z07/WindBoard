using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WindBoard.Board;
using WindBoard.Board.Commands;
using WindBoard.Board.Editing;
using WindBoard.Board.Elements;
using WindBoard.Board.Viewport;
using Vortice.Mathematics;

namespace WindBoard.Interaction
{
    internal sealed partial class BoardInputController
    {
        private const int WheelZoomIdleTimeoutMs = 150;
        private const int WheelZoomTimerIntervalMs = 50;
        private const float DirtyRectExtraDip = 2.0f;
        private const float SelectHitToleranceDip = 8.0f;
        private const float MarqueeClickThresholdDip = 6.0f;

        /// <summary>
        /// 橡皮擦半径（DIP）：X/Y 分量分别表示水平/垂直半径。
        /// 
        /// 说明：
        /// - 该值需要与擦除光标的视觉尺寸保持一致，避免出现“擦除范围与光标不一致”。
        /// - 默认值与当前 SVG 光标（48×72 DIP）对齐：半径为 (24, 36)。
        /// </summary>
        public Vector2 EraserRadiusDip { get; set; } = new(24.0f, 36.0f);

        private readonly SwapChainPanel _panel;
        private readonly BoardSession _session;
        private readonly BoardViewport _viewport;
        private IBoardEraser _eraser;

        private uint? _activePointerId;
        private uint? _panPointerId;
        private uint? _selectionPointerId;
        private uint? _marqueePointerId;
        private Vector2 _lastPanScreen = Vector2.Zero;
        private Vector2 _lastSelectionScreen = Vector2.Zero;
        private Vector2 _marqueeStartScreen = Vector2.Zero;
        private Vector2 _marqueeCurrentScreen = Vector2.Zero;
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
        private Rect? _pendingStrokeDirtyRect;
        private bool _isErasing;
        private Vector2? _lastEraserWorld;
        private List<Stroke>? _eraseBeforeSnapshot;

        // 选择工具：支持“单笔迹”与“多笔迹框选”两种形态。
        // 约定：框选命中多个笔迹时，把它们视为一个整体进行移动/缩放/旋转等操作。
        private readonly List<Stroke> _selectedStrokes = new();
        private BoardElement? _selectedElement;

        // 选择变换：对“选中的笔迹集合”做快照，提交时写入撤销记录。
        private List<StrokeTransformSnapshot>? _selectionStrokeBeforeSnapshots;
        private BoardElement? _selectionTransformElement;
        private Vector2? _selectionElementBeforePositionWorld;
        private Vector2? _selectionElementBeforeSizeWorld;
        private bool _selectionModified;

        private sealed class StrokeTransformSnapshot
        {
            public StrokeTransformSnapshot(Stroke stroke)
            {
                Stroke = stroke ?? throw new ArgumentNullException(nameof(stroke));
                BeforePoints = new List<StrokePoint>(stroke.Points);
            }

            public Stroke Stroke { get; }

            public List<StrokePoint> BeforePoints { get; }
        }

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
            // 默认使用“像素级擦除”（局部擦除），更符合常见橡皮擦体验。
            _eraser = eraser ?? new PixelStrokeEraser();
        }

        public BoardTool Tool { get; set; } = BoardTool.Pen;

        /// <summary>
        /// 画笔颜色（仅影响后续新建笔迹）。
        /// </summary>
        public Color4 PenColor { get; set; } = new(0, 0, 0, 1);

        /// <summary>
        /// 画笔粗细（世界坐标下的“笔迹直径”，仅影响后续新建笔迹）。
        /// </summary>
        public float PenBaseSize { get; set; } = 3.0f;

        /// <summary>
        /// 是否启用压感（会影响笔迹宽度随压力变化），仅影响后续新建笔迹。
        /// </summary>
        public bool PenEnablePressure { get; set; } = true;

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

        public IBoardEraser Eraser
        {
            get => _eraser;
            set => _eraser = value ?? throw new ArgumentNullException(nameof(value));
        }

        public Stroke? ActiveStroke { get; private set; }

        /// <summary>
        /// 当前选中的笔迹（选择工具）。
        /// </summary>
        /// <remarks>
        /// 兼容单选场景：当且仅当选中一条笔迹时返回该笔迹；多选时返回 null。
        /// 多选请使用 <see cref="SelectedStrokes"/>。
        /// </remarks>
        public Stroke? SelectedStroke => _selectedStrokes.Count == 1 ? _selectedStrokes[0] : null;

        /// <summary>
        /// 当前选中的笔迹集合（选择工具）。
        /// </summary>
        public IReadOnlyList<Stroke> SelectedStrokes => _selectedStrokes;

        /// <summary>
        /// 当前选中的元素（选择工具）。
        /// </summary>
        public BoardElement? SelectedElement => _selectedElement;

        public bool IsErasing => _isErasing;

        public bool IsWheelZooming => _isWheelZooming;

        private bool HasActiveToolInteraction => ActiveStroke is not null || _isErasing;

        private bool HasPointerGesture => _panPointerId is not null || _selectionPointerId is not null || _marqueePointerId is not null;

        private bool HasViewportGesture => _panPointerId is not null || _isManipulating;

        private bool HasSelectionGesture => _selectionPointerId is not null || _isManipulatingSelection || _marqueePointerId is not null;

        public bool IsContinuousViewportInteraction => HasViewportGesture;

        public bool IsContinuousSelectionInteraction => HasSelectionGesture;

        public bool TryGetSelectionMarqueeRectDip(out Rect marqueeRectDip)
        {
            if (_marqueePointerId is null)
            {
                marqueeRectDip = default;
                return false;
            }

            marqueeRectDip = CreateRectFromTwoPoints(_marqueeStartScreen, _marqueeCurrentScreen);
            return true;
        }

        public Vector2 ConsumePanScreenDelta()
        {
            Vector2 delta = _pendingPanScreenDelta;
            _pendingPanScreenDelta = Vector2.Zero;
            return delta;
        }

        public bool TryConsumeStrokeDirtyRect(out Rect dirtyRectDip)
        {
            if (_pendingStrokeDirtyRect is Rect rect)
            {
                _pendingStrokeDirtyRect = null;
                dirtyRectDip = rect;
                return true;
            }

            dirtyRectDip = default;
            return false;
        }

        /// <summary>
        /// 校验当前选择是否仍存在于文档中（例如撤销/重做导致笔迹移除时清理选择）。
        /// </summary>
        public void ValidateSelection()
        {
            if (_selectedStrokes.Count > 0)
            {
                // 选择笔迹集合：按文档当前顺序重新归一化，避免撤销/重做或重排后出现“顺序错乱/包含失效对象”。
                var set = new HashSet<Stroke>(_selectedStrokes);
                var normalized = new List<Stroke>(_selectedStrokes.Count);
                for (int i = 0; i < _session.Document.Strokes.Count; i++)
                {
                    Stroke s = _session.Document.Strokes[i];
                    if (set.Contains(s))
                    {
                        normalized.Add(s);
                    }
                }

                if (IsSameStrokeList(_selectedStrokes, normalized))
                {
                    return;
                }

                _selectedStrokes.Clear();
                _selectedStrokes.AddRange(normalized);
                FrameInvalidated?.Invoke();
                StateChanged?.Invoke();
                return;
            }

            if (_selectedElement is BoardElement element)
            {
                if (_session.Document.ElementsAboveInk.Contains(element) || _session.Document.ElementsBelowInk.Contains(element))
                {
                    return;
                }

                _selectedElement = null;
                FrameInvalidated?.Invoke();
                StateChanged?.Invoke();
            }
        }

        public void ClearSelection()
        {
            SetSelectedStroke(null);
            SetSelectedElement(null);
        }

        public void SetSelection(Stroke? stroke)
        {
            SetSelectedStroke(stroke);
        }

        public void SetSelectionStrokes(IReadOnlyList<Stroke>? strokes)
        {
            SetSelectedStrokes(strokes);
        }

        public void SetSelection(BoardElement? element)
        {
            SetSelectedElement(element);
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
