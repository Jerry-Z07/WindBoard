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
        private const float EraserRadiusDip = 12.0f;

        private readonly SwapChainPanel _panel;
        private readonly BoardSession _session;
        private readonly BoardViewport _viewport;
        private IBoardEraser _eraser;

        private uint? _activePointerId;
        private uint? _panPointerId;
        private Vector2 _lastPanScreen = Vector2.Zero;
        private PointerDeviceType? _activeStrokeDeviceType;
        private readonly HashSet<uint> _activeTouchPointers = new();
        private bool _isManipulating;
        private bool _isInteracting;
        private bool _isWheelZooming;
        private DateTimeOffset _lastWheelZoomAt;
        private DispatcherQueueTimer? _wheelZoomTimer;
        private Vector2 _pendingPanScreenDelta = Vector2.Zero;
        private Rect? _pendingStrokeDirtyRect;
        private bool _isErasing;
        private Vector2? _lastEraserWorld;
        private List<Stroke>? _eraseBeforeSnapshot;

        public BoardInputController(SwapChainPanel panel, BoardSession session, BoardViewport viewport, IBoardEraser? eraser = null)
        {
            _panel = panel;
            _session = session;
            _viewport = viewport;
            _eraser = eraser ?? new WholeStrokeEraser();
        }

        public BoardTool Tool { get; set; } = BoardTool.Pen;

        public IBoardEraser Eraser
        {
            get => _eraser;
            set => _eraser = value ?? throw new ArgumentNullException(nameof(value));
        }

        public Stroke? ActiveStroke { get; private set; }

        public bool IsErasing => _isErasing;

        public bool IsWheelZooming => _isWheelZooming;

        public bool IsContinuousViewportInteraction => _panPointerId is not null || _isManipulating;

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
            _panel.ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY | ManipulationModes.Scale;
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

            DiscardActiveStroke();
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
            float radiusWorld = EraserRadiusDip / zoom;

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

        private void OnCanvasPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            PointerPoint point = e.GetCurrentPoint(_panel);

            if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch)
            {
                _activeTouchPointers.Add(e.Pointer.PointerId);
                UpdateInteractionState();

                // 多指触摸：交给 Manipulation 处理缩放/拖动；如果正在用“触摸单指画线”，则先结束画线
                if (_activeTouchPointers.Count >= 2)
                {
                    if (ActiveStroke is not null && _activeStrokeDeviceType == PointerDeviceType.Touch)
                    {
                        // 两指及以上时视为手势：如果只是按下的“单点”，不要留下点状笔迹
                        if (ActiveStroke.Points.Count <= 1)
                        {
                            DiscardActiveStroke();
                        }
                        else
                        {
                            CommitActiveStroke();
                        }
                    }
                    else if (_isErasing && _activeStrokeDeviceType == PointerDeviceType.Touch)
                    {
                        CommitEraserGesture();
                    }

                    e.Handled = true;
                    FrameInvalidated?.Invoke();
                    return;
                }

                // 单指触摸：画线 / 擦除
                if (_activePointerId is not null || _panPointerId is not null)
                {
                    return;
                }

                _panel.CapturePointer(e.Pointer);
                _activePointerId = e.Pointer.PointerId;
                _activeStrokeDeviceType = e.Pointer.PointerDeviceType;
                _pendingStrokeDirtyRect = null;

                if (Tool == BoardTool.Eraser)
                {
                    BeginEraserGesture(e.Pointer, point);
                    UpdateInteractionState();
                    e.Handled = true;
                    StateChanged?.Invoke();
                    return;
                }

                ActiveStroke = new Stroke
                {
                    Color = new Color4(0, 0, 0, 1),
                    BaseSize = 3.0f,
                    EnablePressure = true,
                };

                if (AppendPoint(ActiveStroke, e.Pointer, point))
                {
                    FrameInvalidated?.Invoke();
                }
                UpdateInteractionState();
                e.Handled = true;
                StateChanged?.Invoke();
                return;
            }

            if (_activePointerId is not null || _panPointerId is not null)
            {
                return;
            }

            if (ShouldStartPan(e.Pointer, point))
            {
                _panel.CapturePointer(e.Pointer);
                _panPointerId = e.Pointer.PointerId;
                _lastPanScreen = new Vector2((float)point.Position.X, (float)point.Position.Y);
                e.Handled = true;
                UpdateInteractionState();
                FrameInvalidated?.Invoke();
                StateChanged?.Invoke();
                return;
            }

            if (!ShouldStartStroke(e.Pointer, point))
            {
                return;
            }

            _panel.CapturePointer(e.Pointer);
            _activePointerId = e.Pointer.PointerId;
            _activeStrokeDeviceType = e.Pointer.PointerDeviceType;
            _pendingStrokeDirtyRect = null;

            if (Tool == BoardTool.Eraser)
            {
                BeginEraserGesture(e.Pointer, point);
                UpdateInteractionState();
                e.Handled = true;
                StateChanged?.Invoke();
                return;
            }

            ActiveStroke = new Stroke
            {
                Color = new Color4(0, 0, 0, 1),
                BaseSize = 3.0f,
                EnablePressure = true,
            };

            if (AppendPoint(ActiveStroke, e.Pointer, point))
            {
                FrameInvalidated?.Invoke();
            }
            UpdateInteractionState();
            e.Handled = true;
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
            if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch)
            {
                _activeTouchPointers.Remove(e.Pointer.PointerId);
                UpdateInteractionState();
            }

            if (_panPointerId == e.Pointer.PointerId)
            {
                _panPointerId = null;
                _panel.ReleasePointerCaptures();
                e.Handled = true;
                UpdateInteractionState();
                FrameInvalidated?.Invoke();
                StateChanged?.Invoke();
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
            if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch)
            {
                _activeTouchPointers.Remove(e.Pointer.PointerId);
                UpdateInteractionState();
            }

            if (_panPointerId == e.Pointer.PointerId)
            {
                _panPointerId = null;
                _panel.ReleasePointerCaptures();
                e.Handled = true;
                UpdateInteractionState();
                FrameInvalidated?.Invoke();
                StateChanged?.Invoke();
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
            if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch)
            {
                _activeTouchPointers.Remove(e.Pointer.PointerId);
                UpdateInteractionState();
            }

            if (_panPointerId == e.Pointer.PointerId)
            {
                _panPointerId = null;
                e.Handled = true;
                UpdateInteractionState();
                FrameInvalidated?.Invoke();
                StateChanged?.Invoke();
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

        private void OnCanvasPointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (ActiveStroke is not null || _isErasing || _panPointerId is not null)
            {
                return;
            }

            PointerPoint point = e.GetCurrentPoint(_panel);
            int delta = point.Properties.MouseWheelDelta;
            if (delta == 0)
            {
                return;
            }

            BeginWheelZoomInteraction();

            // 以鼠标所在位置为锚点缩放，避免缩放时“跳动”
            float factor = (float)Math.Pow(1.1, delta / 120.0);
            _viewport.ZoomAboutScreenPoint(new Vector2((float)point.Position.X, (float)point.Position.Y), factor);
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
            UpdateInteractionState();
        }

        private void OnCanvasManipulationStarting(object sender, ManipulationStartingRoutedEventArgs e)
        {
            // 触摸手势以 CanvasPanel 为坐标系
            if (ActiveStroke is not null || _isErasing || _panPointerId is not null)
            {
                e.Handled = true;
                return;
            }

            _isManipulating = _activeTouchPointers.Count >= 2;
            UpdateInteractionState();
            FrameInvalidated?.Invoke();
            e.Handled = true;
        }

        private void OnCanvasManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            // 触摸：多指拖动 + 捏合缩放（以手势中心为缩放锚点）
            if (ActiveStroke is not null || _isErasing || _panPointerId is not null)
            {
                e.Handled = true;
                return;
            }

            if (_activeTouchPointers.Count < 2)
            {
                e.Handled = true;
                return;
            }

            if (!_isManipulating)
            {
                _isManipulating = true;
                UpdateInteractionState();
            }

            Vector2 anchor = new((float)e.Position.X, (float)e.Position.Y);

            float scale = (float)e.Delta.Scale;
            if (Math.Abs(scale - 1.0f) > 0.0001f)
            {
                _viewport.ZoomAboutScreenPoint(anchor, scale);
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
            bool isInteracting = ActiveStroke is not null
                || _isErasing
                || _panPointerId is not null
                || _isManipulating
                || _isWheelZooming;

            if (_isInteracting == isInteracting)
            {
                return;
            }

            _isInteracting = isInteracting;
            InteractionStateChanged?.Invoke(isInteracting);
        }
    }
}
