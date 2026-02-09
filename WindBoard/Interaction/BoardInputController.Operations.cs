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

            BoardElement? selectedElement = null;
            bool isClick = rectDip.Width <= MarqueeClickThresholdDip && rectDip.Height <= MarqueeClickThresholdDip;

            // 小于阈值时按“点击”处理，避免用户轻微抖动导致无法点选。
            if (isClick)
            {
                HitTestSelectableAtScreenPoint(start, out Stroke? selectedStroke, out selectedElement);

                if (selectedStroke is not null)
                {
                    SetSelectedStroke(selectedStroke);
                }
                else
                {
                    SetSelectedElement(selectedElement);
                }
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

                List<Stroke> selectedStrokes = HitTestSelectableStrokesInWorldRect(minWorld, maxWorld);
                SetSelectedStrokes(selectedStrokes.Count > 0 ? selectedStrokes : null);
            }

            // 元素双击：外部打开（仅在“点击”路径触发，框选不会触发）。
            if (isClick && selectedElement is not null)
            {
                HandleElementClickForMaybeOpen(selectedElement, start);
            }

            UpdateInteractionState();
            FrameInvalidated?.Invoke();
            StateChanged?.Invoke();
        }

        private void HitTestSelectableAtScreenPoint(Vector2 screenDip, out Stroke? stroke, out BoardElement? element)
        {
            stroke = null;
            element = null;

            Vector2 pointWorld = _viewport.ScreenToWorld(screenDip);
            float toleranceWorld = SelectHitToleranceDip / Math.Max(0.0001f, _viewport.Zoom);

            // 选择优先级（视觉顺序）：上层元素 → 笔迹 → 下层元素
            element = ElementPickTest.HitTestTopMostElement(_session.Document.ElementsAboveInk, pointWorld, toleranceWorld);
            if (element is not null)
            {
                return;
            }

            stroke = StrokePickTest.HitTestTopMostStroke(_session.Document.Strokes, pointWorld, toleranceWorld);
            if (stroke is not null)
            {
                return;
            }

            element = ElementPickTest.HitTestTopMostElement(_session.Document.ElementsBelowInk, pointWorld, toleranceWorld);
        }

        private List<Stroke> HitTestSelectableStrokesInWorldRect(Vector2 minWorld, Vector2 maxWorld)
        {
            // 交互约定：元素只能通过“单击”选中，不支持框选。
            // 框选仅用于笔迹，避免导入的图片/文本/链接等元素被误选。
            return StrokeRectSelectTest.HitTestStrokesInWorldRect(_session.Document.Strokes, minWorld, maxWorld);
        }

        private void HitTestSelectableInWorldRect(Vector2 minWorld, Vector2 maxWorld, out Stroke? stroke, out BoardElement? element)
        {
            // 交互约定：元素只能通过“单击”选中，不支持框选。
            // 框选仅用于笔迹，避免导入的图片/文本/链接等元素被误选。
            element = null;
            stroke = StrokeRectSelectTest.HitTestTopMostStrokeInWorldRect(_session.Document.Strokes, minWorld, maxWorld);
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
            if (stroke is null)
            {
                throw new ArgumentNullException(nameof(stroke));
            }

            BeginSelectionTransformSnapshot(new[] { stroke });
        }

        private void BeginSelectionTransformSnapshot(IReadOnlyList<Stroke> strokes)
        {
            if (strokes is null)
            {
                throw new ArgumentNullException(nameof(strokes));
            }

            // 选择变换快照规则：
            // - 一次连续交互（拖拽/触摸/滚轮）只创建一次“Before”快照；
            // - 快照按当前选择集合顺序记录，提交时按相同顺序生成撤销命令；
            // - 仅支持“笔迹集合”或“单元素”二选一，避免产生混合撤销记录。
            if (IsSameSelectionStrokeSnapshot(strokes))
            {
                return;
            }

            var snapshots = new List<StrokeTransformSnapshot>(strokes.Count);
            for (int i = 0; i < strokes.Count; i++)
            {
                Stroke stroke = strokes[i];
                if (stroke is null)
                {
                    continue;
                }

                snapshots.Add(new StrokeTransformSnapshot(stroke));
            }

            _selectionStrokeBeforeSnapshots = snapshots.Count > 0 ? snapshots : null;
            _selectionTransformElement = null;
            _selectionElementBeforePositionWorld = null;
            _selectionElementBeforeSizeWorld = null;
            _selectionModified = false;
        }

        private bool IsSameSelectionStrokeSnapshot(IReadOnlyList<Stroke> strokes)
        {
            if (_selectionStrokeBeforeSnapshots is not { Count: > 0 } snapshots)
            {
                return false;
            }

            if (strokes.Count != snapshots.Count)
            {
                return false;
            }

            for (int i = 0; i < snapshots.Count; i++)
            {
                if (!ReferenceEquals(strokes[i], snapshots[i].Stroke))
                {
                    return false;
                }
            }

            return true;
        }

        private void BeginSelectionTransformSnapshot(BoardElement element)
        {
            _selectionStrokeBeforeSnapshots = null;

            _selectionTransformElement = element;
            _selectionElementBeforePositionWorld = element.PositionWorld;
            _selectionElementBeforeSizeWorld = element.SizeWorld;
            _selectionModified = false;
        }

        private void CommitSelectionGesture(bool releasePointerCaptures)
        {
            List<StrokeTransformSnapshot>? strokeSnapshots = _selectionStrokeBeforeSnapshots;
            BoardElement? element = _selectionTransformElement;
            Vector2? elementBeforePos = _selectionElementBeforePositionWorld;
            Vector2? elementBeforeSize = _selectionElementBeforeSizeWorld;

            _selectionPointerId = null;
            _isManipulatingSelection = false;

            if (releasePointerCaptures)
            {
                _panel.ReleasePointerCaptures();
            }

            _selectionStrokeBeforeSnapshots = null;
            _selectionTransformElement = null;
            _selectionElementBeforePositionWorld = null;
            _selectionElementBeforeSizeWorld = null;

            // 统一把“多笔迹变换/元素变换”合并成一次撤销记录，保证用户一次操作对应一次 Ctrl+Z。
            List<IBoardCommand>? commands = null;

            if (_selectionModified && strokeSnapshots is { Count: > 0 })
            {
                for (int i = 0; i < strokeSnapshots.Count; i++)
                {
                    StrokeTransformSnapshot snap = strokeSnapshots[i];
                    var after = new List<StrokePoint>(snap.Stroke.Points);
                    if (IsSameStrokePointList(snap.BeforePoints, after))
                    {
                        continue;
                    }

                    commands ??= new List<IBoardCommand>();
                    commands.Add(new UpdateStrokePointsCommand(snap.Stroke, snap.BeforePoints, after));
                }
            }

            if (element is not null
                && elementBeforePos is Vector2 beforePos
                && elementBeforeSize is Vector2 beforeSize
                && _selectionModified)
            {
                Vector2 afterPos = element.PositionWorld;
                Vector2 afterSize = element.SizeWorld;

                bool moved = Vector2.DistanceSquared(beforePos, afterPos) > 0.000001f;
                bool resized = Vector2.DistanceSquared(beforeSize, afterSize) > 0.000001f;
                if (moved || resized)
                {
                    commands ??= new List<IBoardCommand>();
                    commands.Add(new UpdateElementTransformCommand(
                        element,
                        beforePos,
                        afterPos,
                        beforeSizeWorld: beforeSize,
                        afterSizeWorld: afterSize));
                }
            }

            if (commands is { Count: > 0 })
            {
                _session.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand(commands));
            }

            _selectionModified = false;
            UpdateInteractionState();
            FrameInvalidated?.Invoke();
            StateChanged?.Invoke();
        }

        private void CancelSelectionGesture(bool releasePointerCaptures = true)
        {
            if (_selectionStrokeBeforeSnapshots is { Count: > 0 } strokeSnapshots)
            {
                for (int i = 0; i < strokeSnapshots.Count; i++)
                {
                    StrokeTransformSnapshot snap = strokeSnapshots[i];
                    RestoreStrokePoints(snap.Stroke, snap.BeforePoints);
                }
            }

            if (_selectionTransformElement is not null && _selectionElementBeforePositionWorld is Vector2 beforePos)
            {
                _selectionTransformElement.PositionWorld = beforePos;
            }

            if (_selectionTransformElement is not null && _selectionElementBeforeSizeWorld is Vector2 beforeSize)
            {
                _selectionTransformElement.SizeWorld = beforeSize;
            }

            _selectionPointerId = null;
            _isManipulatingSelection = false;
            _selectionStrokeBeforeSnapshots = null;
            _selectionTransformElement = null;
            _selectionElementBeforePositionWorld = null;
            _selectionElementBeforeSizeWorld = null;
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
