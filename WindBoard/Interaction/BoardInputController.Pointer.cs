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
    /// 输入控制器：指针（鼠标/触控笔/触摸）事件处理相关代码。
    /// </summary>
    internal sealed partial class BoardInputController
    {
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

            bool notifyStateChanged = false;
            bool handled = Tool == BoardTool.Select
                ? TryBeginSelectGesture(e, point)
                : TryBeginNonSelectGesture(e, point, out notifyStateChanged);

            if (!handled)
            {
                return;
            }

            e.Handled = true;
            if (notifyStateChanged)
            {
                StateChanged?.Invoke();
            }
        }

        private bool HasActivePointerCapture => _activePointerId is not null || _panPointerId is not null || _selectionPointerId is not null || _marqueePointerId is not null;

        private bool TryBeginSelectGesture(PointerRoutedEventArgs e, PointerPoint point)
        {
            // 选择模式（框选）：
            // - 鼠标右键：平移视口
            // - 其它：单指/鼠标左键/触控笔拖拽 → 框选；在已选中笔迹范围内拖拽 → 移动选中笔迹
            if (ShouldStartPan(e.Pointer, point))
            {
                BeginPanGesture(e.Pointer, point);
                return true;
            }

            if (!ShouldStartStroke(e.Pointer, point))
            {
                return false;
            }

            Vector2 screen = new((float)point.Position.X, (float)point.Position.Y);
            if (IsScreenPointInsideSelectedStrokeBounds(screen))
            {
                BeginSelectionMoveGesture(e.Pointer, screen);
            }
            else
            {
                BeginMarqueeSelectionGesture(e.Pointer, screen);
            }

            return true;
        }

        private bool TryBeginNonSelectGesture(PointerRoutedEventArgs e, PointerPoint point, out bool notifyStateChanged)
        {
            notifyStateChanged = false;

            if (ShouldStartPan(e.Pointer, point))
            {
                BeginPanGesture(e.Pointer, point);
                return true;
            }

            if (!ShouldStartStroke(e.Pointer, point))
            {
                return false;
            }

            // BeginStrokeOrEraserGesture 内部不会触发 StateChanged，这里保持与现有交互一致。
            BeginStrokeOrEraserGesture(e.Pointer, point);
            notifyStateChanged = true;
            return true;
        }

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

    }
}
