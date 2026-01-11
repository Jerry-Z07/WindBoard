using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WindBoard.Models.InkV2;
using WindBoard.Services;
using WindBoard.Services.InkV2;

namespace WindBoard
{
    public partial class MainWindow
    {
        private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e) => ScheduleSelectionDockUpdate();

        private void BuildAttachmentSelectionOverlay()
        {
            if (AttachmentSelectionOverlay == null) return;
            AttachmentSelectionOverlay.Children.Clear();

            _attachmentSelectionFrame = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x5B, 0xA1, 0xFF)),
                BorderThickness = new Thickness(2),
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(8),
                Visibility = Visibility.Collapsed
            };

            var grid = new Grid();
            _attachmentSelectionFrame.Child = grid;

            _attachmentMoveThumb = new Thumb
            {
                Cursor = Cursors.SizeAll,
                Background = Brushes.Transparent
            };
            _attachmentMoveThumb.DragDelta += AttachmentMoveThumb_DragDelta;
            _attachmentMoveThumb.PreviewMouseLeftButtonDown += AttachmentMoveThumb_PreviewMouseLeftButtonDown;
            grid.Children.Add(_attachmentMoveThumb);

            AddResizeThumb(grid, "TL", HorizontalAlignment.Left, VerticalAlignment.Top, Cursors.SizeNWSE);
            AddResizeThumb(grid, "T", HorizontalAlignment.Center, VerticalAlignment.Top, Cursors.SizeNS);
            AddResizeThumb(grid, "TR", HorizontalAlignment.Right, VerticalAlignment.Top, Cursors.SizeNESW);
            AddResizeThumb(grid, "L", HorizontalAlignment.Left, VerticalAlignment.Center, Cursors.SizeWE);
            AddResizeThumb(grid, "R", HorizontalAlignment.Right, VerticalAlignment.Center, Cursors.SizeWE);
            AddResizeThumb(grid, "BL", HorizontalAlignment.Left, VerticalAlignment.Bottom, Cursors.SizeNESW);
            AddResizeThumb(grid, "B", HorizontalAlignment.Center, VerticalAlignment.Bottom, Cursors.SizeNS);
            AddResizeThumb(grid, "BR", HorizontalAlignment.Right, VerticalAlignment.Bottom, Cursors.SizeNWSE);

            AttachmentSelectionOverlay.Children.Add(_attachmentSelectionFrame);
        }

        private void BuildInkSelectionOverlay()
        {
            if (AttachmentSelectionOverlay == null) return;

            _inkSelectionFrame = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x3D)),
                BorderThickness = new Thickness(2),
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(8),
                Visibility = Visibility.Collapsed
            };

            var grid = new Grid();
            _inkSelectionFrame.Child = grid;

            _inkMoveThumb = new Thumb
            {
                Cursor = Cursors.SizeAll,
                Background = Brushes.Transparent
            };
            _inkMoveThumb.DragStarted += InkTransform_DragStarted;
            _inkMoveThumb.DragDelta += InkMoveThumb_DragDelta;
            _inkMoveThumb.DragCompleted += InkTransform_DragCompleted;
            grid.Children.Add(_inkMoveThumb);

            AddInkResizeThumb(grid, "TL", HorizontalAlignment.Left, VerticalAlignment.Top, Cursors.SizeNWSE);
            AddInkResizeThumb(grid, "T", HorizontalAlignment.Center, VerticalAlignment.Top, Cursors.SizeNS);
            AddInkResizeThumb(grid, "TR", HorizontalAlignment.Right, VerticalAlignment.Top, Cursors.SizeNESW);
            AddInkResizeThumb(grid, "L", HorizontalAlignment.Left, VerticalAlignment.Center, Cursors.SizeWE);
            AddInkResizeThumb(grid, "R", HorizontalAlignment.Right, VerticalAlignment.Center, Cursors.SizeWE);
            AddInkResizeThumb(grid, "BL", HorizontalAlignment.Left, VerticalAlignment.Bottom, Cursors.SizeNESW);
            AddInkResizeThumb(grid, "B", HorizontalAlignment.Center, VerticalAlignment.Bottom, Cursors.SizeNS);
            AddInkResizeThumb(grid, "BR", HorizontalAlignment.Right, VerticalAlignment.Bottom, Cursors.SizeNWSE);

            AttachmentSelectionOverlay.Children.Add(_inkSelectionFrame);
        }

        private void BuildInkMarqueeOverlay()
        {
            if (AttachmentSelectionOverlay == null) return;

            _inkMarqueeFrame = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x3D)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromArgb(32, 0xFF, 0xC1, 0x3D)),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };

            AttachmentSelectionOverlay.Children.Add(_inkMarqueeFrame);
        }

        private void AddInkResizeThumb(Grid host, string key, HorizontalAlignment h, VerticalAlignment v, Cursor cursor)
        {
            var thumb = new Thumb
            {
                Width = 14,
                Height = 14,
                HorizontalAlignment = h,
                VerticalAlignment = v,
                Cursor = cursor,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x3D)),
                BorderThickness = new Thickness(2),
                Margin = new Thickness(-7)
            };
            thumb.DragStarted += InkTransform_DragStarted;
            thumb.DragDelta += InkResizeThumb_DragDelta;
            thumb.DragCompleted += InkTransform_DragCompleted;
            thumb.Tag = key;

            _inkResizeThumbs[key] = thumb;
            host.Children.Add(thumb);
        }

        private void SetSelectedInkStrokes(IReadOnlyList<InkStroke> strokes)
        {
            if (strokes == null || strokes.Count == 0)
            {
                ClearSelectedInkStrokes();
                return;
            }

            SelectAttachment(null);

            _selectedInkStrokes.Clear();
            for (int i = 0; i < strokes.Count; i++)
            {
                InkStroke stroke = strokes[i];
                if (stroke != null)
                {
                    _selectedInkStrokes.Add(stroke);
                }
            }

            UpdateInkSelectionOverlay();
            ScheduleSelectionDockUpdate();
            InvalidateInkSurface();
        }

        private void ClearSelectedInkStrokes()
        {
            _selectedInkStrokes.Clear();
            _inkTransformBeforePoints = null;
            _inkTransformInitialBounds = Rect.Empty;
            _inkTransformCurrentBounds = Rect.Empty;

            if (_inkMarqueeFrame != null)
            {
                _inkMarqueeFrame.Visibility = Visibility.Collapsed;
            }

            UpdateInkSelectionOverlay();
            ScheduleSelectionDockUpdate();
        }

        private void SetInkMarqueeRect(Rect? rect)
        {
            if (_inkMarqueeFrame == null) return;

            if (rect == null || rect.Value.IsEmpty)
            {
                _inkMarqueeFrame.Visibility = Visibility.Collapsed;
                return;
            }

            Rect b = rect.Value;
            _inkMarqueeFrame.Visibility = Visibility.Visible;
            Canvas.SetLeft(_inkMarqueeFrame, b.X);
            Canvas.SetTop(_inkMarqueeFrame, b.Y);
            _inkMarqueeFrame.Width = Math.Max(0, b.Width);
            _inkMarqueeFrame.Height = Math.Max(0, b.Height);
        }

        private Rect GetSelectedInkBounds()
        {
            if (_inkTransformBeforePoints != null && !_inkTransformCurrentBounds.IsEmpty)
            {
                return _inkTransformCurrentBounds;
            }

            return TryComputeInkBounds(_selectedInkStrokes, out Rect bounds) ? bounds : Rect.Empty;
        }

        private void UpdateInkSelectionOverlay()
        {
            if (_inkSelectionFrame == null || AttachmentSelectionOverlay == null) return;

            if (!IsSelectModeActive() || _selectedInkStrokes.Count == 0)
            {
                _inkSelectionFrame.Visibility = Visibility.Collapsed;
                return;
            }

            Rect bounds = GetSelectedInkBounds();
            if (bounds.IsEmpty)
            {
                _inkSelectionFrame.Visibility = Visibility.Collapsed;
                return;
            }

            _inkSelectionFrame.Visibility = Visibility.Visible;
            Canvas.SetLeft(_inkSelectionFrame, bounds.X);
            Canvas.SetTop(_inkSelectionFrame, bounds.Y);
            _inkSelectionFrame.Width = Math.Max(0, bounds.Width);
            _inkSelectionFrame.Height = Math.Max(0, bounds.Height);
        }

        private void InkTransform_DragStarted(object sender, DragStartedEventArgs e)
        {
            if (!IsSelectModeActive()) return;
            if (_selectedInkStrokes.Count == 0) return;
            if (_inkTransformBeforePoints != null) return;

            BoardPage? page = _pageService.CurrentPage;
            if (page == null) return;

            if (!TryComputeInkBounds(_selectedInkStrokes, out Rect bounds) || bounds.IsEmpty)
            {
                return;
            }

            _inkTransformBeforePoints = CaptureInkPoints(_selectedInkStrokes);
            _inkTransformInitialBounds = bounds;
            _inkTransformCurrentBounds = bounds;

            page.InkUndoHistory.Begin();
        }

        private void InkTransform_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (!IsSelectModeActive()) return;

            BoardPage? page = _pageService.CurrentPage;
            if (page == null)
            {
                _inkTransformBeforePoints = null;
                return;
            }

            var before = _inkTransformBeforePoints;
            if (before == null)
            {
                return;
            }

            foreach (var kv in before)
            {
                InkFragment fragment = kv.Key;
                InkPoint[] beforePoints = kv.Value;
                InkPoint[] afterPoints = fragment.Points.ToArray();
                if (AreSamePoints(beforePoints, afterPoints))
                {
                    continue;
                }

                page.InkUndoHistory.Record(new ReplaceFragmentPointsCommand(fragment, beforePoints, afterPoints));
            }

            page.InkUndoHistory.End();
            page.InkSpatialIndex.Rebuild(page.Ink);
            page.ContentVersion++;

            _inkTransformBeforePoints = null;
            _inkTransformInitialBounds = Rect.Empty;
            _inkTransformCurrentBounds = Rect.Empty;

            UpdateInkSelectionOverlay();
            ScheduleSelectionDockUpdate();
            InvalidateInkSurface();
        }

        private void InkMoveThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (!IsSelectModeActive()) return;
            if (_selectedInkStrokes.Count == 0) return;

            var page = _pageService.CurrentPage;
            if (page == null) return;

            double dx = e.HorizontalChange;
            double dy = e.VerticalChange;
            if (dx == 0 && dy == 0) return;

            TranslateInkStrokes(_selectedInkStrokes, dx, dy);

            if (_inkTransformBeforePoints != null && !_inkTransformCurrentBounds.IsEmpty)
            {
                _inkTransformCurrentBounds = new Rect(
                    _inkTransformCurrentBounds.X + dx,
                    _inkTransformCurrentBounds.Y + dy,
                    _inkTransformCurrentBounds.Width,
                    _inkTransformCurrentBounds.Height);
            }

            UpdateInkSelectionOverlay();
            ScheduleSelectionDockUpdate();
            InvalidateInkSurface();
        }

        private void InkResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (!IsSelectModeActive()) return;
            if (_selectedInkStrokes.Count == 0) return;
            if (_inkTransformBeforePoints == null) return;
            if (sender is not Thumb t || t.Tag is not string key) return;

            Rect b = _inkTransformCurrentBounds;
            double x = b.X;
            double y = b.Y;
            double w = b.Width;
            double h = b.Height;

            double dx = e.HorizontalChange;
            double dy = e.VerticalChange;

            switch (key)
            {
                case "TL":
                    x += dx; y += dy; w -= dx; h -= dy;
                    break;
                case "T":
                    y += dy; h -= dy;
                    break;
                case "TR":
                    y += dy; w += dx; h -= dy;
                    break;
                case "L":
                    x += dx; w -= dx;
                    break;
                case "R":
                    w += dx;
                    break;
                case "BL":
                    x += dx; w -= dx; h += dy;
                    break;
                case "B":
                    h += dy;
                    break;
                case "BR":
                    w += dx; h += dy;
                    break;
            }

            const double minSize = 10;
            if (w < minSize)
            {
                double diff = minSize - w;
                w = minSize;
                if (key is "TL" or "L" or "BL") x -= diff;
            }
            if (h < minSize)
            {
                double diff = minSize - h;
                h = minSize;
                if (key is "TL" or "T" or "TR") y -= diff;
            }

            var next = new Rect(x, y, w, h);
            _inkTransformCurrentBounds = next;

            ApplyInkScaleFromTransformSnapshot(next);
            UpdateInkSelectionOverlay();
            ScheduleSelectionDockUpdate();
            InvalidateInkSurface();
        }

        private void ApplyInkScaleFromTransformSnapshot(Rect targetBounds)
        {
            var before = _inkTransformBeforePoints;
            if (before == null) return;

            Rect initial = _inkTransformInitialBounds;
            if (initial.IsEmpty) return;

            double denomX = Math.Abs(initial.Width) <= 1e-9 ? 1.0 : initial.Width;
            double denomY = Math.Abs(initial.Height) <= 1e-9 ? 1.0 : initial.Height;

            for (int si = 0; si < _selectedInkStrokes.Count; si++)
            {
                InkStroke stroke = _selectedInkStrokes[si];
                for (int fi = 0; fi < stroke.Fragments.Count; fi++)
                {
                    InkFragment fragment = stroke.Fragments[fi];
                    if (!before.TryGetValue(fragment, out InkPoint[]? originalPoints))
                    {
                        continue;
                    }

                    fragment.Points.Clear();
                    for (int pi = 0; pi < originalPoints.Length; pi++)
                    {
                        InkPoint p = originalPoints[pi];
                        double tx = (p.XDip - initial.X) / denomX;
                        double ty = (p.YDip - initial.Y) / denomY;

                        fragment.Points.Add(
                            new InkPoint(
                                targetBounds.X + tx * targetBounds.Width,
                                targetBounds.Y + ty * targetBounds.Height,
                                p.Pressure,
                                p.TimestampTicks));
                    }
                    fragment.PointsVersion++;
                }
            }
        }

        private static Dictionary<InkFragment, InkPoint[]> CaptureInkPoints(IReadOnlyList<InkStroke> strokes)
        {
            var map = new Dictionary<InkFragment, InkPoint[]>(strokes.Count * 2);
            for (int si = 0; si < strokes.Count; si++)
            {
                InkStroke stroke = strokes[si];
                for (int fi = 0; fi < stroke.Fragments.Count; fi++)
                {
                    InkFragment fragment = stroke.Fragments[fi];
                    map[fragment] = fragment.Points.ToArray();
                }
            }
            return map;
        }

        private static void TranslateInkStrokes(IReadOnlyList<InkStroke> strokes, double dx, double dy)
        {
            for (int si = 0; si < strokes.Count; si++)
            {
                InkStroke stroke = strokes[si];
                for (int fi = 0; fi < stroke.Fragments.Count; fi++)
                {
                    InkFragment fragment = stroke.Fragments[fi];
                    List<InkPoint> points = fragment.Points;
                    for (int pi = 0; pi < points.Count; pi++)
                    {
                        InkPoint p = points[pi];
                        points[pi] = new InkPoint(p.XDip + dx, p.YDip + dy, p.Pressure, p.TimestampTicks);
                    }
                    fragment.PointsVersion++;
                }
            }
        }

        private static bool TryComputeInkBounds(IReadOnlyList<InkStroke> strokes, out Rect bounds)
        {
            bounds = Rect.Empty;
            if (strokes.Count == 0) return false;

            bool any = false;
            double minX = 0;
            double minY = 0;
            double maxX = 0;
            double maxY = 0;

            for (int si = 0; si < strokes.Count; si++)
            {
                InkStroke stroke = strokes[si];
                for (int fi = 0; fi < stroke.Fragments.Count; fi++)
                {
                    InkFragment fragment = stroke.Fragments[fi];
                    List<InkPoint> points = fragment.Points;
                    for (int pi = 0; pi < points.Count; pi++)
                    {
                        InkPoint p = points[pi];
                        if (!any)
                        {
                            any = true;
                            minX = maxX = p.XDip;
                            minY = maxY = p.YDip;
                            continue;
                        }

                        minX = Math.Min(minX, p.XDip);
                        minY = Math.Min(minY, p.YDip);
                        maxX = Math.Max(maxX, p.XDip);
                        maxY = Math.Max(maxY, p.YDip);
                    }
                }
            }

            if (!any)
            {
                return false;
            }

            bounds = new Rect(minX, minY, maxX - minX, maxY - minY);
            return true;
        }

        private static bool AreSamePoints(InkPoint[] a, InkPoint[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (!a[i].Equals(b[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private static InkStroke CloneInkStrokeWithOffset(InkStroke stroke, double dx, double dy)
        {
            var clone = new InkStroke(stroke.Tool);
            for (int fi = 0; fi < stroke.Fragments.Count; fi++)
            {
                InkFragment source = stroke.Fragments[fi];
                var fragment = new InkFragment();
                for (int pi = 0; pi < source.Points.Count; pi++)
                {
                    InkPoint p = source.Points[pi];
                    fragment.Points.Add(new InkPoint(p.XDip + dx, p.YDip + dy, p.Pressure, p.TimestampTicks));
                }
                clone.Fragments.Add(fragment);
            }

            return clone;
        }

        private void AddResizeThumb(Grid host, string key, HorizontalAlignment h, VerticalAlignment v, Cursor cursor)
        {
            var thumb = new Thumb
            {
                Width = 14,
                Height = 14,
                HorizontalAlignment = h,
                VerticalAlignment = v,
                Cursor = cursor,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x5B, 0xA1, 0xFF)),
                BorderThickness = new Thickness(2),
                Margin = new Thickness(-7)
            };
            thumb.DragDelta += AttachmentResizeThumb_DragDelta;
            thumb.PreviewMouseLeftButtonDown += AttachmentMoveThumb_PreviewMouseLeftButtonDown;
            thumb.Tag = key;

            _attachmentResizeThumbs[key] = thumb;
            host.Children.Add(thumb);
        }

        private void AttachmentMoveThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (_selectedAttachment == null) return;
            _selectedAttachment.X += e.HorizontalChange;
            _selectedAttachment.Y += e.VerticalChange;
            UpdateAttachmentSelectionOverlay();
            ScheduleSelectionDockUpdate();
        }

        private void AttachmentMoveThumb_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_selectedAttachment == null) return;
            if (!IsSelectModeActive()) return;

            if (TryOpenAttachmentExternalOnDoubleClick(_selectedAttachment, e.ClickCount))
            {
                e.Handled = true;
            }
        }

        private void AttachmentResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (_selectedAttachment == null) return;
            if (sender is not Thumb t || t.Tag is not string key) return;

            double x = _selectedAttachment.X;
            double y = _selectedAttachment.Y;
            double w = _selectedAttachment.Width;
            double h = _selectedAttachment.Height;

            double dx = e.HorizontalChange;
            double dy = e.VerticalChange;

            switch (key)
            {
                case "TL":
                    x += dx; y += dy; w -= dx; h -= dy;
                    break;
                case "T":
                    y += dy; h -= dy;
                    break;
                case "TR":
                    y += dy; w += dx; h -= dy;
                    break;
                case "L":
                    x += dx; w -= dx;
                    break;
                case "R":
                    w += dx;
                    break;
                case "BL":
                    x += dx; w -= dx; h += dy;
                    break;
                case "B":
                    h += dy;
                    break;
                case "BR":
                    w += dx; h += dy;
                    break;
            }

            if (w < AttachmentMinSize)
            {
                double diff = AttachmentMinSize - w;
                w = AttachmentMinSize;
                if (key is "TL" or "L" or "BL") x -= diff;
            }
            if (h < AttachmentMinSize)
            {
                double diff = AttachmentMinSize - h;
                h = AttachmentMinSize;
                if (key is "TL" or "T" or "TR") y -= diff;
            }

            _selectedAttachment.X = x;
            _selectedAttachment.Y = y;
            _selectedAttachment.Width = w;
            _selectedAttachment.Height = h;

            UpdateAttachmentSelectionOverlay();
            ScheduleSelectionDockUpdate();
        }

        private void UpdateAttachmentSelectionOverlay()
        {
            if (_attachmentSelectionFrame == null || AttachmentSelectionOverlay == null) return;
            if (_selectedAttachment == null)
            {
                _attachmentSelectionFrame.Visibility = Visibility.Collapsed;
                return;
            }

            _attachmentSelectionFrame.Visibility = Visibility.Visible;
            Canvas.SetLeft(_attachmentSelectionFrame, _selectedAttachment.X);
            Canvas.SetTop(_attachmentSelectionFrame, _selectedAttachment.Y);
            _attachmentSelectionFrame.Width = Math.Max(0, _selectedAttachment.Width);
            _attachmentSelectionFrame.Height = Math.Max(0, _selectedAttachment.Height);
        }

        private void SelectAttachment(BoardAttachment? attachment)
        {
            if (_selectedAttachment != null)
            {
                _selectedAttachment.IsSelected = false;
            }

            _selectedAttachment = attachment;

            if (_selectedAttachment != null)
            {
                _selectedAttachment.IsSelected = true;

                // 清空笔迹选择，避免同时出现两套选择语义
                ClearSelectedInkStrokes();
            }

            UpdateAttachmentSelectionOverlay();
            ScheduleSelectionDockUpdate();
        }

        private bool IsSelectModeActive()
        {
            var mode = _modeController?.ActiveMode ?? _modeController?.CurrentMode;
            return ReferenceEquals(mode, _selectMode);
        }

        private bool TryHandleAttachmentSelectModeMouseDown(MouseButtonEventArgs e)
        {
            if (!IsSelectModeActive()) return false;
            if (e.ChangedButton != MouseButton.Left) return false;

            var canvasPoint = e.GetPosition(MyCanvas);
            var hit = HitTestAttachment(canvasPoint);
            if (hit == null)
            {
                // 未命中附件：清除附件选择，交给 SelectMode 处理墨迹选择
                SelectAttachment(null);
                return false;
            }

            SelectAttachment(hit);

            if (TryOpenAttachmentExternalOnDoubleClick(hit, e.ClickCount))
            {
                e.Handled = true;
                return true;
            }

            e.Handled = true;
            return true;
        }

        private BoardAttachment? HitTestAttachment(Point canvasPoint)
        {
            var list = _pageService.CurrentPage?.Attachments;
            if (list == null || list.Count == 0) return null;

            // 置顶层优先，其次 ZIndex 越大越靠上（优先命中）
            return list
                .OrderByDescending(a => a.IsPinnedTop)
                .ThenByDescending(a => a.ZIndex)
                .FirstOrDefault(a =>
                {
                    double w = Math.Max(0, a.Width);
                    double h = Math.Max(0, a.Height);
                    return canvasPoint.X >= a.X && canvasPoint.X <= a.X + w
                        && canvasPoint.Y >= a.Y && canvasPoint.Y <= a.Y + h;
                });
        }

        private void ScheduleSelectionDockUpdate()
        {
            if (_selectionDockUpdateScheduled) return;
            _selectionDockUpdateScheduled = true;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                _selectionDockUpdateScheduled = false;
                UpdateSelectionDock();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void UpdateSelectionDock()
        {
            if (_selectionDock == null || _rootGrid == null || Viewport == null) return;

            Rect? selectionBounds = null;
            if (_selectedAttachment != null)
            {
                selectionBounds = new Rect(_selectedAttachment.X, _selectedAttachment.Y, _selectedAttachment.Width, _selectedAttachment.Height);
            }
            else if (_selectedInkStrokes.Count > 0)
            {
                selectionBounds = GetSelectedInkBounds();
            }

            if (selectionBounds == null || selectionBounds.Value.IsEmpty)
            {
                _selectionDock.Visibility = Visibility.Collapsed;
                return;
            }

            if (!IsSelectModeActive())
            {
                _selectionDock.Visibility = Visibility.Collapsed;
                return;
            }

            // 根据状态更新“置顶/取消置顶”按钮文案与图标
            if (_btnSelectionTop != null && _selectedAttachment != null)
            {
                var l = LocalizationService.Instance;
                if (_selectedAttachment.IsPinnedTop)
                {
                    _btnSelectionTop.Content = l.GetString("Common_Unpin");
                    _btnSelectionTop.Tag = "ArrangeSendToBack";
                }
                else
                {
                    _btnSelectionTop.Content = l.GetString("Common_Pin");
                    _btnSelectionTop.Tag = "ArrangeBringToFront";
                }
            }
            else if (_btnSelectionTop != null)
            {
                _btnSelectionTop.Content = LocalizationService.Instance.GetString("Common_Pin");
                _btnSelectionTop.Tag = "ArrangeBringToFront";
            }

            // MVP：导入元素暂不支持“复制”，仅对笔迹复制
            if (_btnSelectionCopy != null)
            {
                _btnSelectionCopy.Visibility = _selectedAttachment != null ? Visibility.Collapsed : Visibility.Visible;
            }

            if (_selectionDock.Visibility != Visibility.Visible)
            {
                _selectionDock.Visibility = Visibility.Visible;
                _selectionDock.UpdateLayout();
            }
            else
            {
                // 当按钮显隐变化时，宽高会变，需要重新测量
                _selectionDock.UpdateLayout();
            }

            var b = selectionBounds.Value;
            var bottomCenter = new Point(b.X + b.Width / 2.0, b.Y + b.Height);
            Point inRoot = MyCanvas.TranslatePoint(bottomCenter, _rootGrid);

            double dockW = _selectionDock.ActualWidth;
            double dockH = _selectionDock.ActualHeight;
            if (dockW <= 0 || dockH <= 0)
            {
                _selectionDock.UpdateLayout();
                dockW = _selectionDock.ActualWidth;
                dockH = _selectionDock.ActualHeight;
            }

            double x = inRoot.X - dockW / 2.0;
            double y = inRoot.Y + 10;

            if (y + dockH > _rootGrid.ActualHeight)
            {
                y = inRoot.Y - dockH - 10;
            }

            x = Math.Max(8, Math.Min(_rootGrid.ActualWidth - dockW - 8, x));
            y = Math.Max(8, Math.Min(_rootGrid.ActualHeight - dockH - 8, y));

            Canvas.SetLeft(_selectionDock, x);
            Canvas.SetTop(_selectionDock, y);
        }

        private static int GetNextAttachmentZIndex(BoardPage page)
        {
            return GetNextAttachmentZIndex(page, pinnedTop: false);
        }

        private static int GetNextAttachmentZIndex(BoardPage page, bool pinnedTop)
        {
            var list = page.Attachments.Where(a => a.IsPinnedTop == pinnedTop).ToList();
            if (list.Count == 0) return 1;
            return list.Max(a => a.ZIndex) + 1;
        }

        private void BtnSelectionTop_Click(object sender, RoutedEventArgs e)
        {
            if (!IsSelectModeActive()) return;

            if (_selectedAttachment != null)
            {
                var page = _pageService.CurrentPage;
                if (page == null) return;
                if (_selectedAttachment.IsPinnedTop)
                {
                    _selectedAttachment.IsPinnedTop = false;
                    _selectedAttachment.ZIndex = GetNextAttachmentZIndex(page, pinnedTop: false);
                }
                else
                {
                    _selectedAttachment.IsPinnedTop = true;
                    _selectedAttachment.ZIndex = GetNextAttachmentZIndex(page, pinnedTop: true);
                }
                ScheduleSelectionDockUpdate();
                return;
            }

            if (_selectedInkStrokes.Count == 0) return;

            var pageInk = _pageService.CurrentPage;
            if (pageInk == null) return;

            var before = new List<InkStroke>(pageInk.Ink.Strokes);

            var selected = _selectedInkStrokes.ToList();
            for (int i = 0; i < selected.Count; i++)
            {
                _ = pageInk.Ink.Strokes.Remove(selected[i]);
            }
            for (int i = 0; i < selected.Count; i++)
            {
                pageInk.Ink.Strokes.Add(selected[i]);
            }

            var after = new List<InkStroke>(pageInk.Ink.Strokes);

            pageInk.InkUndoHistory.Begin();
            pageInk.InkUndoHistory.Record(new ReorderStrokesCommand(before, after));
            pageInk.InkUndoHistory.End();

            pageInk.ContentVersion++;
            InvalidateInkSurface();
            ScheduleSelectionDockUpdate();
        }

        private void BtnSelectionCopy_Click(object sender, RoutedEventArgs e)
        {
            if (!IsSelectModeActive()) return;

            if (_selectedAttachment != null) return;

            if (_selectedInkStrokes.Count == 0) return;

            var page = _pageService.CurrentPage;
            if (page == null) return;

            const double offset = 20;
            var clones = new List<InkStroke>(_selectedInkStrokes.Count);

            for (int i = 0; i < _selectedInkStrokes.Count; i++)
            {
                InkStroke source = _selectedInkStrokes[i];
                clones.Add(CloneInkStrokeWithOffset(source, offset, offset));
            }

            page.InkUndoHistory.Begin();

            for (int i = 0; i < clones.Count; i++)
            {
                InkStroke stroke = clones[i];
                int index = page.Ink.Strokes.Count;
                page.Ink.Strokes.Add(stroke);
                page.InkUndoHistory.Record(new InsertStrokeCommand(index, stroke));
                page.InkSpatialIndex.AddStroke(stroke);
            }

            page.InkUndoHistory.End();

            _selectedInkStrokes.Clear();
            _selectedInkStrokes.AddRange(clones);
            UpdateInkSelectionOverlay();

            page.ContentVersion++;
            InvalidateInkSurface();
            ScheduleSelectionDockUpdate();
        }

        private void BtnSelectionDelete_Click(object sender, RoutedEventArgs e)
        {
            if (!IsSelectModeActive()) return;

            if (_selectedAttachment != null)
            {
                var page = _pageService.CurrentPage;
                if (page == null) return;
                page.Attachments.Remove(_selectedAttachment);
                SelectAttachment(null);
                return;
            }

            if (_selectedInkStrokes.Count == 0) return;

            var cur = _pageService.CurrentPage;
            if (cur == null) return;

            var strokeIndices = new List<(int Index, InkStroke Stroke)>(_selectedInkStrokes.Count);
            for (int i = 0; i < _selectedInkStrokes.Count; i++)
            {
                InkStroke stroke = _selectedInkStrokes[i];
                int index = cur.Ink.Strokes.IndexOf(stroke);
                if (index >= 0)
                {
                    strokeIndices.Add((index, stroke));
                }
            }

            if (strokeIndices.Count == 0) return;

            strokeIndices.Sort((a, b) => b.Index.CompareTo(a.Index));

            cur.InkUndoHistory.Begin();
            for (int i = 0; i < strokeIndices.Count; i++)
            {
                (int index, InkStroke stroke) = strokeIndices[i];
                if (cur.Ink.Strokes.Remove(stroke))
                {
                    cur.InkUndoHistory.Record(new RemoveStrokeCommand(index, stroke));
                }
            }
            cur.InkUndoHistory.End();

            cur.InkSpatialIndex.Rebuild(cur.Ink);
            cur.ContentVersion++;
            ClearSelectedInkStrokes();
            InvalidateInkSurface();
            ScheduleSelectionDockUpdate();
        }
    }
}
