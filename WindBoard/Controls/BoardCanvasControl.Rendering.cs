using System;
using System.Collections.Generic;
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
using WindBoard.Board.Elements;
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
                        drawBackground: ctx => _sceneRenderer.DrawBackgroundUnderInk(ctx, _session.Document, _viewport),
                        drawOverlay: ctx => _sceneRenderer.DrawOverlayAboveInk(ctx, _session.Document, activeStroke, _viewport));
                }
                else
                {
                    _renderer.RenderWithCachedBackground(
                        drawBackground: ctx => _sceneRenderer.DrawBackgroundUnderInk(ctx, _session.Document, _viewport),
                        drawOverlay: ctx => _sceneRenderer.DrawOverlayAboveInk(ctx, _session.Document, activeStroke, _viewport));
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
                 else if (TryGetSelectedStrokesScreenRect(out Rect strokeBoundsScreenDip))
                 {
                     ShowSelectedStrokesOverlay(strokeBoundsScreenDip);
                 }
                 else if (TryGetSelectedElementScreenRect(out BoardElement element, out Rect elementBoundsScreenDip))
                 {
                     ShowSelectedElementOverlay(element, elementBoundsScreenDip);
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

         private bool TryGetSelectedStrokesScreenRect(out Rect strokeBoundsScreenDip)
         {
             strokeBoundsScreenDip = default;

             if (_input is null)
             {
                 return false;
             }

             IReadOnlyList<Stroke> selectedStrokes = _input.SelectedStrokes;
             if (selectedStrokes.Count == 0)
             {
                 return false;
             }

             Matrix3x2 worldToScreen = _viewport.GetWorldToScreenTransform();

             float left = float.PositiveInfinity;
             float top = float.PositiveInfinity;
             float right = float.NegativeInfinity;
             float bottom = float.NegativeInfinity;

             bool hasAny = false;
             for (int i = 0; i < selectedStrokes.Count; i++)
             {
                 Stroke stroke = selectedStrokes[i];
                 if (stroke.Points.Count == 0)
                 {
                     continue;
                 }

                 // 某些情况下笔迹可能还未计算 Bounds（例如外部构造/导入），此时这里补算一次。
                 if (!stroke.HasBounds)
                 {
                     stroke.RecalculateBoundsFromPoints();
                 }

                 if (!stroke.HasBounds)
                 {
                     continue;
                 }

                 Vector2 minScreen = Vector2.Transform(stroke.BoundsMin, worldToScreen);
                 Vector2 maxScreen = Vector2.Transform(stroke.BoundsMax, worldToScreen);

                 float l = Math.Min(minScreen.X, maxScreen.X);
                 float t = Math.Min(minScreen.Y, maxScreen.Y);
                 float r = Math.Max(minScreen.X, maxScreen.X);
                 float b = Math.Max(minScreen.Y, maxScreen.Y);

                 left = Math.Min(left, l);
                 top = Math.Min(top, t);
                 right = Math.Max(right, r);
                 bottom = Math.Max(bottom, b);
                 hasAny = true;
             }

             if (!hasAny)
             {
                 return false;
             }

             strokeBoundsScreenDip = Rect.FromLTRB(left, top, right, bottom);
             return true;
         }

         private void ShowSelectedStrokesOverlay(Rect strokeBoundsScreenDip)
         {
             ShowSelectionBoundsOverlay(strokeBoundsScreenDip);
             ShowSelectionDockOverlay(strokeBoundsScreenDip);
         }

        private void ShowSelectedElementOverlay(BoardElement element, Rect elementBoundsScreenDip)
        {
            ShowSelectionBoundsOverlay(elementBoundsScreenDip);
            ShowSelectionDockOverlay(elementBoundsScreenDip);
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

        private void ShowSelectionDockOverlay(Rect boundsDip)
        {
            if (SelectionDockBorder is null)
            {
                return;
            }

            SelectionDockBorder.Visibility = Visibility.Visible;

             // 置顶：
              // - 笔迹：当选中笔迹已在列表末尾（最后绘制）时禁用。
              // - 元素：当选中元素已在“上层元素列表”末尾时禁用；下层元素永远可置顶（跨层）。
             if (SelectionBringToFrontButton is not null)
             {
                 bool isTopMost = false;

                 if (_input?.SelectedStrokes is IReadOnlyList<Stroke> selectedStrokes && selectedStrokes.Count > 0)
                 {
                     isTopMost = AreSelectedStrokesTopMost(selectedStrokes);
                 }
                 else if (_input?.SelectedElement is BoardElement selectedElement)
                 {
                     isTopMost = _session.Document.ElementsAboveInk.Count > 0
                         && ReferenceEquals(_session.Document.ElementsAboveInk[^1], selectedElement);
                }

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

         private bool AreSelectedStrokesTopMost(IReadOnlyList<Stroke> selectedStrokes)
         {
             if (selectedStrokes is null || selectedStrokes.Count == 0)
             {
                 return false;
             }

             int total = _session.Document.Strokes.Count;
             if (total <= 0 || selectedStrokes.Count > total)
             {
                 return false;
             }

             // 当且仅当“选中集合”恰好是笔迹列表的末尾一段（suffix）时，才认为已经置顶到位。
             int start = total - selectedStrokes.Count;
             for (int i = 0; i < selectedStrokes.Count; i++)
             {
                 if (!ReferenceEquals(_session.Document.Strokes[start + i], selectedStrokes[i]))
                 {
                     return false;
                 }
             }

             return true;
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
            if (_input is null)
            {
                return;
            }

             // 点击 Dock 时，主动结束输入控制器的连续动作，避免残留捕获/状态。
             _input.CancelActiveToolOperation();

             IReadOnlyList<Stroke>? selectedStrokes = _input.SelectedStrokes;
             if (selectedStrokes is { Count: > 0 })
             {
                 if (AreSelectedStrokesTopMost(selectedStrokes))
                 {
                     return;
                 }

                 var commands = new List<IBoardCommand>(selectedStrokes.Count);
                 for (int i = 0; i < selectedStrokes.Count; i++)
                 {
                     commands.Add(new BringStrokeToFrontCommand(selectedStrokes[i]));
                 }

                 _session.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand(commands));
             }
             else if (_input?.SelectedElement is BoardElement element)
             {
                 _session.Execute(new BringElementToFrontCommand(element));
             }
            else
            {
                return;
            }

            _input.ValidateSelection();
            UpdateSelectionOverlay();
        }

        private void OnSelectionDuplicateClicked(object sender, RoutedEventArgs e)
        {
            if (_input is null)
            {
                return;
            }

             _input.CancelActiveToolOperation();

             IReadOnlyList<Stroke>? selectedStrokes = _input.SelectedStrokes;
             if (selectedStrokes is { Count: > 0 })
             {
                 float zoom = Math.Max(0.0001f, _viewport.Zoom);
                 Vector2 deltaWorld = new Vector2(12.0f, 12.0f) / zoom;

                 var copies = new List<Stroke>(selectedStrokes.Count);
                 for (int i = 0; i < selectedStrokes.Count; i++)
                 {
                     Stroke copy = CloneStroke(selectedStrokes[i]);
                     // 复制后整体做一个轻微偏移，避免与原笔迹完全重叠导致“看不见”。
                     copy.Translate(deltaWorld);
                     copies.Add(copy);
                 }

                 var commands = new List<IBoardCommand>(copies.Count);
                 for (int i = 0; i < copies.Count; i++)
                 {
                     commands.Add(new AddStrokeCommand(copies[i]));
                 }

                 _session.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand(commands));
                 _input.SetSelectionStrokes(copies);
                 UpdateSelectionOverlay();
                 return;
             }

             if (_input?.SelectedElement is BoardElement element)
            {
                if (!TryCloneElement(element, out BoardElement? copy, out bool aboveInk))
                {
                    return;
                }

                float zoom = Math.Max(0.0001f, _viewport.Zoom);
                copy!.PositionWorld = element.PositionWorld + new Vector2(12.0f, 12.0f) / zoom;

                _session.Execute(new AddElementCommand(copy, aboveInk));
                _input.SetSelection(copy);
                UpdateSelectionOverlay();
            }
        }

        private void OnSelectionDeleteClicked(object sender, RoutedEventArgs e)
        {
            if (_input is null)
            {
                return;
            }

             _input.CancelActiveToolOperation();

             IReadOnlyList<Stroke>? selectedStrokes = _input.SelectedStrokes;
             if (selectedStrokes is { Count: > 0 })
             {
                 // 删除多个笔迹：从后往前删除，可减少索引移动对记录的影响。
                 var commands = new List<IBoardCommand>(selectedStrokes.Count);
                 for (int i = selectedStrokes.Count - 1; i >= 0; i--)
                 {
                     commands.Add(new RemoveStrokeCommand(selectedStrokes[i]));
                 }

                 _session.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand(commands));
                 _input.ClearSelection();
                 UpdateSelectionOverlay();
                 return;
             }

            if (_input?.SelectedElement is BoardElement element)
            {
                _session.Execute(new RemoveElementCommand(element));
                _input.ClearSelection();
                UpdateSelectionOverlay();
            }
        }

        private bool TryCloneElement(BoardElement source, out BoardElement? clone, out bool aboveInk)
        {
            clone = null;
            aboveInk = false;

            if (_session.Document.ElementsAboveInk.Contains(source))
            {
                aboveInk = true;
            }
            else if (_session.Document.ElementsBelowInk.Contains(source))
            {
                aboveInk = false;
            }
            else
            {
                return false;
            }

            clone = source switch
            {
                BoardTextElement t => new BoardTextElement { Text = t.Text },
                BoardLinkElement l => new BoardLinkElement { Url = l.Url, Title = l.Title },
                BoardMediaElement m => new BoardMediaElement
                {
                    Kind = m.Kind,
                    SourcePath = m.SourcePath,
                    DisplayName = m.DisplayName,
                    PixelWidth = m.PixelWidth,
                    PixelHeight = m.PixelHeight,
                    Bgra8PremulPixels = m.Bgra8PremulPixels,
                },
                BoardFileElement f => new BoardFileElement
                {
                    SourcePath = f.SourcePath,
                    DisplayName = f.DisplayName,
                },
                _ => null,
            };

            if (clone is null)
            {
                return false;
            }

            clone.PositionWorld = source.PositionWorld;
            clone.SizeWorld = source.SizeWorld;
            return true;
        }

        private bool TryGetSelectedElementScreenRect(out BoardElement element, out Rect elementBoundsScreenDip)
        {
            element = null!;
            elementBoundsScreenDip = default;

            if (_input?.SelectedElement is not BoardElement selected)
            {
                return false;
            }

            Rect boundsWorld = selected.GetBoundsWorld();
            if (boundsWorld.Width <= 0.0001f || boundsWorld.Height <= 0.0001f)
            {
                return false;
            }

            element = selected;

            Matrix3x2 worldToScreen = _viewport.GetWorldToScreenTransform();
            Vector2 minScreen = Vector2.Transform(new Vector2(boundsWorld.Left, boundsWorld.Top), worldToScreen);
            Vector2 maxScreen = Vector2.Transform(new Vector2(boundsWorld.Right, boundsWorld.Bottom), worldToScreen);

            float left = Math.Min(minScreen.X, maxScreen.X);
            float top = Math.Min(minScreen.Y, maxScreen.Y);
            float right = Math.Max(minScreen.X, maxScreen.X);
            float bottom = Math.Max(minScreen.Y, maxScreen.Y);

            elementBoundsScreenDip = Rect.FromLTRB(left, top, right, bottom);
            return true;
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
