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
    /// <summary>
    /// 输入控制器：工具操作（笔迹/擦除/选择）与状态更新相关代码。
    /// </summary>
    internal sealed partial class BoardInputController
    {
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
