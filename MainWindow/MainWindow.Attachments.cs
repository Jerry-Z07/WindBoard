using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MaterialDesignThemes.Wpf;
using WindBoard.Views.Dialogs;

namespace WindBoard
{
    public partial class MainWindow
    {
        private const double AttachmentMinSize = 60;

        private BoardAttachment? _selectedAttachment;

        private Border? _attachmentSelectionFrame;
        private Thumb? _attachmentMoveThumb;
        private readonly Dictionary<string, Thumb> _attachmentResizeThumbs = new();

         private Border? _selectionDock;
         private Button? _btnSelectionTop;
         private Button? _btnSelectionCopy;
         private bool _selectionDockUpdateScheduled;

        private readonly StaBitmapLoader _bitmapLoader = new();

         private void InitializeAttachmentUi()
         {
             _selectionDock = (Border)FindName("SelectionDock");
             _btnSelectionTop = (Button)FindName("BtnSelectionTop");
             _btnSelectionCopy = (Button)FindName("BtnSelectionCopy");

             if (MyCanvas != null)
             {
                MyCanvas.SelectionChanged -= MyCanvas_SelectionChanged;
                MyCanvas.SelectionChanged += MyCanvas_SelectionChanged;

                MyCanvas.SelectionMoved -= MyCanvas_SelectionMoved;
                MyCanvas.SelectionMoved += MyCanvas_SelectionMoved;

                MyCanvas.SelectionResized -= MyCanvas_SelectionResized;
                MyCanvas.SelectionResized += MyCanvas_SelectionResized;
            }

            BuildAttachmentSelectionOverlay();

            if (Viewport != null)
            {
                Viewport.SizeChanged -= Viewport_SizeChanged;
                Viewport.SizeChanged += Viewport_SizeChanged;
            }
        }

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
            if (e.ClickCount < 2) return;

            if (TryOpenAttachmentExternal(_selectedAttachment))
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

        private async void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            var result = await DialogHost.Show(new ImportDialog(), "MainDialogHost");
            if (result is not ImportRequest req) return;
            await ImportAttachmentsAsync(req);
        }

        private async Task ImportAttachmentsAsync(ImportRequest req)
        {
            var page = _pageService.CurrentPage;
            if (page == null) return;

            var center = GetViewportCenterCanvasPoint();
            int baseZ = GetNextAttachmentZIndex(page, pinnedTop: false);

            var pendingAdds = new List<BoardAttachment>();

            async Task AddAttachmentAsync(BoardAttachment a)
            {
                pendingAdds.Add(a);
                if (pendingAdds.Count >= 16)
                {
                    foreach (var it in pendingAdds) page.Attachments.Add(it);
                    pendingAdds.Clear();
                    await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                }
            }

            double cellW = 420;
            double cellH = 280;
            double gap = 24;
            int colCount = 4;
            int index = 0;

            Point NextPos()
            {
                int col = index % colCount;
                int row = index / colCount;
                index++;
                return new Point(center.X + col * (cellW + gap), center.Y + row * (cellH + gap));
            }

            var imageAttachments = new List<(BoardAttachment attachment, string path)>();

            foreach (var path in req.ImagePaths.Where(File.Exists))
            {
                var pos = NextPos();
                var att = new BoardAttachment
                {
                    Type = BoardAttachmentType.Image,
                    FilePath = path,
                    X = pos.X,
                    Y = pos.Y,
                    Width = 480,
                    Height = 320,
                    ZIndex = baseZ++
                };
                await AddAttachmentAsync(att);
                imageAttachments.Add((att, path));
            }

            foreach (var path in req.VideoPaths.Where(File.Exists))
            {
                var pos = NextPos();
                await AddAttachmentAsync(new BoardAttachment
                {
                    Type = BoardAttachmentType.Video,
                    FilePath = path,
                    X = pos.X,
                    Y = pos.Y,
                    Width = 480,
                    Height = 270,
                    ZIndex = baseZ++
                });
            }

            foreach (var path in req.TextFilePaths.Where(File.Exists))
            {
                string? content = await ReadTextFileAsync(path);
                if (string.IsNullOrWhiteSpace(content)) continue;

                var pos = NextPos();
                await AddAttachmentAsync(new BoardAttachment
                {
                    Type = BoardAttachmentType.Text,
                    FilePath = path,
                    Text = content,
                    X = pos.X,
                    Y = pos.Y,
                    Width = 420,
                    Height = 260,
                    ZIndex = baseZ++
                });
            }

            if (!string.IsNullOrWhiteSpace(req.TextContent))
            {
                var pos = NextPos();
                await AddAttachmentAsync(new BoardAttachment
                {
                    Type = BoardAttachmentType.Text,
                    Text = req.TextContent,
                    X = pos.X,
                    Y = pos.Y,
                    Width = 420,
                    Height = 260,
                    ZIndex = baseZ++
                });
            }

            foreach (var url in req.Urls)
            {
                if (!TryNormalizeHttpUrl(url, out var normalized)) continue;

                var pos = NextPos();
                await AddAttachmentAsync(new BoardAttachment
                {
                    Type = BoardAttachmentType.Link,
                    Url = normalized,
                    X = pos.X,
                    Y = pos.Y,
                    Width = 360,
                    Height = 120,
                    ZIndex = baseZ++
                });
            }

            foreach (var it in pendingAdds) page.Attachments.Add(it);
            pendingAdds.Clear();

            // 确保 ItemsSource 已绑定到当前页附件集合（防止资源/绑定初始化早于架构初始化时的空引用导致不刷新）
            OnPropertyChanged(nameof(CurrentAttachments));

            // 图片异步解码（避免 UI 卡顿；解码完成后再回到 UI 线程赋值）
            foreach (var (attachment, path) in imageAttachments)
            {
                _ = LoadImageIntoAttachmentAsync(attachment, path);
            }

            if (page.Attachments.Count > 0)
            {
                var newest = page.Attachments.LastOrDefault();
                if (newest != null)
                {
                    SelectAttachment(newest);
                    RadioSelect.IsChecked = true;
                }
            }
        }

