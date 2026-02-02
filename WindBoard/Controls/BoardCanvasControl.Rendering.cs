using System;
using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Vortice.Mathematics;
using UiColor = Windows.UI.Color;
using WindBoard.Board;
using WindBoard.Board.Commands;
using WindBoard.Board.Editing;
using WindBoard.Board.Viewport;
using WindBoard.Interaction;
using WindBoard.Rendering;
using WindBoard.Rendering.Board;

namespace WindBoard.Controls
{
    /// <summary>
    /// 画布控件：渲染循环与覆盖层更新相关代码。
    /// </summary>
    public sealed partial class BoardCanvasControl
    {
        private void RequestRender()
        {
            if (!_isInitialized || _renderer is null)
            {
                return;
            }

            if (_isRenderingLoopActive)
            {
                return;
            }

            if (_isRenderQueued)
            {
                return;
            }

            _isRenderQueued = true;
            bool enqueued = DispatcherQueue.TryEnqueue(() =>
            {
                _isRenderQueued = false;

                if (!_isInitialized || _renderer is null || _isRenderingLoopActive)
                {
                    return;
                }

                RenderFrame();
            });

            if (!enqueued)
            {
                _isRenderQueued = false;

                if (!_isInitialized || _renderer is null || _isRenderingLoopActive)
                {
                    return;
                }

                RenderFrame();
            }
        }

        private void RenderFrame()
        {
            if (!_isInitialized || _renderer is null)
            {
                return;
            }

            UpdateSelectionOverlay();

            Stroke? activeStroke = _input?.ActiveStroke;
            if (activeStroke is not null)
            {
                if (_input?.TryConsumeStrokeDirtyRect(out Rect dirtyRectDip) == true)
                {
                    _renderer.RenderWithCachedBackgroundDirtyRect(
                        dirtyRectDip,
                        drawBackground: ctx => _sceneRenderer.DrawBackground(ctx, _session.Document, _viewport),
                        drawOverlay: ctx => _sceneRenderer.DrawActiveStroke(ctx, activeStroke, _viewport));
                }
                else
                {
                    _renderer.RenderWithCachedBackground(
                        drawBackground: ctx => _sceneRenderer.DrawBackground(ctx, _session.Document, _viewport),
                        drawOverlay: ctx => _sceneRenderer.DrawActiveStroke(ctx, activeStroke, _viewport));
                }

                _lastRenderedZoom = _viewport.Zoom;
                return;
            }

            Vector2 panDeltaDip = _input?.ConsumePanScreenDelta() ?? Vector2.Zero;
            if (panDeltaDip.LengthSquared() > 0.0001f
                && !float.IsNaN(_lastRenderedZoom)
                && Math.Abs(_viewport.Zoom - _lastRenderedZoom) < 0.000001f)
            {
                bool presented = _renderer.TryRenderWithScroll(
                    panDeltaDip,
                    (ctx, dirtyDip) => _sceneRenderer.DrawBackgroundInScreenRect(ctx, _session.Document, _viewport, dirtyDip));

                if (presented)
                {
                    _lastRenderedZoom = _viewport.Zoom;
                    return;
                }
            }

            if (_isRenderingLoopActive
                && panDeltaDip.LengthSquared() <= 0.0001f
                && !float.IsNaN(_lastRenderedZoom)
                && Math.Abs(_viewport.Zoom - _lastRenderedZoom) < 0.000001f)
            {
                return;
            }

            _renderer.Render(ctx => _sceneRenderer.Draw(ctx, _session.Document, null, _viewport));
            _lastRenderedZoom = _viewport.Zoom;
        }

        private void UpdateSelectionOverlay()
        {
            if (!_isInitialized || _input is null)
            {
                return;
            }

            // 仅在“选择工具”下展示选择框与悬浮 Dock，避免干扰书写/擦除。
            if (_tool == BoardTool.Select)
            {
                // 正在框选时：展示框选矩形，隐藏 Dock。
                if (_input.TryGetSelectionMarqueeRectDip(out Rect marqueeRectDip))
                {
                    ShowMarqueeSelectionOverlay(marqueeRectDip);
                }
                else if (TryGetSelectedStrokeScreenRect(out Stroke stroke, out Rect strokeBoundsScreenDip))
                {
                    ShowSelectedStrokeOverlay(stroke, strokeBoundsScreenDip);
                }
                else
                {
                    HideSelectionOverlay();
                }
            }
            else
            {
                HideSelectionOverlay();
            }
        }

        private void ShowMarqueeSelectionOverlay(Rect marqueeRectDip)
        {
            if (SelectionBoundsBorder is not null)
            {
                SelectionBoundsBorder.Visibility = Visibility.Visible;
                SelectionBoundsBorder.Width = Math.Max(0.0, marqueeRectDip.Width);
                SelectionBoundsBorder.Height = Math.Max(0.0, marqueeRectDip.Height);
                Canvas.SetLeft(SelectionBoundsBorder, marqueeRectDip.Left);
                Canvas.SetTop(SelectionBoundsBorder, marqueeRectDip.Top);
            }

            if (SelectionDockBorder is not null)
            {
                SelectionDockBorder.Visibility = Visibility.Collapsed;
            }
        }

        private bool TryGetSelectedStrokeScreenRect(out Stroke stroke, out Rect strokeBoundsScreenDip)
        {
            stroke = null!;
            strokeBoundsScreenDip = default;

            if (_input?.SelectedStroke is not Stroke selected || selected.Points.Count == 0)
            {
                return false;
            }

            stroke = selected;

            // 某些情况下笔迹可能还未计算 Bounds（例如外部构造/导入），此时这里补算一次。
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

            float left = Math.Min(minScreen.X, maxScreen.X);
            float top = Math.Min(minScreen.Y, maxScreen.Y);
            float right = Math.Max(minScreen.X, maxScreen.X);
            float bottom = Math.Max(minScreen.Y, maxScreen.Y);

            strokeBoundsScreenDip = Rect.FromLTRB(left, top, right, bottom);
            return true;
        }

