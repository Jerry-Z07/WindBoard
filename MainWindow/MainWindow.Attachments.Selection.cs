using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;

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
                ClearInkCanvasSelectionPreserveEditingMode();
            }

            UpdateAttachmentSelectionOverlay();
            ScheduleSelectionDockUpdate();
        }

        private void ClearInkCanvasSelectionPreserveEditingMode()
        {
            if (MyCanvas == null) return;

            var editingMode = MyCanvas.EditingMode;
            try
            {
                MyCanvas.Select(new StrokeCollection(), Array.Empty<UIElement>());
            }
            catch
            {
            }
            finally
            {
                try { MyCanvas.EditingMode = editingMode; } catch { }
            }
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
            if (Keyboard.IsKeyDown(Key.Space)) return false;

            var canvasPoint = e.GetPosition(MyCanvas);
            var hit = HitTestAttachment(canvasPoint);
            if (hit == null)
            {
                // 未命中附件：交给 InkCanvas 做笔迹选择，同时清除当前附件选择框
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

        private void MyCanvas_SelectionChanged(object sender, EventArgs e)
        {
            var strokes = MyCanvas.GetSelectedStrokes();
            if (strokes != null && strokes.Count > 0)
            {
                SelectAttachment(null);
            }
            ScheduleSelectionDockUpdate();
        }

        private void MyCanvas_SelectionMoved(object sender, EventArgs e) => ScheduleSelectionDockUpdate();
        private void MyCanvas_SelectionResized(object sender, EventArgs e) => ScheduleSelectionDockUpdate();

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
            else
            {
                var strokes = MyCanvas.GetSelectedStrokes();
                if (strokes != null && strokes.Count > 0)
                {
                    selectionBounds = MyCanvas.GetSelectionBounds();
                }
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
                if (_selectedAttachment.IsPinnedTop)
                {
                    _btnSelectionTop.Content = "取消置顶";
                    _btnSelectionTop.Tag = "ArrangeSendToBack";
                }
                else
                {
                    _btnSelectionTop.Content = "置顶";
                    _btnSelectionTop.Tag = "ArrangeBringToFront";
                }
            }
            else if (_btnSelectionTop != null)
            {
                _btnSelectionTop.Content = "置顶";
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

            var selected = MyCanvas.GetSelectedStrokes();
            if (selected == null || selected.Count == 0) return;

            var list = selected.ToList();
            foreach (var s in list) MyCanvas.Strokes.Remove(s);
            foreach (var s in list) MyCanvas.Strokes.Add(s);

            var reselect = new StrokeCollection();
            foreach (var s in list) reselect.Add(s);
            MyCanvas.Select(reselect);
            ScheduleSelectionDockUpdate();
        }

        private void BtnSelectionCopy_Click(object sender, RoutedEventArgs e)
        {
            if (!IsSelectModeActive()) return;

            if (_selectedAttachment != null) return;

            var selected = MyCanvas.GetSelectedStrokes();
            if (selected == null || selected.Count == 0) return;

            var clones = selected.Clone();
            var m = new Matrix();
            m.Translate(20, 20);
            foreach (var s in clones)
            {
                s.Transform(m, false);
            }
            foreach (var s in clones) MyCanvas.Strokes.Add(s);
            MyCanvas.Select(clones);
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

            var selected = MyCanvas.GetSelectedStrokes();
            if (selected == null || selected.Count == 0) return;
            foreach (var s in selected.ToList()) MyCanvas.Strokes.Remove(s);
            ClearInkCanvasSelectionPreserveEditingMode();
            ScheduleSelectionDockUpdate();
        }
    }
}