        private async Task LoadImageIntoAttachmentAsync(BoardAttachment attachment, string path)
        {
            try
            {
                int decodeWidth = (int)Math.Clamp(attachment.Width * 2.0, 400, 2048);
                var image = await _bitmapLoader.LoadAsync(path, decodeWidth).ConfigureAwait(false);

                await Dispatcher.InvokeAsync(() =>
                {
                    attachment.Image = image;

                    if (image is BitmapSource bs && bs.PixelWidth > 0 && bs.PixelHeight > 0)
                    {
                        double aspect = (double)bs.PixelWidth / bs.PixelHeight;
                        attachment.Height = Math.Max(AttachmentMinSize, attachment.Width / aspect);
                    }
                });
            }
            catch
            {
                // ignore decode failures (keep placeholder card)
            }
        }

        private static bool TryNormalizeHttpUrl(string raw, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            raw = raw.Trim();
            if (!raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                raw = "https://" + raw;
            }

            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return false;
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            normalized = uri.ToString();
            return true;
        }

        private static async Task<string?> ReadTextFileAsync(string path)
        {
            try
            {
                byte[] bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
                if (bytes.Length == 0) return null;

                Encoding enc = DetectEncodingFromBom(bytes) ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
                return enc.GetString(bytes);
            }
            catch
            {
                return null;
            }
        }

        private static Encoding? DetectEncodingFromBom(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8;
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode; // UTF-16 LE
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode; // UTF-16 BE
            if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00) return Encoding.UTF32; // UTF-32 LE
            return null;
        }

        private Point GetViewportCenterCanvasPoint()
        {
            double zoom = _zoomPanService.Zoom <= 0 ? 1.0 : _zoomPanService.Zoom;
            double vx = Viewport.ActualWidth / 2.0;
            double vy = Viewport.ActualHeight / 2.0;

            double x = (vx - _zoomPanService.PanX) / zoom;
            double y = (vy - _zoomPanService.PanY) / zoom;
            return new Point(x, y);
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

        private void OpenExternal(string pathOrUrl)
        {
            try
            {
                Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true });
            }
            catch
            {
                // ignore
            }
        }

        private bool TryOpenAttachmentExternal(BoardAttachment? attachment)
        {
            if (attachment == null) return false;

            if (attachment.Type == BoardAttachmentType.Video && !string.IsNullOrWhiteSpace(attachment.FilePath))
            {
                OpenExternal(attachment.FilePath);
                return true;
            }

            if (attachment.Type == BoardAttachmentType.Link && !string.IsNullOrWhiteSpace(attachment.Url))
            {
                OpenExternal(attachment.Url);
                return true;
            }

            return false;
        }

        private sealed class StaBitmapLoader : IDisposable
        {
            private readonly Thread _thread;
            private System.Windows.Threading.Dispatcher? _dispatcher;
            private readonly ManualResetEventSlim _ready = new(false);
            private readonly ManualResetEventSlim _stopped = new(false);
            private bool _disposed;

            public StaBitmapLoader()
            {
                _thread = new Thread(ThreadStart)
                {
                    IsBackground = true,
                    Name = "WindBoard.StaBitmapLoader"
                };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
                _ready.Wait();
            }

            private void ThreadStart()
            {
                try
                {
                    _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                    _ready.Set();
                    System.Windows.Threading.Dispatcher.Run();
                }
                finally
                {
                    _stopped.Set();
                }
            }

            public Task<BitmapSource> LoadAsync(string path, int decodePixelWidth)
            {
                if (_disposed)
                {
                    return Task.FromException<BitmapSource>(new ObjectDisposedException(nameof(StaBitmapLoader)));
                }

                var tcs = new TaskCompletionSource<BitmapSource>(TaskCreationOptions.RunContinuationsAsynchronously);

                var dispatcher = _dispatcher;
                if (dispatcher == null)
                {
                    tcs.TrySetException(new InvalidOperationException("Bitmap loader dispatcher not initialized."));
                    return tcs.Task;
                }

                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                        bi.StreamSource = fs;
                        if (decodePixelWidth > 0) bi.DecodePixelWidth = decodePixelWidth;
                        bi.EndInit();
                        bi.Freeze();
                        tcs.TrySetResult(bi);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                }));

                return tcs.Task;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                try
                {
                    var dispatcher = _dispatcher;
                    if (dispatcher != null)
                    {
                        dispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Background);
                    }
                }
                catch
                {
                }

                try
                {
                    if (!_stopped.Wait(2000) && _thread.IsAlive)
                    {
                        _thread.Join(TimeSpan.FromSeconds(2));
                    }
                }
                catch
                {
                }

                _ready.Dispose();
                _stopped.Dispose();
            }
        }
    }
}
