using System;
using System.Collections.Generic;
using System.Numerics;
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
        private readonly SwapChainPanel _panel;
        private readonly BoardSession _session;
        private readonly BoardViewport _viewport;

        private uint? _activePointerId;
        private uint? _panPointerId;
        private Vector2 _lastPanScreen = Vector2.Zero;
        private PointerDeviceType? _activeStrokeDeviceType;
        private readonly HashSet<uint> _activeTouchPointers = new();

        public BoardInputController(SwapChainPanel panel, BoardSession session, BoardViewport viewport)
        {
            _panel = panel;
            _session = session;
            _viewport = viewport;
        }

        public Stroke? ActiveStroke { get; private set; }

        public event Action? StateChanged;

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
            _panel.ReleasePointerCaptures();
            StateChanged?.Invoke();
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
            _panel.ReleasePointerCaptures();
            StateChanged?.Invoke();
        }

        private void OnCanvasPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            PointerPoint point = e.GetCurrentPoint(_panel);

            if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch)
            {
                _activeTouchPointers.Add(e.Pointer.PointerId);

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

                    e.Handled = true;
                    return;
                }

                // 单指触摸：画线
                if (_activePointerId is not null || _panPointerId is not null)
                {
                    return;
                }

                _panel.CapturePointer(e.Pointer);
                _activePointerId = e.Pointer.PointerId;
                _activeStrokeDeviceType = e.Pointer.PointerDeviceType;

                ActiveStroke = new Stroke
                {
                    Color = new Color4(0, 0, 0, 1),
                    BaseSize = 3.0f,
                    EnablePressure = true,
                };

                AppendPoint(ActiveStroke, e.Pointer, point);
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

            ActiveStroke = new Stroke
            {
                Color = new Color4(0, 0, 0, 1),
                BaseSize = 3.0f,
                EnablePressure = true,
            };

            AppendPoint(ActiveStroke, e.Pointer, point);
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
                e.Handled = true;
                return;
            }

            if (_activePointerId != e.Pointer.PointerId)
            {
                return;
            }

            if (ActiveStroke is null)
            {
                return;
            }

            PointerPoint point2 = e.GetCurrentPoint(_panel);
            AppendPoint(ActiveStroke, e.Pointer, point2);
            e.Handled = true;
        }

        private void OnCanvasPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch)
            {
                _activeTouchPointers.Remove(e.Pointer.PointerId);
            }

            if (_panPointerId == e.Pointer.PointerId)
            {
                _panPointerId = null;
                _panel.ReleasePointerCaptures();
                e.Handled = true;
                StateChanged?.Invoke();
                return;
            }

            if (_activePointerId != e.Pointer.PointerId)
            {
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
            }

            if (_panPointerId == e.Pointer.PointerId)
            {
                _panPointerId = null;
                _panel.ReleasePointerCaptures();
                e.Handled = true;
                StateChanged?.Invoke();
                return;
            }

            if (_activePointerId != e.Pointer.PointerId)
            {
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
            }

            if (_panPointerId == e.Pointer.PointerId)
            {
                _panPointerId = null;
                e.Handled = true;
                StateChanged?.Invoke();
                return;
            }

            if (_activePointerId != e.Pointer.PointerId)
            {
                return;
            }

            CommitActiveStroke();
            e.Handled = true;
        }

        private void OnCanvasPointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (ActiveStroke is not null || _panPointerId is not null)
            {
                return;
            }

            PointerPoint point = e.GetCurrentPoint(_panel);
            int delta = point.Properties.MouseWheelDelta;
            if (delta == 0)
            {
                return;
            }

            // 以鼠标所在位置为锚点缩放，避免缩放时“跳动”
            float factor = (float)Math.Pow(1.1, delta / 120.0);
            _viewport.ZoomAboutScreenPoint(new Vector2((float)point.Position.X, (float)point.Position.Y), factor);
            e.Handled = true;
        }

        private void OnCanvasManipulationStarting(object sender, ManipulationStartingRoutedEventArgs e)
        {
            // 触摸手势以 CanvasPanel 为坐标系
            if (ActiveStroke is not null || _panPointerId is not null)
            {
                e.Handled = true;
                return;
            }

            e.Handled = true;
        }

        private void OnCanvasManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            // 触摸：多指拖动 + 捏合缩放（以手势中心为缩放锚点）
            if (ActiveStroke is not null || _panPointerId is not null)
            {
                e.Handled = true;
                return;
            }

            if (_activeTouchPointers.Count < 2)
            {
                e.Handled = true;
                return;
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
            }

            e.Handled = true;
        }

        private void OnCanvasManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            // 在三指及以上的复杂触摸手势下，系统可能不会为每个触点都完整触发 PointerReleased/PointerCanceled。
            // 为避免触点残留导致始终被判定为“多指”，这里在手势结束时强制清空触摸状态。
            _activeTouchPointers.Clear();
            e.Handled = true;
        }

        private void AppendPoint(Stroke stroke, Pointer pointer, PointerPoint point)
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
                    return;
                }
            }

            stroke.Points.Add(new StrokePoint(pos, pressure));
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
    }
}

