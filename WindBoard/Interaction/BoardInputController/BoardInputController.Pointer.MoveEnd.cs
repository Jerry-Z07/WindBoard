using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WindBoard.Board;
using WindBoard.Board.Elements;
using WindBoard.Interaction.Tools;
using Vortice.Mathematics;

namespace WindBoard.Interaction
{
    /// <summary>
    /// 输入控制器：指针移动与结束（Moved/Released/Canceled/CaptureLost）相关代码。
    /// </summary>
    internal sealed partial class BoardInputController
    {
        private void OnCanvasPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            uint pointerId = e.Pointer.PointerId;

            if (_panPointerId == pointerId)
            {
                HandlePanPointerMoved(e);
            }
            else if (_selectionPointerId == pointerId)
            {
                HandleSelectionPointerMoved(e);
            }
            else if (_marqueePointerId == pointerId)
            {
                HandleMarqueePointerMoved(e);
            }
            else if (_activePointerId == pointerId)
            {
                HandleActivePointerMoved(e);
            }
        }

        private void HandlePanPointerMoved(PointerRoutedEventArgs e)
        {
            PointerPoint point = e.GetCurrentPoint(_panel);
            Vector2 current = new((float)point.Position.X, (float)point.Position.Y);
            Vector2 delta = current - _lastPanScreen;
            _lastPanScreen = current;
            _viewport.PanByScreenDelta(delta);
            _pendingPanScreenDelta += delta;
            e.Handled = true;
            FrameInvalidated?.Invoke();
        }

        private void HandleSelectionPointerMoved(PointerRoutedEventArgs e)
        {
            PointerPoint point = e.GetCurrentPoint(_panel);
            Vector2 current = new((float)point.Position.X, (float)point.Position.Y);
            Vector2 deltaScreen = current - _lastSelectionScreen;
            _lastSelectionScreen = current;

            _selectTool.MoveSelectionByScreenDelta(deltaScreen);

            e.Handled = true;
            FrameInvalidated?.Invoke();
        }

        private void HandleMarqueePointerMoved(PointerRoutedEventArgs e)
        {
            PointerPoint point = e.GetCurrentPoint(_panel);
            // 框选几何状态由 SelectTool 内聚，这里只做输入转发。
            Vector2 screen = new((float)point.Position.X, (float)point.Position.Y);
            _selectTool.Move(new ToolInput(screen, 1.0f, e.Pointer.PointerDeviceType, _context));
            e.Handled = true;
            FrameInvalidated?.Invoke();
        }

        private void HandleActivePointerMoved(PointerRoutedEventArgs e)
        {
            // 工具策略化调度：会话由 Begin 建立（_activePointerId 匹配保证配对），
            // 画笔/橡皮/形状统一路由到各自策略对象。
            // 与按下路径统一经 ResolveActiveToolId 解析（Select 回退画笔语义一致）。
            // e.Handled 语义与原版对齐：橡皮会话始终消费事件；绘制类会话（画笔/形状）
            // 在无活动预览条目时早退且不置 Handled（原 ActiveStroke == null 分支行为，
            // 泛化为 PreviewItem 以同时覆盖形状预览）。
            if (_toolRegistry.TryGetTool(ResolveActiveToolId(), out IBoardTool? tool))
            {
                if (!_eraserTool.IsErasing && _context.PreviewItem is null)
                {
                    return;
                }

                PointerPoint point = e.GetCurrentPoint(_panel);
                tool.Move(CreateToolInput(e.Pointer, point));
                e.Handled = true;
            }
        }

        private void OnCanvasPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            HandlePointerEnded(e, PointerEndMode.Commit, releasePointerCaptures: true);
        }

        private void OnCanvasPointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            HandlePointerEnded(e, PointerEndMode.Cancel, releasePointerCaptures: true);
        }

        private void OnCanvasPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            // 捕获丢失时尽量按“释放”处理并提交，避免出现“已经移动/擦除了，但撤销栈没有记录”的不一致。
            HandlePointerEnded(e, PointerEndMode.Commit, releasePointerCaptures: false);
        }

        private enum PointerEndMode
        {
            Commit,
            Cancel,
        }

        private void HandlePointerEnded(PointerRoutedEventArgs e, PointerEndMode mode, bool releasePointerCaptures)
        {
            // 指针结束（释放/取消/捕获丢失）在结构上高度相似：
            // 1) 先处理触摸触点集合
            // 2) 再处理平移/框选/移动选中
            // 3) 最后处理画线/擦除
            // 统一入口可以减少重复代码，也更容易保证三种结束路径的状态清理一致。
            HandleTouchPointerEnded(e);

            if (TryHandlePanPointerEnded(e, releasePointerCaptures))
            {
                return;
            }

            uint pointerId = e.Pointer.PointerId;

            if (_marqueePointerId == pointerId)
            {
                if (mode == PointerEndMode.Commit)
                {
                    CommitMarqueeSelectionGesture(releasePointerCaptures);
                }
                else
                {
                    CancelMarqueeSelectionGesture(releasePointerCaptures);
                }

                e.Handled = true;
                return;
            }

            if (_selectionPointerId == pointerId)
            {
                if (mode == PointerEndMode.Commit)
                {
                    PointerPoint point = e.GetCurrentPoint(_panel);
                    Vector2 screenDip = new((float)point.Position.X, (float)point.Position.Y);

                    // 选择拖拽未发生任何变换时，将其视为一次“点击”用于双击外部打开。
                    bool shouldHandleElementClick = !_selectTool.SelectionModified && _selectTool.SelectedElement is not null;
                    BoardElement? clickedElement = _selectTool.SelectedElement;

                    CommitSelectionGesture(releasePointerCaptures);

                    if (shouldHandleElementClick && clickedElement is not null)
                    {
                        HandleElementClickForMaybeOpen(clickedElement, screenDip);
                    }
                }
                else
                {
                    CancelSelectionGesture(releasePointerCaptures);
                }

                e.Handled = true;
                return;
            }

            if (_activePointerId != pointerId)
            {
                return;
            }

            // 工具策略化调度：画笔/橡皮会话的提交/取消经策略对象完成。
            if (mode == PointerEndMode.Commit)
            {
                CommitActiveToolGesture();
            }
            else
            {
                DiscardActiveToolGesture();
            }

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

            e.Handled = true;
            FinalizeGestureState(releasePointerCaptures);
            return true;
        }

    }
}
