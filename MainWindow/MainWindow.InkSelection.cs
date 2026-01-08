using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WindBoard.Core.Ink.Backend;

namespace WindBoard
{
    public partial class MainWindow
    {
        private Canvas? _inkSelectionOverlay;
        private Border? _inkSelectionFrame;
        private Thumb? _inkSelectionMoveThumb;
        private readonly Dictionary<string, Thumb> _inkSelectionResizeThumbs = new();
        private Rectangle? _inkSelectionMarquee;

        private void InitializeInkSelectionUi()
        {
            _inkSelectionOverlay = (Canvas)FindName("InkSelectionOverlay");
            if (_inkSelectionOverlay == null) return;

            _inkSelectionOverlay.Children.Clear();

            _inkSelectionMarquee = new Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0x5B, 0xA1, 0xFF)),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                Fill = new SolidColorBrush(Color.FromArgb(0x22, 0x5B, 0xA1, 0xFF)),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            _inkSelectionOverlay.Children.Add(_inkSelectionMarquee);

            _inkSelectionFrame = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x5B, 0xA1, 0xFF)),
                BorderThickness = new Thickness(2),
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(8),
                Visibility = Visibility.Collapsed
            };

            var grid = new Grid();
            _inkSelectionFrame.Child = grid;

            _inkSelectionMoveThumb = new Thumb
            {
                Cursor = Cursors.SizeAll,
                Background = Brushes.Transparent
            };
            _inkSelectionMoveThumb.DragStarted += InkSelectionThumb_DragStarted;
            _inkSelectionMoveThumb.DragCompleted += InkSelectionThumb_DragCompleted;
            _inkSelectionMoveThumb.DragDelta += InkSelectionMoveThumb_DragDelta;
            grid.Children.Add(_inkSelectionMoveThumb);

            AddInkResizeThumb(grid, "TL", HorizontalAlignment.Left, VerticalAlignment.Top, Cursors.SizeNWSE);
            AddInkResizeThumb(grid, "T", HorizontalAlignment.Center, VerticalAlignment.Top, Cursors.SizeNS);
            AddInkResizeThumb(grid, "TR", HorizontalAlignment.Right, VerticalAlignment.Top, Cursors.SizeNESW);
            AddInkResizeThumb(grid, "L", HorizontalAlignment.Left, VerticalAlignment.Center, Cursors.SizeWE);
            AddInkResizeThumb(grid, "R", HorizontalAlignment.Right, VerticalAlignment.Center, Cursors.SizeWE);
            AddInkResizeThumb(grid, "BL", HorizontalAlignment.Left, VerticalAlignment.Bottom, Cursors.SizeNESW);
            AddInkResizeThumb(grid, "B", HorizontalAlignment.Center, VerticalAlignment.Bottom, Cursors.SizeNS);
            AddInkResizeThumb(grid, "BR", HorizontalAlignment.Right, VerticalAlignment.Bottom, Cursors.SizeNWSE);

            _inkSelectionOverlay.Children.Add(_inkSelectionFrame);
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
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x5B, 0xA1, 0xFF)),
                BorderThickness = new Thickness(2),
                Margin = new Thickness(-7),
                Tag = key
            };

            thumb.DragStarted += InkSelectionThumb_DragStarted;
            thumb.DragCompleted += InkSelectionThumb_DragCompleted;
            thumb.DragDelta += InkSelectionResizeThumb_DragDelta;

            _inkSelectionResizeThumbs[key] = thumb;
            host.Children.Add(thumb);
        }

        private void InkSelectionThumb_DragStarted(object sender, DragStartedEventArgs e)
        {
            if (!IsSelectModeActive()) return;
            if (_inkService == null) return;

            _inkService.BeginUndoTransaction();
        }

        private void InkSelectionThumb_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (_inkService == null) return;
            _inkService.EndUndoTransaction();
        }

        private void InkSelectionMoveThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (!IsSelectModeActive()) return;
            var backend = _inkBackend;

            if (backend.MoveSelection(e.HorizontalChange, e.VerticalChange))
            {
                UpdateInkSelectionOverlay();
                ScheduleSelectionDockUpdate();
            }
        }

        private void InkSelectionResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (!IsSelectModeActive()) return;
            if (sender is not Thumb t || t.Tag is not string key) return;

            var backend = _inkBackend;

            Rect from = backend.GetSelectionBounds();
            if (from.IsEmpty) return;

            double x = from.X;
            double y = from.Y;
            double w = from.Width;
            double h = from.Height;

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

            const double minSize = 1.0;
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

            var to = new Rect(x, y, w, h);
            if (backend.ScaleSelection(from, to))
            {
                UpdateInkSelectionOverlay();
                ScheduleSelectionDockUpdate();
            }
        }

        private void UpdateInkSelectionMarquee(Rect? rect)
        {
            if (_inkSelectionOverlay == null || _inkSelectionMarquee == null) return;

            if (rect == null || rect.Value.IsEmpty)
            {
                _inkSelectionMarquee.Visibility = Visibility.Collapsed;
                UpdateInkSelectionOverlay();
                return;
            }

            var r = rect.Value;
            if (r.Width < 0)
            {
                r = new Rect(r.X + r.Width, r.Y, -r.Width, r.Height);
            }
            if (r.Height < 0)
            {
                r = new Rect(r.X, r.Y + r.Height, r.Width, -r.Height);
            }

            _inkSelectionMarquee.Visibility = Visibility.Visible;
            Canvas.SetLeft(_inkSelectionMarquee, r.X);
            Canvas.SetTop(_inkSelectionMarquee, r.Y);
            _inkSelectionMarquee.Width = Math.Max(0, r.Width);
            _inkSelectionMarquee.Height = Math.Max(0, r.Height);

            if (_inkSelectionFrame != null)
            {
                _inkSelectionFrame.Visibility = Visibility.Collapsed;
            }
        }

        private void OnInkSelectionChanged()
        {
            UpdateInkSelectionOverlay();
            ScheduleSelectionDockUpdate();
        }

        private void UpdateInkSelectionOverlay()
        {
            if (_inkSelectionOverlay == null || _inkSelectionFrame == null) return;

            if (!IsSelectModeActive())
            {
                _inkSelectionFrame.Visibility = Visibility.Collapsed;
                return;
            }

            var backend = _inkBackend;

            Rect bounds = backend.GetSelectionBounds();
            if (bounds.IsEmpty || !backend.HasSelection)
            {
                _inkSelectionFrame.Visibility = Visibility.Collapsed;
                return;
            }

            if (_inkSelectionMarquee != null && _inkSelectionMarquee.Visibility == Visibility.Visible)
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

        private bool IsInkSelectionOverlaySource(DependencyObject? source)
        {
            var overlay = _inkSelectionOverlay;
            if (overlay == null || source == null) return false;

            DependencyObject? current = source;
            while (current != null)
            {
                if (ReferenceEquals(current, overlay))
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }
    }
}
