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
    /// 输入控制器：滚轮缩放与触摸手势（Manipulation）相关代码。
    /// </summary>
    internal sealed partial class BoardInputController
    {
        private void OnCanvasPointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (!_allowViewportManipulation)
            {
                return;
            }

            if (HasBlockingInteractionForWheelZoom())
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

            // 选择模式下，按住修饰键对“选中笔迹集合”做变换（将多笔迹视为整体）：
            // - Ctrl + 滚轮：缩放（以鼠标位置为锚点）
            // - Shift + 滚轮：旋转（以选中集合中心为锚点）
            // 形状跳过矩阵变换（design D）：纯形状选择时不进入该分支，滚轮透传为视口缩放。
            if (Tool == BoardTool.Select
                && _selectTool.HasTransformableStrokes
                && (mods.HasFlag(Windows.System.VirtualKeyModifiers.Control) || mods.HasFlag(Windows.System.VirtualKeyModifiers.Shift)))
            {
                BeginWheelZoomInteraction();

                _selectTool.BeginSelectionTransformSnapshotForSelectedItems();

                if (mods.HasFlag(Windows.System.VirtualKeyModifiers.Control))
                {
                    // 以鼠标所在位置为锚点缩放，避免缩放时“跳动”。
                    float factor = (float)Math.Pow(1.1, delta / 120.0);
                    Vector2 anchorScreen = new((float)point.Position.X, (float)point.Position.Y);
                    Vector2 anchorWorld = _viewport.ScreenToWorld(anchorScreen);
                    Matrix3x2 transform = Matrix3x2.CreateTranslation(-anchorWorld)
                        * Matrix3x2.CreateScale(factor)
                        * Matrix3x2.CreateTranslation(anchorWorld);
                    _selectTool.ApplyMatrixTransformToSelectedStrokes(transform);
                }

                if (mods.HasFlag(Windows.System.VirtualKeyModifiers.Shift))
                {
                    // 以选中集合中心为锚点旋转（避免滚轮旋转时锚点漂移）。
                    float stepDeg = 5.0f;
                    float rotationRad = stepDeg * (delta / 120.0f) * (float)(Math.PI / 180.0);
                    Vector2 centerWorld = _selectTool.GetSelectedItemsCenterWorld();
                    _selectTool.ApplyMatrixTransformToSelectedStrokes(Matrix3x2.CreateRotation(rotationRad, centerWorld));
                }

                e.Handled = true;
                FrameInvalidated?.Invoke();
                return;
            }

            BeginWheelZoomInteraction();

            // 以鼠标所在位置为锚点缩放，避免缩放时“跳动”
            // 选中元素：Ctrl + 滚轮缩放（与笔迹一致的交互方式）。
            if (Tool == BoardTool.Select
                && _selectTool.SelectedElement is BoardElement element
                && mods.HasFlag(Windows.System.VirtualKeyModifiers.Control))
            {
                _selectTool.EnsureElementTransformSnapshot(element);

                // 以鼠标所在位置为锚点缩放，避免缩放时“跳动”。
                float factor = (float)Math.Pow(1.1, delta / 120.0);
                Vector2 anchorScreen = new((float)point.Position.X, (float)point.Position.Y);
                Vector2 anchorWorld = _viewport.ScreenToWorld(anchorScreen);

                Vector2 beforePos = element.PositionWorld;
                Vector2 beforeSize = element.SizeWorld;

                Vector2 afterSize = beforeSize * factor;
                afterSize = new Vector2(Math.Max(0.01f, afterSize.X), Math.Max(0.01f, afterSize.Y));

                Vector2 afterPos = anchorWorld + (beforePos - anchorWorld) * factor;

                element.PositionWorld = afterPos;
                element.SizeWorld = afterSize;
                _selectTool.MarkSelectionModified();

                e.Handled = true;
                FrameInvalidated?.Invoke();
                return;
            }

            float factor2 = (float)Math.Pow(1.1, delta / 120.0);
            _viewport.ZoomAboutScreenPoint(new Vector2((float)point.Position.X, (float)point.Position.Y), factor2);
            e.Handled = true;
            FrameInvalidated?.Invoke();
        }

        private bool HasBlockingInteractionForWheelZoom()
        {
            // 滚轮缩放属于“瞬时交互”，当同时存在其它连续交互（例如画线/擦除/平移/选择变换）时直接忽略，
            // 避免状态互相干扰或导致撤销快照不一致。
            return HasActiveToolInteraction || HasPointerGesture || _routes.IsManipulatingSelection;
        }

        private void BeginWheelZoomInteraction()
        {
            _lastWheelZoomAt = DateTimeOffset.UtcNow;

            if (_wheelZoomTimer is null)
            {
                _wheelZoomTimer = _panel.DispatcherQueue.CreateTimer();
                _wheelZoomTimer.Interval = TimeSpan.FromMilliseconds(PointerRoutingDecisions.WheelZoomTimerIntervalMs);
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
            // tick 决策（空闲合并/结束判定）收敛到 PointerRoutingDecisions.EvaluateWheelZoomTick。
            switch (PointerRoutingDecisions.EvaluateWheelZoomTick(_isWheelZooming, DateTimeOffset.UtcNow, _lastWheelZoomAt))
            {
                case WheelZoomTickDecision.StopIdle:
                    sender.Stop();
                    return;
                case WheelZoomTickDecision.Wait:
                    return;
                case WheelZoomTickDecision.End:
                    break;
            }

            _isWheelZooming = false;
            sender.Stop();

            // Wheel 交互结束时，如果期间对选中笔迹做了变换，则在此一次性写入撤销记录。
            if (_selectTool.HasPendingSelectionChanges)
            {
                CommitSelectionGesture(releasePointerCaptures: false);
                return;
            }

            UpdateInteractionState();
        }

        private void OnCanvasManipulationStarting(object sender, ManipulationStartingRoutedEventArgs e)
        {
            if (!_allowViewportManipulation)
            {
                e.Handled = true;
                return;
            }

            // 触摸手势以 CanvasPanel 为坐标系
            if (HasBlockingInteractionForManipulation())
            {
                e.Handled = true;
                return;
            }

            // 默认：双指/多指才进入手势模式（选择工具也不使用单指平移，避免与后续“框选”冲突）。
            const int minTouchCount = 2;
            if (Tool == BoardTool.Select
                && _routes.TouchManipulationTarget == TouchManipulationTarget.Selection
                && (_selectTool.SelectedItems.Count > 0 || _selectTool.SelectedElement is not null))
            {
                _routes.IsManipulating = false;
                _routes.IsManipulatingSelection = _routes.ActiveTouchPointers.Count >= minTouchCount;
                if (_routes.IsManipulatingSelection)
                {
                    if (_selectTool.SelectedItems.Count > 0)
                    {
                        _selectTool.BeginSelectionTransformSnapshotForSelectedItems();
                    }
                    else if (_selectTool.SelectedElement is BoardElement element)
                    {
                        _selectTool.EnsureElementTransformSnapshot(element);
                    }
                }
            }
            else
            {
                _routes.TouchManipulationTarget = TouchManipulationTarget.Viewport;
                _routes.IsManipulatingSelection = false;
                _routes.IsManipulating = _routes.ActiveTouchPointers.Count >= minTouchCount;
            }

            UpdateInteractionState();
            FrameInvalidated?.Invoke();
            e.Handled = true;
        }

        private void OnCanvasManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            if (!_allowViewportManipulation)
            {
                e.Handled = true;
                return;
            }

            // 触摸：多指拖动 + 捏合缩放（以手势中心为缩放锚点）
            const int minTouchCount = 2;
            bool canHandle = !HasBlockingInteractionForManipulation() && _routes.ActiveTouchPointers.Count >= minTouchCount;
            if (canHandle)
            {
                if (!TryHandleSelectionManipulationDelta(e))
                {
                    HandleViewportManipulationDelta(e);
                }

                FrameInvalidated?.Invoke();
            }

            e.Handled = true;
        }

        private bool TryHandleSelectionManipulationDelta(ManipulationDeltaRoutedEventArgs e)
        {
            if (Tool != BoardTool.Select
                || _routes.TouchManipulationTarget != TouchManipulationTarget.Selection
                || (_selectTool.SelectedItems.Count == 0 && _selectTool.SelectedElement is null))
            {
                return false;
            }

            if (!_routes.IsManipulatingSelection)
            {
                _routes.IsManipulatingSelection = true;

                if (_selectTool.SelectedItems.Count > 0)
                {
                    _selectTool.BeginSelectionTransformSnapshotForSelectedItems();
                }
                else if (_selectTool.SelectedElement is BoardElement element)
                {
                    _selectTool.EnsureElementTransformSnapshot(element);
                }

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

            if (_selectTool.SelectedElement is BoardElement selectedElement)
            {
                // 元素：支持平移 + 缩放（暂不支持旋转）。
                if (!hasScale && !hasTranslation)
                {
                    // 仍然吞掉事件，避免把旋转手势误判为视口操作。
                    return true;
                }

                if (hasScale)
                {
                    Vector2 beforePos = selectedElement.PositionWorld;
                    Vector2 beforeSize = selectedElement.SizeWorld;

                    Vector2 afterSize = beforeSize * scale;
                    afterSize = new Vector2(Math.Max(0.01f, afterSize.X), Math.Max(0.01f, afterSize.Y));

                    Vector2 afterPos = anchorWorld + (beforePos - anchorWorld) * scale;

                    selectedElement.PositionWorld = afterPos;
                    selectedElement.SizeWorld = afterSize;
                    _selectTool.MarkSelectionModified();
                }

                if (hasTranslation)
                {
                    selectedElement.PositionWorld += translationWorld;
                    _selectTool.MarkSelectionModified();
                }

                return true;
            }

            if (_selectTool.SelectedItems.Count == 0)
            {
                return false;
            }

            if (!hasScale && !hasRotation && !hasTranslation)
            {
                return true;
            }

            // 形状跳过矩阵变换（design D）：纯形状选择时，缩放/旋转手势透传视口，
            // 避免手势被吞掉后视口缩放失效；平移分量仍由选择集处理（形状支持平移）。
            if (!_selectTool.HasTransformableStrokes && (hasScale || hasRotation))
            {
                if (!hasTranslation)
                {
                    return false;
                }

                _selectTool.TranslateSelectedItems(translationWorld);
                return true;
            }

            if (hasScale || hasRotation)
            {
                // 注意：这里的增量（Delta）是“逐帧增量”，因此直接对当前点集做增量变换即可。
                // 混合选择时矩阵变换仅作用于笔迹，形状跳过（design D 已知边界）。
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

                _selectTool.ApplyMatrixTransformToSelectedStrokes(transform);
                return true;
            }

            // 仅平移：走更轻量的 Translate，避免构造矩阵（形状与笔迹都生效）。
            _selectTool.TranslateSelectedItems(translationWorld);
            return true;
        }

        private void HandleViewportManipulationDelta(ManipulationDeltaRoutedEventArgs e)
        {
            if (!_routes.IsManipulating)
            {
                _routes.IsManipulating = true;
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
        }

        private bool HasBlockingInteractionForManipulation()
        {
            // Manipulation 属于触摸手势通道，与“指针捕获 + 工具动作”互斥：
            // - 正在画线/擦除时不进入手势
            // - 正在鼠标右键平移/框选/移动选中时不进入手势
            return HasActiveToolInteraction || HasPointerGesture;
        }

        private void OnCanvasManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            if (!_allowViewportManipulation)
            {
                _routes.ActiveTouchPointers.Clear();
                _routes.TouchManipulationTarget = TouchManipulationTarget.Viewport;
                e.Handled = true;
                return;
            }

            // 在三指及以上的复杂触摸手势下，系统可能不会为每个触点都完整触发 PointerReleased/PointerCanceled。
            // 为避免触点残留导致始终被判定为“多指”，这里在手势结束时强制清空触摸状态。
            _routes.ActiveTouchPointers.Clear();
            _routes.TouchManipulationTarget = TouchManipulationTarget.Viewport;

            if (_routes.IsManipulatingSelection)
            {
                _routes.IsManipulating = false;
                CommitSelectionGesture(releasePointerCaptures: false);
                e.Handled = true;
                return;
            }

            _routes.IsManipulating = false;
            NotifyInteractionUiChanged(notifyStateChanged: false);
            e.Handled = true;
        }

    }
}
