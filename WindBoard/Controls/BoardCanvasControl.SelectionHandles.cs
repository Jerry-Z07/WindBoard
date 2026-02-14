using System;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using WindBoard.Board.Commands;
using WindBoard.Board.Elements;
using WindBoard.Logging;
using Vortice.Mathematics;

namespace WindBoard.Controls
{
    public sealed partial class BoardCanvasControl
    {
        private const float SelectionHandleSizeDip = 14.0f;
        private const float ElementMinWidthDip = 120.0f;
        private const float ElementMinHeightDip = 80.0f;

        private BoardElement? _selectionHandleTargetElement;
        private Vector2 _selectionHandleBeforePositionWorld;
        private Vector2 _selectionHandleBeforeSizeWorld;

        private void OnSelectionHandleDragStarted(object sender, DragStartedEventArgs e)
        {
            if (_input?.SelectedElement is not BoardElement element)
            {
                return;
            }

            // 点击缩放手柄属于“独占交互”：先结束输入控制器可能存在的连续动作，避免残留捕获/状态。
            _input.CancelActiveToolOperation();

            _selectionHandleTargetElement = element;
            _selectionHandleBeforePositionWorld = element.PositionWorld;
            _selectionHandleBeforeSizeWorld = element.SizeWorld;
        }

        private void OnSelectionHandleDragDelta(object sender, DragDeltaEventArgs e)
        {
            if (_selectionHandleTargetElement is not BoardElement element)
            {
                return;
            }

            if (sender is not Thumb t || t.Tag is not string key)
            {
                return;
            }

            float zoom = Math.Max(0.0001f, _viewport.Zoom);
            Vector2 deltaWorld = new((float)e.HorizontalChange / zoom, (float)e.VerticalChange / zoom);

            Vector2 pos = element.PositionWorld;
            Vector2 size = element.SizeWorld;

            float rightEdge = pos.X + size.X;
            float bottomEdge = pos.Y + size.Y;

            switch (key)
            {
                case "TL":
                    pos += deltaWorld;
                    size -= deltaWorld;
                    break;
                case "T":
                    pos.Y += deltaWorld.Y;
                    size.Y -= deltaWorld.Y;
                    break;
                case "TR":
                    pos.Y += deltaWorld.Y;
                    size.Y -= deltaWorld.Y;
                    size.X += deltaWorld.X;
                    break;
                case "L":
                    pos.X += deltaWorld.X;
                    size.X -= deltaWorld.X;
                    break;
                case "R":
                    size.X += deltaWorld.X;
                    break;
                case "BL":
                    pos.X += deltaWorld.X;
                    size.X -= deltaWorld.X;
                    size.Y += deltaWorld.Y;
                    break;
                case "B":
                    size.Y += deltaWorld.Y;
                    break;
                case "BR":
                    size += deltaWorld;
                    break;
                default:
                    return;
            }

            // 最小尺寸以 DIP 定义，再换算到世界坐标，保证不同缩放下手感一致。
            float minWidthWorld = ElementMinWidthDip / zoom;
            float minHeightWorld = ElementMinHeightDip / zoom;

            if (size.X < minWidthWorld)
            {
                size.X = minWidthWorld;

                // 左侧手柄需要保持右边界不动：把 X 推回去。
                if (key is "TL" or "L" or "BL")
                {
                    pos.X = rightEdge - size.X;
                }
            }

            if (size.Y < minHeightWorld)
            {
                size.Y = minHeightWorld;

                // 顶部手柄需要保持下边界不动：把 Y 推回去。
                if (key is "TL" or "T" or "TR")
                {
                    pos.Y = bottomEdge - size.Y;
                }
            }

            element.PositionWorld = pos;
            element.SizeWorld = size;

            // 缩放过程中需要实时更新：选择框 + Dock + 画布渲染。
            UpdateSelectionOverlay();
            RequestRender();
        }

        private void OnSelectionHandleDragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (_selectionHandleTargetElement is not BoardElement element)
            {
                return;
            }

            try
            {
                if (e.Canceled)
                {
                    element.PositionWorld = _selectionHandleBeforePositionWorld;
                    element.SizeWorld = _selectionHandleBeforeSizeWorld;
                    UpdateSelectionOverlay();
                    RequestRender();
                    return;
                }

                Vector2 afterPos = element.PositionWorld;
                Vector2 afterSize = element.SizeWorld;

                if (afterPos == _selectionHandleBeforePositionWorld && afterSize == _selectionHandleBeforeSizeWorld)
                {
                    return;
                }

                // 记录为一次 Undo 命令：拖拽过程中只更新内存态，结束时一次性写入撤销栈。
                _session.Execute(new UpdateElementTransformCommand(
                    element,
                    beforePositionWorld: _selectionHandleBeforePositionWorld,
                    afterPositionWorld: afterPos,
                    beforeSizeWorld: _selectionHandleBeforeSizeWorld,
                    afterSizeWorld: afterSize));
            }
            catch (Exception ex)
            {
                // 防御：缩放手柄不应导致崩溃；出现异常时回退到拖拽前的状态并记录日志。
                AppLog.Error("Selection", "缩放手柄提交失败，已回退到拖拽前状态。", ex);
                element.PositionWorld = _selectionHandleBeforePositionWorld;
                element.SizeWorld = _selectionHandleBeforeSizeWorld;
            }
            finally
            {
                _selectionHandleTargetElement = null;
                UpdateSelectionOverlay();
                RequestRender();
            }
        }

        private void ShowSelectionHandlesOverlay(Rect boundsDip)
        {
            if (SelectionHandlesCanvas is null)
            {
                return;
            }

            float left = boundsDip.Left;
            float top = boundsDip.Top;
            float right = boundsDip.Right;
            float bottom = boundsDip.Bottom;

            float cx = (left + right) / 2.0f;
            float cy = (top + bottom) / 2.0f;

            float half = SelectionHandleSizeDip / 2.0f;

            SetThumbVisibleAndPosition(SelectionHandleTL, left - half, top - half);
            SetThumbVisibleAndPosition(SelectionHandleT, cx - half, top - half);
            SetThumbVisibleAndPosition(SelectionHandleTR, right - half, top - half);
            SetThumbVisibleAndPosition(SelectionHandleL, left - half, cy - half);
            SetThumbVisibleAndPosition(SelectionHandleR, right - half, cy - half);
            SetThumbVisibleAndPosition(SelectionHandleBL, left - half, bottom - half);
            SetThumbVisibleAndPosition(SelectionHandleB, cx - half, bottom - half);
            SetThumbVisibleAndPosition(SelectionHandleBR, right - half, bottom - half);
        }

        private void HideSelectionHandlesOverlay()
        {
            SetThumbHidden(SelectionHandleTL);
            SetThumbHidden(SelectionHandleT);
            SetThumbHidden(SelectionHandleTR);
            SetThumbHidden(SelectionHandleL);
            SetThumbHidden(SelectionHandleR);
            SetThumbHidden(SelectionHandleBL);
            SetThumbHidden(SelectionHandleB);
            SetThumbHidden(SelectionHandleBR);
        }

        private static void SetThumbVisibleAndPosition(Thumb? thumb, float left, float top)
        {
            if (thumb is null)
            {
                return;
            }

            thumb.Visibility = Visibility.Visible;
            Canvas.SetLeft(thumb, left);
            Canvas.SetTop(thumb, top);
        }

        private static void SetThumbHidden(Thumb? thumb)
        {
            if (thumb is null)
            {
                return;
            }

            thumb.Visibility = Visibility.Collapsed;
        }
    }
}
