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

            if (_selectionStrokeBeforeSnapshots is not null && _selectedStrokes.Count > 0)
            {
                Vector2 deltaWorld = deltaScreen / Math.Max(0.0001f, _viewport.Zoom);
                for (int i = 0; i < _selectedStrokes.Count; i++)
                {
                    _selectedStrokes[i].Translate(deltaWorld);
                }
            }
            else if (_selectionTransformElement is not null)
            {
                Vector2 deltaWorld = deltaScreen / Math.Max(0.0001f, _viewport.Zoom);
                _selectionTransformElement.PositionWorld += deltaWorld;
            }

            if (deltaScreen.LengthSquared() > 0.0001f)
            {
                _selectionModified = true;
            }

            e.Handled = true;
            FrameInvalidated?.Invoke();
        }

        private void HandleMarqueePointerMoved(PointerRoutedEventArgs e)
        {
            PointerPoint point = e.GetCurrentPoint(_panel);
            _marqueeCurrentScreen = new Vector2((float)point.Position.X, (float)point.Position.Y);
            e.Handled = true;
            FrameInvalidated?.Invoke();
        }

        private void HandleActivePointerMoved(PointerRoutedEventArgs e)
        {
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

            PointerPoint point = e.GetCurrentPoint(_panel);
            if (AppendPoint(ActiveStroke, e.Pointer, point))
            {
                FrameInvalidated?.Invoke();
            }

            e.Handled = true;
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
                    bool shouldHandleElementClick = !_selectionModified && _selectedElement is not null;
                    BoardElement? clickedElement = _selectedElement;

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

            if (_isErasing)
            {
                if (mode == PointerEndMode.Commit)
                {
                    CommitEraserGesture();
                }
                else
                {
                    CancelEraserGesture();
                }

                e.Handled = true;
                return;
            }

            if (mode == PointerEndMode.Commit)
            {
                CommitActiveStroke();
            }
            else
            {
                DiscardActiveStroke();
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