        private void ShowSelectedStrokeOverlay(Stroke stroke, Rect strokeBoundsScreenDip)
        {
            ShowSelectionBoundsOverlay(strokeBoundsScreenDip);
            ShowSelectionDockOverlay(stroke, strokeBoundsScreenDip);
        }

        private void ShowSelectionBoundsOverlay(Rect boundsDip)
        {
            if (SelectionBoundsBorder is null)
            {
                return;
            }

            SelectionBoundsBorder.Visibility = Visibility.Visible;
            SelectionBoundsBorder.Width = Math.Max(0.0, boundsDip.Width);
            SelectionBoundsBorder.Height = Math.Max(0.0, boundsDip.Height);
            Canvas.SetLeft(SelectionBoundsBorder, boundsDip.Left);
            Canvas.SetTop(SelectionBoundsBorder, boundsDip.Top);
        }

        private void ShowSelectionDockOverlay(Stroke stroke, Rect boundsDip)
        {
            if (SelectionDockBorder is null)
            {
                return;
            }

            SelectionDockBorder.Visibility = Visibility.Visible;

            // 置顶：当选中笔迹已经是最后绘制（列表末尾）时禁用。
            if (SelectionBringToFrontButton is not null)
            {
                bool isTopMost = _session.Document.Strokes.Count > 0
                    && ReferenceEquals(_session.Document.Strokes[^1], stroke);
                SelectionBringToFrontButton.IsEnabled = !isTopMost;
            }

            // 将 Dock 放在选择框下方居中，并做边界钳制，避免跑出画布。
            double dockW = SelectionDockBorder.ActualWidth;
            double dockH = SelectionDockBorder.ActualHeight;
            if (dockW <= 0.0 || dockH <= 0.0)
            {
                SelectionDockBorder.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                dockW = SelectionDockBorder.DesiredSize.Width;
                dockH = SelectionDockBorder.DesiredSize.Height;
            }

            double boundsLeft = boundsDip.Left;
            double boundsWidth = Math.Max(0.0, boundsDip.Width);
            double boundsBottom = boundsDip.Bottom;

            double dockLeft = boundsLeft + boundsWidth / 2.0 - dockW / 2.0;
            double dockTop = boundsBottom + 8.0;

            double maxLeft = Math.Max(0.0, CanvasPanel.ActualWidth - dockW);
            double maxTop = Math.Max(0.0, CanvasPanel.ActualHeight - dockH);

            dockLeft = Math.Clamp(dockLeft, 0.0, maxLeft);
            dockTop = Math.Clamp(dockTop, 0.0, maxTop);

            Canvas.SetLeft(SelectionDockBorder, dockLeft);
            Canvas.SetTop(SelectionDockBorder, dockTop);
        }

        private void HideSelectionOverlay()
        {
            if (SelectionBoundsBorder is not null)
            {
                SelectionBoundsBorder.Visibility = Visibility.Collapsed;
            }

            if (SelectionDockBorder is not null)
            {
                SelectionDockBorder.Visibility = Visibility.Collapsed;
            }
        }

        private void OnSelectionBringToFrontClicked(object sender, RoutedEventArgs e)
        {
            if (_input?.SelectedStroke is not Stroke stroke)
            {
                return;
            }

            // 点击 Dock 时，主动结束输入控制器的连续动作，避免残留捕获/状态。
            _input.CancelActiveToolOperation();
            _session.Execute(new BringStrokeToFrontCommand(stroke));
            _input.ValidateSelection();
            UpdateSelectionOverlay();
        }

        private void OnSelectionDuplicateClicked(object sender, RoutedEventArgs e)
        {
            if (_input?.SelectedStroke is not Stroke stroke)
            {
                return;
            }

            _input.CancelActiveToolOperation();

            Stroke copy = CloneStroke(stroke);

            // 复制后做一个轻微偏移，避免与原笔迹完全重叠导致“看不见”。
            float zoom = Math.Max(0.0001f, _viewport.Zoom);
            copy.Translate(new Vector2(12.0f, 12.0f) / zoom);

            _session.Execute(new AddStrokeCommand(copy));
            _input.SetSelection(copy);
            UpdateSelectionOverlay();
        }

        private void OnSelectionDeleteClicked(object sender, RoutedEventArgs e)
        {
            if (_input?.SelectedStroke is not Stroke stroke)
            {
                return;
            }

            _input.CancelActiveToolOperation();
            _session.Execute(new RemoveStrokeCommand(stroke));
            _input.ClearSelection();
            UpdateSelectionOverlay();
        }

        private static Stroke CloneStroke(Stroke source)
        {
            var clone = new Stroke
            {
                Color = source.Color,
                BaseSize = source.BaseSize,
                EnablePressure = source.EnablePressure,
            };

            clone.Points.AddRange(source.Points);
            clone.RecalculateBoundsFromPoints();
            return clone;
        }

        private void UpdateWritingCacheState()
        {
            bool isWriting = _input?.ActiveStroke is not null;
            if (_wasWriting == isWriting)
            {
                return;
            }

            _wasWriting = isWriting;

            if (_renderer is null)
            {
                return;
            }

            if (isWriting)
            {
                _renderer.InvalidateCachedBackground();
                return;
            }

            _renderer.ReleaseCachedBackground();
        }

    }
}
