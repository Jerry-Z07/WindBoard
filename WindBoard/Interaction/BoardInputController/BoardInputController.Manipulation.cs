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
            if (Tool == BoardTool.Select
                && _selectedStrokes.Count > 0
                && (mods.HasFlag(Windows.System.VirtualKeyModifiers.Control) || mods.HasFlag(Windows.System.VirtualKeyModifiers.Shift)))
            {
                BeginWheelZoomInteraction();

                BeginSelectionTransformSnapshot(_selectedStrokes);

                if (mods.HasFlag(Windows.System.VirtualKeyModifiers.Control))
                {
                    // 以鼠标所在位置为锚点缩放，避免缩放时“跳动”。
                    float factor = (float)Math.Pow(1.1, delta / 120.0);
                    Vector2 anchorScreen = new((float)point.Position.X, (float)point.Position.Y);
                    Vector2 anchorWorld = _viewport.ScreenToWorld(anchorScreen);
                    Matrix3x2 transform = Matrix3x2.CreateTranslation(-anchorWorld)
                        * Matrix3x2.CreateScale(factor)
                        * Matrix3x2.CreateTranslation(anchorWorld);
                    ApplyTransformToSelectedStrokes(transform);
                    _selectionModified = true;
                }

                if (mods.HasFlag(Windows.System.VirtualKeyModifiers.Shift))
                {
                    // 以选中集合中心为锚点旋转（避免滚轮旋转时锚点漂移）。
                    float stepDeg = 5.0f;
                    float rotationRad = stepDeg * (delta / 120.0f) * (float)(Math.PI / 180.0);
                    Vector2 centerWorld = GetSelectedStrokesCenterWorld();
                    ApplyTransformToSelectedStrokes(Matrix3x2.CreateRotation(rotationRad, centerWorld));
                    _selectionModified = true;
                }

                e.Handled = true;
                FrameInvalidated?.Invoke();
                return;
            }

            BeginWheelZoomInteraction();

            // 以鼠标所在位置为锚点缩放，避免缩放时“跳动”
            // 选中元素：Ctrl + 滚轮缩放（与笔迹一致的交互方式）。
            if (Tool == BoardTool.Select
                && _selectedElement is BoardElement element
                && mods.HasFlag(Windows.System.VirtualKeyModifiers.Control))
            {
                if (_selectionElementBeforePositionWorld is null || !ReferenceEquals(_selectionTransformElement, element))
                {
                    BeginSelectionTransformSnapshot(element);
                }

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
                _selectionModified = true;

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
            bool hasActiveTool = ActiveStroke is not null || _isErasing;
            bool hasPointerGesture = _panPointerId is not null || _selectionPointerId is not null || _marqueePointerId is not null;
            bool hasSelectionManipulation = _isManipulatingSelection;
            return hasActiveTool || hasPointerGesture || hasSelectionManipulation;
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
            if (_selectionModified
                && ((_selectionStrokeBeforeSnapshots is { Count: > 0 })
                    || (_selectionTransformElement is not null
                        && _selectionElementBeforePositionWorld is not null
                        && _selectionElementBeforeSizeWorld is not null)))
            {
                CommitSelectionGesture(releasePointerCaptures: false);
                return;
            }

            UpdateInteractionState();
        }

        private Vector2 GetSelectedStrokesCenterWorld()
        {
            // 单笔迹：沿用既有中心逻辑（Bounds 优先，否则点集平均）。
            if (_selectedStrokes.Count == 1)
            {
                return GetStrokeCenterWorld(_selectedStrokes[0]);
            }

            // 多笔迹：以“包围盒中心”为整体中心，更符合用户对“作为整体旋转”的直觉。
            Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
            bool hasAny = false;

            for (int i = 0; i < _selectedStrokes.Count; i++)
            {
                Stroke stroke = _selectedStrokes[i];
                if (stroke.Points.Count == 0)
                {
                    continue;
                }

                if (!stroke.HasBounds)
                {
                    stroke.RecalculateBoundsFromPoints();
                }

                if (!stroke.HasBounds)
                {
                    continue;
                }

                min = new Vector2(
                    Math.Min(min.X, stroke.BoundsMin.X),
                    Math.Min(min.Y, stroke.BoundsMin.Y));
                max = new Vector2(
                    Math.Max(max.X, stroke.BoundsMax.X),
                    Math.Max(max.Y, stroke.BoundsMax.Y));
                hasAny = true;
            }

            return hasAny ? (min + max) / 2.0f : Vector2.Zero;
        }

        private void ApplyTransformToSelectedStrokes(Matrix3x2 transform)
        {
            for (int i = 0; i < _selectedStrokes.Count; i++)
            {
                _selectedStrokes[i].Transform(transform);
            }
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
                && _touchManipulationTarget == TouchManipulationTarget.Selection
                && (_selectedStrokes.Count > 0 || _selectedElement is not null))
            {
                _isManipulating = false;
                _isManipulatingSelection = _activeTouchPointers.Count >= minTouchCount;
                if (_isManipulatingSelection)
                {
                    if (_selectedStrokes.Count > 0)
                    {
                        BeginSelectionTransformSnapshot(_selectedStrokes);
                    }
                    else if (_selectedElement is BoardElement element)
                    {
                        BeginSelectionTransformSnapshot(element);
                    }
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
            if (!_allowViewportManipulation)
            {
                e.Handled = true;
                return;
            }

            // 触摸：多指拖动 + 捏合缩放（以手势中心为缩放锚点）
            const int minTouchCount = 2;
            bool canHandle = !HasBlockingInteractionForManipulation() && _activeTouchPointers.Count >= minTouchCount;
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
                || _touchManipulationTarget != TouchManipulationTarget.Selection
                || (_selectedStrokes.Count == 0 && _selectedElement is null))
            {
                return false;
            }

            if (!_isManipulatingSelection)
            {
                _isManipulatingSelection = true;

                if (_selectedStrokes.Count > 0)
                {
                    BeginSelectionTransformSnapshot(_selectedStrokes);
                }
                else if (_selectedElement is BoardElement element)
                {
                    BeginSelectionTransformSnapshot(element);
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

            if (_selectedElement is BoardElement selectedElement)
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
                    _selectionModified = true;
                }

                if (hasTranslation)
                {
                    selectedElement.PositionWorld += translationWorld;
                    _selectionModified = true;
                }

                return true;
            }

            if (_selectedStrokes.Count == 0)
            {
                return false;
            }

            if (!hasScale && !hasRotation && !hasTranslation)
            {
                return true;
            }

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

                ApplyTransformToSelectedStrokes(transform);
                _selectionModified = true;
                return true;
            }

            // 仅平移：走更轻量的 Translate，避免构造矩阵。
            for (int i = 0; i < _selectedStrokes.Count; i++)
            {
                _selectedStrokes[i].Translate(translationWorld);
            }
            _selectionModified = true;
            return true;
        }

        private void HandleViewportManipulationDelta(ManipulationDeltaRoutedEventArgs e)
        {
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
        }

        private bool HasBlockingInteractionForManipulation()
        {
            // Manipulation 属于触摸手势通道，与“指针捕获 + 工具动作”互斥：
            // - 正在画线/擦除时不进入手势
            // - 正在鼠标右键平移/框选/移动选中时不进入手势
            bool hasActiveTool = ActiveStroke is not null || _isErasing;
            bool hasPointerGesture = _panPointerId is not null || _selectionPointerId is not null || _marqueePointerId is not null;
            return hasActiveTool || hasPointerGesture;
        }

        private void OnCanvasManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            if (!_allowViewportManipulation)
            {
                _activeTouchPointers.Clear();
                _touchManipulationTarget = TouchManipulationTarget.Viewport;
                e.Handled = true;
                return;
            }

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

    }
}
