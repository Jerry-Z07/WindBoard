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
using WindBoard.Board.Viewport;
using Vortice.Mathematics;

namespace WindBoard.Interaction
{
    internal sealed class BoardInputController
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
        private Stroke? _selectionTransformStroke;
        private List<StrokePoint>? _selectionBeforeSnapshot;
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
            if (_selectedStroke is null)
            {
                return;
            }

            if (_session.Document.Strokes.Contains(_selectedStroke))
            {
                return;
            }

            _selectedStroke = null;
            FrameInvalidated?.Invoke();
            StateChanged?.Invoke();
        }

        public void ClearSelection()
        {
            SetSelectedStroke(null);
        }

        public void SetSelection(Stroke? stroke)
        {
            SetSelectedStroke(stroke);
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

        public void DiscardActiveStroke()
        {
            if (ActiveStroke is null && _activePointerId is null && _activeStrokeDeviceType is null)
            {
                return;
            }

            ActiveStroke = null;
            _activePointerId = null;
            _activeStrokeDeviceType = null;
            _pendingStrokeDirtyRect = null;
            _panel.ReleasePointerCaptures();
            UpdateInteractionState();
            FrameInvalidated?.Invoke();
            StateChanged?.Invoke();
        }

        public void CancelActiveToolOperation()
        {
            // 外部操作（例如工具切换/撤销/重做/清空）前，用于安全结束当前工具动作，避免留下捕获/状态。
            if (_isErasing)
            {
                CancelEraserGesture();
                return;
            }

            if (_marqueePointerId is not null)
            {
                CancelMarqueeSelectionGesture(releasePointerCaptures: true);
                return;
            }

            if (_selectionPointerId is not null || _isManipulatingSelection)
            {
                CancelSelectionGesture();
                return;
            }

            if (_panPointerId is not null)
            {
                CancelPanGesture();
                return;
            }

            DiscardActiveStroke();
        }

        private void CancelPanGesture()
        {
            _panPointerId = null;
            _pendingPanScreenDelta = Vector2.Zero;
            _panel.ReleasePointerCaptures();
            UpdateInteractionState();
            FrameInvalidated?.Invoke();
            StateChanged?.Invoke();
        }

        private void BeginMarqueeSelectionGesture(Pointer pointer, Vector2 startScreenDip)
        {
            _panel.CapturePointer(pointer);
            _marqueePointerId = pointer.PointerId;
            _marqueeStartScreen = startScreenDip;
            _marqueeCurrentScreen = startScreenDip;

            UpdateInteractionState();
            FrameInvalidated?.Invoke();
            StateChanged?.Invoke();
        }

        private void CommitMarqueeSelectionGesture(bool releasePointerCaptures)
        {
            uint? id = _marqueePointerId;
            if (id is null)
            {
                return;
            }

            Vector2 start = _marqueeStartScreen;
            Vector2 current = _marqueeCurrentScreen;
            Rect rectDip = CreateRectFromTwoPoints(start, current);

            _marqueePointerId = null;

            if (releasePointerCaptures)
            {
                _panel.ReleasePointerCaptures();
            }

            Stroke? selected = null;

            // 小于阈值时按“点击”处理，避免用户轻微抖动导致无法点选。
            if (rectDip.Width <= MarqueeClickThresholdDip && rectDip.Height <= MarqueeClickThresholdDip)
            {
                selected = HitTestStrokeAtScreenPoint(start);
            }
            else
            {
                // 框选：把屏幕矩形转换为世界坐标 AABB。
                Vector2 worldTopLeft = _viewport.ScreenToWorld(new Vector2(rectDip.Left, rectDip.Top));
                Vector2 worldBottomRight = _viewport.ScreenToWorld(new Vector2(rectDip.Right, rectDip.Bottom));

                Vector2 minWorld = new(
                    Math.Min(worldTopLeft.X, worldBottomRight.X),
                    Math.Min(worldTopLeft.Y, worldBottomRight.Y));
                Vector2 maxWorld = new(
                    Math.Max(worldTopLeft.X, worldBottomRight.X),
                    Math.Max(worldTopLeft.Y, worldBottomRight.Y));

                selected = StrokeRectSelectTest.HitTestTopMostStrokeInWorldRect(_session.Document.Strokes, minWorld, maxWorld);
            }

            SetSelectedStroke(selected);

            UpdateInteractionState();
            FrameInvalidated?.Invoke();
            StateChanged?.Invoke();
        }

        private void CancelMarqueeSelectionGesture(bool releasePointerCaptures)
        {
            if (_marqueePointerId is null)
            {
                return;
            }

            _marqueePointerId = null;

            if (releasePointerCaptures)
            {
                _panel.ReleasePointerCaptures();
            }

            UpdateInteractionState();
            FrameInvalidated?.Invoke();
            StateChanged?.Invoke();
        }

        private void BeginSelectionTransformSnapshot(Stroke stroke)
        {
            _selectionTransformStroke = stroke;
            _selectionBeforeSnapshot = new List<StrokePoint>(stroke.Points);
            _selectionModified = false;
        }

        private void CommitSelectionGesture(bool releasePointerCaptures)
        {
            Stroke? stroke = _selectionTransformStroke;
            List<StrokePoint>? before = _selectionBeforeSnapshot;

            _selectionPointerId = null;
            _isManipulatingSelection = false;

            if (releasePointerCaptures)
            {
                _panel.ReleasePointerCaptures();
            }

            _selectionTransformStroke = null;
            _selectionBeforeSnapshot = null;

            if (stroke is not null && before is not null && _selectionModified)
            {
                var after = new List<StrokePoint>(stroke.Points);
                if (!IsSameStrokePointList(before, after))
                {
                    _session.Execute(new UpdateStrokePointsCommand(stroke, before, after));
                }
            }

            _selectionModified = false;
            UpdateInteractionState();
            FrameInvalidated?.Invoke();
            StateChanged?.Invoke();
        }

        private void CancelSelectionGesture(bool releasePointerCaptures = true)
        {
            if (_selectionTransformStroke is not null && _selectionBeforeSnapshot is not null)
            {
                RestoreStrokePoints(_selectionTransformStroke, _selectionBeforeSnapshot);
            }

            _selectionPointerId = null;
            _isManipulatingSelection = false;
            _selectionTransformStroke = null;
            _selectionBeforeSnapshot = null;
            _selectionModified = false;
            _touchManipulationTarget = TouchManipulationTarget.Viewport;

            if (releasePointerCaptures)
            {
                _panel.ReleasePointerCaptures();
            }

            UpdateInteractionState();
            FrameInvalidated?.Invoke();
            StateChanged?.Invoke();
        }

        private static void RestoreStrokePoints(Stroke stroke, List<StrokePoint> snapshot)
        {
            stroke.Points.Clear();
            stroke.Points.AddRange(snapshot);
            stroke.RecalculateBoundsFromPoints();
        }

        private void CommitActiveStroke()
        {
            if (ActiveStroke is not null && ActiveStroke.Points.Count > 0)
            {
                _session.Execute(new AddStrokeCommand(ActiveStroke));
            }

            ActiveStroke = null;
            _activePointerId = null;
            _activeStrokeDeviceType = null;
            _pendingStrokeDirtyRect = null;
            _panel.ReleasePointerCaptures();
            UpdateInteractionState();
            FrameInvalidated?.Invoke();
            StateChanged?.Invoke();
        }

        private void BeginEraserGesture(Pointer pointer, PointerPoint point)
        {
            // 记录擦除前的快照：整笔擦除与未来局部擦除都可以复用这套“前后快照 + 单条命令”机制。
            _eraseBeforeSnapshot = new List<Stroke>(_session.Document.Strokes);
            _isErasing = true;
            _pendingStrokeDirtyRect = null;

            Vector2 screen = new((float)point.Position.X, (float)point.Position.Y);
            Vector2 world = _viewport.ScreenToWorld(screen);
            _lastEraserWorld = world;

            ApplyEraserSegment(world, world);
        }

        private void UpdateEraserGesture(Pointer pointer, PointerPoint point)
        {
            Vector2 screen = new((float)point.Position.X, (float)point.Position.Y);
            Vector2 currentWorld = _viewport.ScreenToWorld(screen);

            if (_lastEraserWorld is not Vector2 lastWorld)
            {
                _lastEraserWorld = currentWorld;
                ApplyEraserSegment(currentWorld, currentWorld);
                return;
            }

            float minDistWorld = 0.75f / Math.Max(0.0001f, _viewport.Zoom);
            if (Vector2.DistanceSquared(lastWorld, currentWorld) < minDistWorld * minDistWorld)
            {
                return;
            }

            _lastEraserWorld = currentWorld;
            ApplyEraserSegment(lastWorld, currentWorld);
        }

        private void ApplyEraserSegment(Vector2 fromWorld, Vector2 toWorld)
        {
            float zoom = Math.Max(0.0001f, _viewport.Zoom);
            Vector2 radiusWorld = EraserRadiusDip / zoom;

            if (_eraser.Erase(_session.Document, fromWorld, toWorld, radiusWorld))
            {
                FrameInvalidated?.Invoke();
            }
        }

        private void CommitEraserGesture()
        {
            if (!_isErasing)
            {
                return;
            }

            List<Stroke>? before = _eraseBeforeSnapshot;
            _eraseBeforeSnapshot = null;

            _isErasing = false;
            _lastEraserWorld = null;

            if (before is not null)
            {
                var after = new List<Stroke>(_session.Document.Strokes);
                if (!IsSameStrokeList(before, after))
                {
                    _session.Execute(new ReplaceStrokesCommand(before, after));
                }
            }

            _activePointerId = null;
            _activeStrokeDeviceType = null;
            _panel.ReleasePointerCaptures();
            UpdateInteractionState();
            FrameInvalidated?.Invoke();
            StateChanged?.Invoke();
        }

        private void CancelEraserGesture()
        {
            if (!_isErasing)
            {
                return;
            }

            // 系统取消/外部打断时：恢复擦除前快照，不写入撤销栈，避免产生“半截”历史。
            if (_eraseBeforeSnapshot is not null)
            {
                _session.Document.Strokes.Clear();
                _session.Document.Strokes.AddRange(_eraseBeforeSnapshot);
            }

            _eraseBeforeSnapshot = null;
            _isErasing = false;
            _lastEraserWorld = null;

            _activePointerId = null;
            _activeStrokeDeviceType = null;
            _panel.ReleasePointerCaptures();
            UpdateInteractionState();
            FrameInvalidated?.Invoke();
            StateChanged?.Invoke();
        }

        private static bool IsSameStrokeList(List<Stroke> a, List<Stroke> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                if (!ReferenceEquals(a[i], b[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsSameStrokePointList(List<StrokePoint> a, List<StrokePoint> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        private void OnCanvasPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            PointerPoint point = e.GetCurrentPoint(_panel);

            if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch)
            {
                HandleTouchPointerPressed(e, point);
                return;
            }

            HandleNonTouchPointerPressed(e, point);
        }

        private void HandleTouchPointerPressed(PointerRoutedEventArgs e, PointerPoint point)
        {
            _activeTouchPointers.Add(e.Pointer.PointerId);
            UpdateInteractionState();

            // 多指触摸：交给 Manipulation 处理缩放/拖动；如果正在用“触摸单指画线/擦除”，则先结束。
            if (_activeTouchPointers.Count >= 2)
            {
                EndTouchSingleFingerToolOperationForManipulation();
                e.Handled = true;
                FrameInvalidated?.Invoke();
                return;
            }

            if (Tool == BoardTool.Select)
            {
                // 选择模式：单指用于“框选”；双指/多指用于视口手势或对已选中笔迹做变换。
                Vector2 screen = new((float)point.Position.X, (float)point.Position.Y);
                _touchManipulationTarget = IsScreenPointInsideSelectedStrokeBounds(screen)
                    ? TouchManipulationTarget.Selection
                    : TouchManipulationTarget.Viewport;
                BeginMarqueeSelectionGesture(e.Pointer, screen);
                e.Handled = true;
                return;
            }

            // 单指触摸：画线 / 擦除
            if (HasActivePointerCapture)
            {
                return;
            }

            BeginStrokeOrEraserGesture(e.Pointer, point);
            e.Handled = true;
            StateChanged?.Invoke();
        }

        private void EndTouchSingleFingerToolOperationForManipulation()
        {
            if (ActiveStroke is not null && _activeStrokeDeviceType == PointerDeviceType.Touch)
            {
                // 两指及以上时视为手势：如果只是按下的“单点”，不要留下点状笔迹。
                if (ActiveStroke.Points.Count <= 1)
                {
                    DiscardActiveStroke();
                }
                else
                {
                    CommitActiveStroke();
                }

                return;
            }

            if (_isErasing && _activeStrokeDeviceType == PointerDeviceType.Touch)
            {
                CommitEraserGesture();
            }

            // 选择框选：当用户从单指切换为双指/多指时，取消框选，交给 Manipulation 处理缩放/拖动。
            if (_marqueePointerId is not null)
            {
                CancelMarqueeSelectionGesture(releasePointerCaptures: true);
            }
        }

        private void HandleNonTouchPointerPressed(PointerRoutedEventArgs e, PointerPoint point)
        {
            if (HasActivePointerCapture)
            {
                return;
            }

            if (Tool == BoardTool.Select)
            {
                // 选择模式（框选）：
                // - 鼠标右键：平移视口
                // - 其它：单指/鼠标左键/触控笔拖拽 → 框选；在已选中笔迹范围内拖拽 → 移动选中笔迹
                if (ShouldStartPan(e.Pointer, point))
                {
                    BeginPanGesture(e.Pointer, point);
                    e.Handled = true;
                    return;
                }

                if (!ShouldStartStroke(e.Pointer, point))
                {
                    return;
                }

                Vector2 screen = new((float)point.Position.X, (float)point.Position.Y);
                if (IsScreenPointInsideSelectedStrokeBounds(screen))
                {
                    BeginSelectionMoveGesture(e.Pointer, screen);
                    e.Handled = true;
                    return;
                }

                BeginMarqueeSelectionGesture(e.Pointer, screen);
                e.Handled = true;
                return;
            }

            if (ShouldStartPan(e.Pointer, point))
            {
                BeginPanGesture(e.Pointer, point);
                e.Handled = true;
                return;
            }

            if (!ShouldStartStroke(e.Pointer, point))
            {
                return;
            }

            BeginStrokeOrEraserGesture(e.Pointer, point);
            e.Handled = true;
            StateChanged?.Invoke();
        }

        private bool HasActivePointerCapture => _activePointerId is not null || _panPointerId is not null || _selectionPointerId is not null || _marqueePointerId is not null;

        private Stroke? HitTestStrokeAtScreenPoint(Vector2 screenDip)
        {
            Vector2 pointWorld = _viewport.ScreenToWorld(screenDip);
            float toleranceWorld = SelectHitToleranceDip / Math.Max(0.0001f, _viewport.Zoom);
            return StrokePickTest.HitTestTopMostStroke(_session.Document.Strokes, pointWorld, toleranceWorld);
        }

        private bool IsScreenPointInsideSelectedStrokeBounds(Vector2 screenDip)
        {
            if (_selectedStroke is not Stroke stroke || stroke.Points.Count == 0)
            {
                return false;
            }

            // 某些情况下笔迹可能还未计算 Bounds（例如外部构造/导入），这里兜底重建。
            if (!stroke.HasBounds)
            {
                stroke.RecalculateBoundsFromPoints();
            }

            if (!stroke.HasBounds)
            {
                return false;
            }

            Matrix3x2 worldToScreen = _viewport.GetWorldToScreenTransform();
            Vector2 minScreen = Vector2.Transform(stroke.BoundsMin, worldToScreen);
            Vector2 maxScreen = Vector2.Transform(stroke.BoundsMax, worldToScreen);

            float left = Math.Min(minScreen.X, maxScreen.X) - SelectHitToleranceDip;
            float top = Math.Min(minScreen.Y, maxScreen.Y) - SelectHitToleranceDip;
            float right = Math.Max(minScreen.X, maxScreen.X) + SelectHitToleranceDip;
            float bottom = Math.Max(minScreen.Y, maxScreen.Y) + SelectHitToleranceDip;

            return screenDip.X >= left && screenDip.X <= right && screenDip.Y >= top && screenDip.Y <= bottom;
        }

        private static Rect CreateRectFromTwoPoints(Vector2 a, Vector2 b)
        {
            float left = Math.Min(a.X, b.X);
            float top = Math.Min(a.Y, b.Y);
            float right = Math.Max(a.X, b.X);
            float bottom = Math.Max(a.Y, b.Y);
            // Rect 的构造函数是 (x, y, width, height)，这里应使用 FromLTRB 构造，避免把 right/bottom 误当作 width/height。
            return Rect.FromLTRB(left, top, right, bottom);
        }

        private static Vector2 GetStrokeCenterWorld(Stroke stroke)
        {
            if (stroke.HasBounds)
            {
                return (stroke.BoundsMin + stroke.BoundsMax) / 2.0f;
            }

            // 某些情况下笔迹可能还未计算 Bounds（例如外部构造/导入），此时退化为“点集平均”。
            if (stroke.Points.Count == 0)
            {
                return Vector2.Zero;
            }

            Vector2 sum = Vector2.Zero;
            for (int i = 0; i < stroke.Points.Count; i++)
            {
                sum += stroke.Points[i].Position;
            }

            return sum / stroke.Points.Count;
        }

        private void SetSelectedStroke(Stroke? stroke)
        {
            if (ReferenceEquals(_selectedStroke, stroke))
            {
                return;
            }

            _selectedStroke = stroke;
            FrameInvalidated?.Invoke();
            StateChanged?.Invoke();
        }

        private void BeginStrokeOrEraserGesture(Pointer pointer, PointerPoint point)
        {
            CaptureStrokePointer(pointer);

            if (Tool == BoardTool.Eraser)
            {
                BeginEraserGesture(pointer, point);
                UpdateInteractionState();
                return;
            }

            ActiveStroke = CreateNewStroke();
            if (AppendPoint(ActiveStroke, pointer, point))
            {
                FrameInvalidated?.Invoke();
            }

            UpdateInteractionState();
        }

        private void CaptureStrokePointer(Pointer pointer)
        {
            _panel.CapturePointer(pointer);
            _activePointerId = pointer.PointerId;
            _activeStrokeDeviceType = pointer.PointerDeviceType;
            _pendingStrokeDirtyRect = null;
        }

        private Stroke CreateNewStroke()
        {
            return new Stroke
            {
                Color = PenColor,
                BaseSize = PenBaseSize,
                EnablePressure = PenEnablePressure,
            };
        }

        private void BeginPanGesture(Pointer pointer, PointerPoint point)
        {
            _panel.CapturePointer(pointer);
            _panPointerId = pointer.PointerId;
            _lastPanScreen = new Vector2((float)point.Position.X, (float)point.Position.Y);
            UpdateInteractionState();
            FrameInvalidated?.Invoke();
            StateChanged?.Invoke();
        }

        private void BeginSelectionMoveGesture(Pointer pointer, Vector2 screenDip)
        {
            if (_selectedStroke is null)
            {
                return;
            }

            _panel.CapturePointer(pointer);
            _selectionPointerId = pointer.PointerId;
            _lastSelectionScreen = screenDip;

            BeginSelectionTransformSnapshot(_selectedStroke);

            UpdateInteractionState();
            FrameInvalidated?.Invoke();
            StateChanged?.Invoke();
        }

        private void OnCanvasPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_panPointerId == e.Pointer.PointerId)
            {
                PointerPoint point = e.GetCurrentPoint(_panel);
                Vector2 current = new((float)point.Position.X, (float)point.Position.Y);
                Vector2 delta = current - _lastPanScreen;
                _lastPanScreen = current;
                _viewport.PanByScreenDelta(delta);
                _pendingPanScreenDelta += delta;
                e.Handled = true;
                FrameInvalidated?.Invoke();
                return;
            }

            if (_selectionPointerId == e.Pointer.PointerId)
            {
                PointerPoint point = e.GetCurrentPoint(_panel);
                Vector2 current = new((float)point.Position.X, (float)point.Position.Y);
                Vector2 deltaScreen = current - _lastSelectionScreen;
                _lastSelectionScreen = current;

                if (_selectionTransformStroke is not null)
                {
                    Vector2 deltaWorld = deltaScreen / Math.Max(0.0001f, _viewport.Zoom);
                    _selectionTransformStroke.Translate(deltaWorld);
                }

                if (deltaScreen.LengthSquared() > 0.0001f)
                {
                    _selectionModified = true;
                }

                e.Handled = true;
                FrameInvalidated?.Invoke();
                return;
            }

            if (_marqueePointerId == e.Pointer.PointerId)
            {
                PointerPoint point = e.GetCurrentPoint(_panel);
                _marqueeCurrentScreen = new Vector2((float)point.Position.X, (float)point.Position.Y);
                e.Handled = true;
                FrameInvalidated?.Invoke();
                return;
            }

            if (_activePointerId != e.Pointer.PointerId)
            {
                return;
            }

            if (_isErasing)
            {
                PointerPoint erasePoint = e.GetCurrentPoint(_panel);
                UpdateEraserGesture(e.Pointer, erasePoint);
                e.Handled = true;
                return;
            }

            if (ActiveStroke is null)
            {
                return;
            }

            PointerPoint point2 = e.GetCurrentPoint(_panel);
            if (AppendPoint(ActiveStroke, e.Pointer, point2))
            {
                FrameInvalidated?.Invoke();
            }
            e.Handled = true;
        }

        private void OnCanvasPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            HandleTouchPointerEnded(e);

            if (TryHandlePanPointerEnded(e, releasePointerCaptures: true))
            {
                return;
            }

            if (_marqueePointerId == e.Pointer.PointerId)
            {
                CommitMarqueeSelectionGesture(releasePointerCaptures: true);
                e.Handled = true;
                return;
            }

            if (_selectionPointerId == e.Pointer.PointerId)
            {
                CommitSelectionGesture(releasePointerCaptures: true);
                e.Handled = true;
                return;
            }

            if (_activePointerId != e.Pointer.PointerId)
            {
                return;
            }

            if (_isErasing)
            {
                CommitEraserGesture();
                e.Handled = true;
                return;
            }

            CommitActiveStroke();
            e.Handled = true;
        }

        private void OnCanvasPointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            HandleTouchPointerEnded(e);

            if (TryHandlePanPointerEnded(e, releasePointerCaptures: true))
            {
                return;
            }

            if (_marqueePointerId == e.Pointer.PointerId)
            {
                CancelMarqueeSelectionGesture(releasePointerCaptures: true);
                e.Handled = true;
                return;
            }

            if (_selectionPointerId == e.Pointer.PointerId)
            {
                CancelSelectionGesture(releasePointerCaptures: true);
                e.Handled = true;
                return;
            }

            if (_activePointerId != e.Pointer.PointerId)
            {
                return;
            }

            if (_isErasing)
            {
                CancelEraserGesture();
                e.Handled = true;
                return;
            }

            DiscardActiveStroke();
            e.Handled = true;
        }

        private void OnCanvasPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            HandleTouchPointerEnded(e);

            if (TryHandlePanPointerEnded(e, releasePointerCaptures: false))
            {
                return;
            }

            if (_marqueePointerId == e.Pointer.PointerId)
            {
                CommitMarqueeSelectionGesture(releasePointerCaptures: false);
                e.Handled = true;
                return;
            }

            if (_selectionPointerId == e.Pointer.PointerId)
            {
                // 捕获丢失时尽量提交，避免用户看到“已经移动了但撤销栈没有记录”的不一致。
                CommitSelectionGesture(releasePointerCaptures: false);
                e.Handled = true;
                return;
            }

            if (_activePointerId != e.Pointer.PointerId)
            {
                return;
            }

            if (_isErasing)
            {
                CommitEraserGesture();
                e.Handled = true;
                return;
            }

            CommitActiveStroke();
            e.Handled = true;
        }

        private void HandleTouchPointerEnded(PointerRoutedEventArgs e)
        {
            if (e.Pointer.PointerDeviceType != PointerDeviceType.Touch)
            {
                return;
            }

            _activeTouchPointers.Remove(e.Pointer.PointerId);
            UpdateInteractionState();
        }

        private bool TryHandlePanPointerEnded(PointerRoutedEventArgs e, bool releasePointerCaptures)
        {
            if (_panPointerId != e.Pointer.PointerId)
            {
                return false;
            }

            _panPointerId = null;

            if (releasePointerCaptures)
            {
                _panel.ReleasePointerCaptures();
            }

            e.Handled = true;
            UpdateInteractionState();
            FrameInvalidated?.Invoke();
            StateChanged?.Invoke();
            return true;
        }

        private void OnCanvasPointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (ActiveStroke is not null || _isErasing || _panPointerId is not null || _selectionPointerId is not null || _isManipulatingSelection || _marqueePointerId is not null)
            {
                return;
            }

            PointerPoint point = e.GetCurrentPoint(_panel);
            int delta = point.Properties.MouseWheelDelta;
            if (delta == 0)
            {
                return;
            }

            Windows.System.VirtualKeyModifiers mods = e.KeyModifiers;

            // 选择模式下，按住修饰键对“选中笔迹”做变换：
            // - Ctrl + 滚轮：缩放
            // - Shift + 滚轮：旋转
            if (Tool == BoardTool.Select && _selectedStroke is not null
                && (mods.HasFlag(Windows.System.VirtualKeyModifiers.Control) || mods.HasFlag(Windows.System.VirtualKeyModifiers.Shift)))
            {
                BeginWheelZoomInteraction();

                if (_selectionBeforeSnapshot is null || !ReferenceEquals(_selectionTransformStroke, _selectedStroke))
                {
                    BeginSelectionTransformSnapshot(_selectedStroke);
                }

                if (mods.HasFlag(Windows.System.VirtualKeyModifiers.Control))
                {
                    // 以鼠标所在位置为锚点缩放，避免缩放时“跳动”。
                    float factor = (float)Math.Pow(1.1, delta / 120.0);
                    Vector2 anchorScreen = new((float)point.Position.X, (float)point.Position.Y);
                    Vector2 anchorWorld = _viewport.ScreenToWorld(anchorScreen);
                    Matrix3x2 transform = Matrix3x2.CreateTranslation(-anchorWorld)
                        * Matrix3x2.CreateScale(factor)
                        * Matrix3x2.CreateTranslation(anchorWorld);
                    _selectedStroke.Transform(transform);
                    _selectionModified = true;
                }

                if (mods.HasFlag(Windows.System.VirtualKeyModifiers.Shift))
                {
                    // 以笔迹中心为锚点旋转（避免滚轮旋转时锚点漂移）。
                    float stepDeg = 5.0f;
                    float rotationRad = stepDeg * (delta / 120.0f) * (float)(Math.PI / 180.0);
                    Vector2 centerWorld = GetStrokeCenterWorld(_selectedStroke);
                    _selectedStroke.Transform(Matrix3x2.CreateRotation(rotationRad, centerWorld));
                    _selectionModified = true;
                }

                e.Handled = true;
                FrameInvalidated?.Invoke();
                return;
            }

            BeginWheelZoomInteraction();

            // 以鼠标所在位置为锚点缩放，避免缩放时“跳动”
            float factor2 = (float)Math.Pow(1.1, delta / 120.0);
            _viewport.ZoomAboutScreenPoint(new Vector2((float)point.Position.X, (float)point.Position.Y), factor2);
            e.Handled = true;
            FrameInvalidated?.Invoke();
        }

        private void BeginWheelZoomInteraction()
        {
            _lastWheelZoomAt = DateTimeOffset.UtcNow;

            if (_wheelZoomTimer is null)
            {
                _wheelZoomTimer = _panel.DispatcherQueue.CreateTimer();
                _wheelZoomTimer.Interval = TimeSpan.FromMilliseconds(WheelZoomTimerIntervalMs);
                _wheelZoomTimer.IsRepeating = true;
                _wheelZoomTimer.Tick += OnWheelZoomTimerTick;
            }

            if (!_wheelZoomTimer.IsRunning)
            {
                _wheelZoomTimer.Start();
            }

            if (_isWheelZooming)
            {
                return;
            }

            _isWheelZooming = true;
            UpdateInteractionState();
        }

        private void OnWheelZoomTimerTick(DispatcherQueueTimer sender, object args)
        {
            if (!_isWheelZooming)
            {
                sender.Stop();
                return;
            }

            if ((DateTimeOffset.UtcNow - _lastWheelZoomAt).TotalMilliseconds < WheelZoomIdleTimeoutMs)
            {
                return;
            }

            _isWheelZooming = false;
            sender.Stop();

            // Wheel 交互结束时，如果期间对选中笔迹做了变换，则在此一次性写入撤销记录。
            if (_selectionBeforeSnapshot is not null && _selectionTransformStroke is not null && _selectionModified)
            {
                CommitSelectionGesture(releasePointerCaptures: false);
                return;
            }

            UpdateInteractionState();
        }

        private void OnCanvasManipulationStarting(object sender, ManipulationStartingRoutedEventArgs e)
        {
            // 触摸手势以 CanvasPanel 为坐标系
            if (ActiveStroke is not null || _isErasing || _panPointerId is not null || _selectionPointerId is not null || _marqueePointerId is not null)
            {
                e.Handled = true;
                return;
            }

            // 默认：双指/多指才进入手势模式（选择工具也不使用单指平移，避免与后续“框选”冲突）。
            const int minTouchCount = 2;
            if (Tool == BoardTool.Select
                && _touchManipulationTarget == TouchManipulationTarget.Selection
                && _selectedStroke is not null)
            {
                _isManipulating = false;
                _isManipulatingSelection = _activeTouchPointers.Count >= minTouchCount;
                if (_isManipulatingSelection)
                {
                    BeginSelectionTransformSnapshot(_selectedStroke);
                }
            }
            else
            {
                _touchManipulationTarget = TouchManipulationTarget.Viewport;
                _isManipulatingSelection = false;
                _isManipulating = _activeTouchPointers.Count >= minTouchCount;
            }

            UpdateInteractionState();
            FrameInvalidated?.Invoke();
            e.Handled = true;
        }

        private void OnCanvasManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            // 触摸：多指拖动 + 捏合缩放（以手势中心为缩放锚点）
            if (ActiveStroke is not null || _isErasing || _panPointerId is not null || _selectionPointerId is not null || _marqueePointerId is not null)
            {
                e.Handled = true;
                return;
            }

            const int minTouchCount = 2;
            if (_activeTouchPointers.Count < minTouchCount)
            {
                e.Handled = true;
                return;
            }

            if (Tool == BoardTool.Select
                && _touchManipulationTarget == TouchManipulationTarget.Selection
                && _selectedStroke is not null)
            {
                if (!_isManipulatingSelection)
                {
                    _isManipulatingSelection = true;
                    BeginSelectionTransformSnapshot(_selectedStroke);
                    UpdateInteractionState();
                }

                Vector2 anchorScreen = new((float)e.Position.X, (float)e.Position.Y);
                Vector2 anchorWorld = _viewport.ScreenToWorld(anchorScreen);

                Vector2 translationScreen = new((float)e.Delta.Translation.X, (float)e.Delta.Translation.Y);
                Vector2 translationWorld = translationScreen / Math.Max(0.0001f, _viewport.Zoom);

                float scale = (float)e.Delta.Scale;
                float rotationDeg = (float)e.Delta.Rotation;
                float rotationRad = rotationDeg * (float)(Math.PI / 180.0);

                bool hasScale = Math.Abs(scale - 1.0f) > 0.0001f;
                bool hasRotation = Math.Abs(rotationRad) > 0.0001f;
                bool hasTranslation = translationWorld.LengthSquared() > 0.0001f;

                if (hasScale || hasRotation)
                {
                    // 注意：这里的增量（Delta）是“逐帧增量”，因此直接对当前点集做增量变换即可。
                    Matrix3x2 transform = Matrix3x2.Identity;

                    if (hasScale)
                    {
                        transform *= Matrix3x2.CreateTranslation(-anchorWorld)
                            * Matrix3x2.CreateScale(scale)
                            * Matrix3x2.CreateTranslation(anchorWorld);
                    }

                    if (hasRotation)
                    {
                        transform *= Matrix3x2.CreateRotation(rotationRad, anchorWorld);
                    }

                    if (hasTranslation)
                    {
                        transform *= Matrix3x2.CreateTranslation(translationWorld);
                    }

                    _selectedStroke.Transform(transform);
                    _selectionModified = true;
                }
                else if (hasTranslation)
                {
                    _selectedStroke.Translate(translationWorld);
                    _selectionModified = true;
                }

                e.Handled = true;
                FrameInvalidated?.Invoke();
                return;
            }

            if (!_isManipulating)
            {
                _isManipulating = true;
                UpdateInteractionState();
            }

            Vector2 anchor = new((float)e.Position.X, (float)e.Position.Y);

            // 单指：只做平移；双指及以上：平移 + 捏合缩放。
            if (_activeTouchPointers.Count >= 2)
            {
                float scale = (float)e.Delta.Scale;
                if (Math.Abs(scale - 1.0f) > 0.0001f)
                {
                    _viewport.ZoomAboutScreenPoint(anchor, scale);
                }
            }

            Vector2 translation = new((float)e.Delta.Translation.X, (float)e.Delta.Translation.Y);
            if (translation.LengthSquared() > 0.0001f)
            {
                _viewport.PanByScreenDelta(translation);
                _pendingPanScreenDelta += translation;
            }

            e.Handled = true;
            FrameInvalidated?.Invoke();
        }

        private void OnCanvasManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            // 在三指及以上的复杂触摸手势下，系统可能不会为每个触点都完整触发 PointerReleased/PointerCanceled。
            // 为避免触点残留导致始终被判定为“多指”，这里在手势结束时强制清空触摸状态。
            _activeTouchPointers.Clear();
            _touchManipulationTarget = TouchManipulationTarget.Viewport;

            if (_isManipulatingSelection)
            {
                _isManipulating = false;
                CommitSelectionGesture(releasePointerCaptures: false);
                e.Handled = true;
                return;
            }

            _isManipulating = false;
            UpdateInteractionState();
            FrameInvalidated?.Invoke();
            e.Handled = true;
        }

        private bool AppendPoint(Stroke stroke, Pointer pointer, PointerPoint point)
        {
            Vector2 screen = new((float)point.Position.X, (float)point.Position.Y);
            Vector2 pos = _viewport.ScreenToWorld(screen);
            float pressure = GetNormalizedPressure(pointer.PointerDeviceType, point.Properties);

            if (stroke.Points.Count > 0)
            {
                Vector2 last = stroke.Points[^1].Position;
                float minDistWorld = 0.5f / Math.Max(0.0001f, _viewport.Zoom);
                if (Vector2.DistanceSquared(last, pos) < minDistWorld * minDistWorld)
                {
                    return false;
                }
            }

            stroke.Points.Add(new StrokePoint(pos, pressure));
            stroke.ExpandBounds(pos, pressure);

            _pendingStrokeDirtyRect = BoardInputDirtyRectCalculator.UpdatePendingStrokeDirtyRect(
                _pendingStrokeDirtyRect,
                stroke,
                _viewport,
                screen,
                DirtyRectExtraDip);
            return true;
        }

        private static bool ShouldStartStroke(Pointer pointer, PointerPoint point)
        {
            if (pointer.PointerDeviceType == PointerDeviceType.Mouse)
            {
                return point.Properties.IsLeftButtonPressed;
            }

            // 触控笔 / 触摸：默认允许
            return true;
        }

        private static bool ShouldStartPan(Pointer pointer, PointerPoint point)
        {
            if (pointer.PointerDeviceType != PointerDeviceType.Mouse)
            {
                return false;
            }

            return point.Properties.IsRightButtonPressed;
        }

        private static float GetNormalizedPressure(PointerDeviceType pointerDeviceType, PointerPointProperties props)
        {
            if (pointerDeviceType != PointerDeviceType.Pen)
            {
                return 1.0f;
            }

            float p = (float)props.Pressure;
            return Math.Clamp(p, 0.1f, 1.0f);
        }

        private void UpdateInteractionState()
        {
            bool hasActiveTool = ActiveStroke is not null || _isErasing;
            bool hasViewportGesture = _panPointerId is not null || _isManipulating;
            bool hasSelectionGesture = _selectionPointerId is not null || _isManipulatingSelection || _marqueePointerId is not null;
            bool isInteracting = hasActiveTool || hasViewportGesture || hasSelectionGesture || _isWheelZooming;

            if (_isInteracting == isInteracting)
            {
                return;
            }

            _isInteracting = isInteracting;
            InteractionStateChanged?.Invoke(isInteracting);
        }
    }
}
