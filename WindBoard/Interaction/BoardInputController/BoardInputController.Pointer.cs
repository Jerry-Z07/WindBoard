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
using WindBoard.Board.Items;
using WindBoard.Board.Viewport;
using WindBoard.Interaction.Tools;
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
            _routes.ActiveTouchPointers.Add(e.Pointer.PointerId);
            UpdateInteractionState();

            // 路由决策收敛到 PointerRoutingDecisions.ResolveTouchPressRoute（分支顺序保持一致）。
            switch (PointerRoutingDecisions.ResolveTouchPressRoute(
                _routes.ActiveTouchPointers.Count,
                Tool == BoardTool.Select,
                _allowSelectionInteractions,
                _routes.HasActivePointerCapture))
            {
                case TouchPressRoute.MultiFinger:
                    // 多指触摸：交给 Manipulation 处理缩放/拖动；如果正在用“触摸单指画线/擦除”，则先结束。
                    if (_allowViewportManipulation)
                    {
                        EndTouchSingleFingerToolOperationForManipulation();
                    }

                    e.Handled = true;
                    FrameInvalidated?.Invoke();
                    return;
                case TouchPressRoute.SelectGesture:
                {
                    // 选择模式：单指用于“框选”；双指/多指用于视口手势或对已选中笔迹做变换。
                    Vector2 screen = new((float)point.Position.X, (float)point.Position.Y);
                    _routes.TouchManipulationTarget = _selectTool.IsScreenPointInsideSelectedBounds(screen)
                        ? TouchManipulationTarget.Selection
                        : TouchManipulationTarget.Viewport;
                    BeginMarqueeSelectionGesture(e.Pointer, screen);
                    e.Handled = true;
                    return;
                }
                case TouchPressRoute.Ignore:
                    // 已有会话/手势：忽略本次按下。
                    return;
                default:
                    // 单指触摸：画线 / 擦除
                    BeginPenOrEraserGesture(e.Pointer, point);
                    e.Handled = true;
                    StateChanged?.Invoke();
                    return;
            }
        }

        private void EndTouchSingleFingerToolOperationForManipulation()
        {
            // 打断处置决策收敛到 PointerRoutingDecisions.ResolveTouchGestureEnd（分支顺序保持一致）。
            switch (PointerRoutingDecisions.ResolveTouchGestureEnd(
                isStrokeActiveItem: ActiveItem is Stroke,
                strokePointCount: ActiveItem is Stroke stroke ? stroke.Points.Count : 0,
                hasOtherActiveItem: ActiveItem is not null && ActiveItem is not Stroke,
                isErasing: IsErasing,
                isTouchOrigin: _routes.ActiveStrokeDeviceType == PointerDeviceType.Touch))
            {
                case TouchGestureEndAction.DiscardDotStroke:
                case TouchGestureEndAction.DiscardShape:
                    DiscardActiveToolGesture();
                    return;
                case TouchGestureEndAction.CommitStroke:
                    CommitActiveToolGesture();
                    return;
                case TouchGestureEndAction.CommitEraser:
                    // 与原实现一致：擦除提交后不提前返回，继续落到下方的框选取消检查。
                    CommitActiveToolGesture();
                    break;
            }

            // 选择框选：当用户从单指切换为双指/多指时，取消框选，交给 Manipulation 处理缩放/拖动。
            if (_routes.MarqueePointerId is not null)
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

        private bool HasActivePointerCapture => _routes.HasActivePointerCapture;

        private bool TryBeginSelectGesture(PointerRoutedEventArgs e, PointerPoint point)
        {
            if (!_allowSelectionInteractions)
            {
                return false;
            }

            // 选择模式（框选）：
            // - 鼠标右键：平移视口
            // - 其它：单指/鼠标左键/触控笔拖拽 → 框选；在已选中笔迹范围内拖拽 → 移动选中笔迹
            if (PointerRoutingDecisions.ShouldStartPan(_allowViewportManipulation, e.Pointer.PointerDeviceType, point.Properties.IsRightButtonPressed))
            {
                BeginPanGesture(e.Pointer, point);
                return true;
            }

            if (!PointerRoutingDecisions.ShouldStartStroke(e.Pointer.PointerDeviceType, point.Properties.IsLeftButtonPressed))
            {
                return false;
            }

            Vector2 screen = new((float)point.Position.X, (float)point.Position.Y);
            if (_selectTool.IsScreenPointInsideSelectedBounds(screen))
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

            if (PointerRoutingDecisions.ShouldStartPan(_allowViewportManipulation, e.Pointer.PointerDeviceType, point.Properties.IsRightButtonPressed))
            {
                BeginPanGesture(e.Pointer, point);
                return true;
            }

            if (!PointerRoutingDecisions.ShouldStartStroke(e.Pointer.PointerDeviceType, point.Properties.IsLeftButtonPressed))
            {
                return false;
            }

            // BeginPenOrEraserGesture 内部不会触发 StateChanged，这里保持与现有交互一致。
            BeginPenOrEraserGesture(e.Pointer, point);
            notifyStateChanged = true;
            return true;
        }

        private void BeginPenOrEraserGesture(Pointer pointer, PointerPoint point)
        {
            CaptureStrokePointer(pointer);

            // 工具策略化调度：已注册工具（画笔/橡皮）走策略对象。
            // 既有回退行为：Select 工具在触摸单指下（例如“禁用选择”场景）按画笔处理，保持零回归。
            if (_toolRegistry.TryGetTool(ResolveActiveToolId(), out IBoardTool? tool))
            {
                tool.Begin(CreateToolInput(pointer, point));
                UpdateInteractionState();
            }
        }

        private void CaptureStrokePointer(Pointer pointer)
        {
            _panel.CapturePointer(pointer);
            _routes.BeginActiveStroke(pointer.PointerId, pointer.PointerDeviceType);
            _context.ClearStrokeDirtyRect();
        }

        private void BeginPanGesture(Pointer pointer, PointerPoint point)
        {
            _panel.CapturePointer(pointer);
            _routes.BeginPan(pointer.PointerId);
            _lastPanScreen = new Vector2((float)point.Position.X, (float)point.Position.Y);
            NotifyInteractionUiChanged();
        }

        private void BeginSelectionMoveGesture(Pointer pointer, Vector2 screenDip)
        {
            IReadOnlyList<IBoardInkItem> selectedItems = _selectTool.SelectedItems;
            bool hasSelection = selectedItems.Count > 0 || _selectTool.SelectedElement is not null;
            if (!hasSelection)
            {
                return;
            }

            _panel.CapturePointer(pointer);
            _routes.BeginSelectionMove(pointer.PointerId);
            _lastSelectionScreen = screenDip;

            _selectTool.BeginSelectionMove();

            NotifyInteractionUiChanged();
        }

    }
}
