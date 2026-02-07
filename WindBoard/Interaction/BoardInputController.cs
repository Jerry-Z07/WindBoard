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
        private DateTimeOffset _lastWheelZoomAt;
        private DispatcherQueueTimer? _wheelZoomTimer;
        private Vector2 _pendingPanScreenDelta = Vector2.Zero;
        private Rect? _pendingStrokeDirtyRect;
        private bool _isErasing;
        private Vector2? _lastEraserWorld;
        private List<Stroke>? _eraseBeforeSnapshot;

        private Stroke? _selectedStroke;
        private BoardElement? _selectedElement;
        private Stroke? _selectionTransformStroke;
        private List<StrokePoint>? _selectionBeforeSnapshot;
        private BoardElement? _selectionTransformElement;
        private Vector2? _selectionElementBeforePositionWorld;
        private Vector2? _selectionElementBeforeSizeWorld;
        private bool _selectionModified;

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

        public IBoardEraser Eraser
        {
            get => _eraser;
            set => _eraser = value ?? throw new ArgumentNullException(nameof(value));
        }

        public Stroke? ActiveStroke { get; private set; }

        /// <summary>
        /// 当前选中的笔迹（选择工具）。
        /// </summary>
        public Stroke? SelectedStroke => _selectedStroke;

        /// <summary>
        /// 当前选中的元素（选择工具）。
        /// </summary>
        public BoardElement? SelectedElement => _selectedElement;

        public bool IsErasing => _isErasing;

        public bool IsWheelZooming => _isWheelZooming;

        public bool IsContinuousViewportInteraction => _panPointerId is not null || _isManipulating;

        public bool IsContinuousSelectionInteraction => _selectionPointerId is not null || _isManipulatingSelection || _marqueePointerId is not null;

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
            if (_selectedStroke is Stroke stroke)
            {
                if (_session.Document.Strokes.Contains(stroke))
                {
                    return;
                }

                _selectedStroke = null;
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
            _panel.ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY | ManipulationModes.Scale | ManipulationModes.Rotate;
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


    }
}
