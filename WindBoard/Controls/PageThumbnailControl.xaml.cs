using System;
using System.Collections.Generic;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using WindBoard.Board;
using WindBoard.Board.Editing;
using Windows.Foundation;
using Windows.UI;

namespace WindBoard.Controls
{
    /// <summary>
    /// 页面缩略图控件。
    /// 
    /// 当前实现为轻量级预览：
    /// - 基于页面笔迹数据在 XAML 侧绘制 Polyline
    /// - 不依赖 DirectX 截屏（SwapChainPanel 无法直接用 RenderTargetBitmap）
    /// 
    /// 后续如果需要更高保真缩略图，可替换为离屏 DirectX 渲染到纹理再转 Bitmap。
    /// </summary>
    public sealed partial class PageThumbnailControl : UserControl
    {
        private const double CanvasPadding = 8;
        private const int MaxPointsPerStroke = 240;
        private static readonly Brush DefaultThumbnailBackgroundBrush = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));

        private BoardPage? _page;
        private readonly DispatcherQueue _dispatcherQueue;
        private bool _isSessionSubscribed;

        public PageThumbnailControl()
        {
            InitializeComponent();

            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            Loaded += (_, _) =>
            {
                EnsureSessionSubscription();
                UpdatePreview();
            };
            Unloaded += (_, _) => RemoveSessionSubscription();
            SizeChanged += (_, _) => UpdatePreview();
        }

        /// <summary>
        /// 页面对象（内部实际为 <see cref="BoardPage"/>）。
        /// </summary>
        public object? Page
        {
            get => GetValue(PageProperty);
            set => SetValue(PageProperty, value);
        }

        public static readonly DependencyProperty PageProperty =
            DependencyProperty.Register(
                nameof(Page),
                typeof(object),
                typeof(PageThumbnailControl),
                new PropertyMetadata(null, OnPagePropertyChanged));

        /// <summary>
        /// 缩略图背景（不跟随系统深浅色变化）。
        /// 
        /// 说明：后续接入“自定义画布背景”时，只需要在外部为该属性赋值即可，
        /// 不必让缩略图依赖系统 ThemeResource，从而避免主题切换导致缩略图底色变化。
        /// </summary>
        public Brush ThumbnailBackground
        {
            get => (Brush)GetValue(ThumbnailBackgroundProperty);
            set => SetValue(ThumbnailBackgroundProperty, value);
        }

        public static readonly DependencyProperty ThumbnailBackgroundProperty =
            DependencyProperty.Register(
                nameof(ThumbnailBackground),
                typeof(Brush),
                typeof(PageThumbnailControl),
                new PropertyMetadata(DefaultThumbnailBackgroundBrush));

        private static void OnPagePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (PageThumbnailControl)d;
            control.AttachPage(e.NewValue);
        }

        private void AttachPage(object? value)
        {
            RemoveSessionSubscription();

            _page = value as BoardPage;

            EnsureSessionSubscription();

            UpdatePreview();
        }

        private void OnPageSessionStateChanged()
        {
            // 会话事件可能在不同触发点产生，这里统一切回 UI 线程刷新缩略图。
            _dispatcherQueue.TryEnqueue(UpdatePreview);
        }

        private void EnsureSessionSubscription()
        {
            if (_isSessionSubscribed || _page is null || !IsLoaded)
            {
                return;
            }

            _page.Session.StateChanged += OnPageSessionStateChanged;
            _isSessionSubscribed = true;
        }

        private void RemoveSessionSubscription()
        {
            if (!_isSessionSubscribed || _page is null)
            {
                return;
            }

            _page.Session.StateChanged -= OnPageSessionStateChanged;
            _isSessionSubscribed = false;
        }

        private void UpdatePreview()
        {
            if (StrokeCanvas is null || HostGrid is null)
            {
                return;
            }

            double width = HostGrid.ActualWidth;
            double height = HostGrid.ActualHeight;
            if (width <= 1 || height <= 1)
            {
                return;
            }

            StrokeCanvas.Children.Clear();

            BoardDocument? document = _page?.Session.Document;
            if (document is null || document.Strokes.Count == 0)
            {
                StrokeCanvas.Clip = new RectangleGeometry { Rect = new Rect(0, 0, width, height) };
                return;
            }

            if (!TryGetDocumentBounds(document.Strokes, out float minX, out float minY, out float maxX, out float maxY))
            {
                StrokeCanvas.Clip = new RectangleGeometry { Rect = new Rect(0, 0, width, height) };
                return;
            }

            double availableW = Math.Max(1, width - CanvasPadding * 2);
            double availableH = Math.Max(1, height - CanvasPadding * 2);
            double boundsW = Math.Max(1e-3, maxX - minX);
            double boundsH = Math.Max(1e-3, maxY - minY);

            double scale = Math.Min(availableW / boundsW, availableH / boundsH);
            // 缩略图只做“缩小适配”，不做“放大填充”：避免少量笔迹（点/短线）被异常放大显示。
            scale = Math.Min(scale, 1.0);
            scale = Math.Max(scale, 0.0001);
            double scaledW = boundsW * scale;
            double scaledH = boundsH * scale;
            double offsetX = (availableW - scaledW) / 2;
            double offsetY = (availableH - scaledH) / 2;

            double baseX = CanvasPadding + offsetX;
            double baseY = CanvasPadding + offsetY;

            // 轻量渲染：每条笔迹用一条 Polyline 近似。
            foreach (Stroke stroke in document.Strokes)
            {
                if (stroke.Points.Count < 2)
                {
                    continue;
                }

                var polyline = new Polyline
                {
                    Stroke = new SolidColorBrush(ToUiColor(stroke.Color)),
                    StrokeThickness = Math.Max(0.8, stroke.BaseSize * scale * 0.55),
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    IsHitTestVisible = false,
                };

                var points = new PointCollection();
                foreach (StrokePoint point in EnumeratePointsForThumbnail(stroke.Points))
                {
                    double x = baseX + (point.Position.X - minX) * scale;
                    double y = baseY + (point.Position.Y - minY) * scale;
                    points.Add(new Point(x, y));
                }

                if (points.Count >= 2)
                {
                    polyline.Points = points;
                    StrokeCanvas.Children.Add(polyline);
                }
            }

            StrokeCanvas.Clip = new RectangleGeometry { Rect = new Rect(0, 0, width, height) };
        }

        private static bool TryGetDocumentBounds(IReadOnlyList<Stroke> strokes, out float minX, out float minY, out float maxX, out float maxY)
        {
            minX = float.PositiveInfinity;
            minY = float.PositiveInfinity;
            maxX = float.NegativeInfinity;
            maxY = float.NegativeInfinity;

            foreach (Stroke stroke in strokes)
            {
                if (!stroke.HasBounds)
                {
                    continue;
                }

                minX = Math.Min(minX, stroke.BoundsMin.X);
                minY = Math.Min(minY, stroke.BoundsMin.Y);
                maxX = Math.Max(maxX, stroke.BoundsMax.X);
                maxY = Math.Max(maxY, stroke.BoundsMax.Y);
            }

            return minX <= maxX && minY <= maxY;
        }

        private static IEnumerable<StrokePoint> EnumeratePointsForThumbnail(IReadOnlyList<StrokePoint> points)
        {
            // 缩略图对“极高密度点”不需要逐点绘制，做一次简单降采样即可。
            if (points.Count <= MaxPointsPerStroke)
            {
                return points;
            }

            int step = Math.Max(1, points.Count / MaxPointsPerStroke);
            var sampled = new List<StrokePoint>(MaxPointsPerStroke + 1);
            int lastIndex = points.Count - 1;
            for (int i = 0; i < lastIndex; i += step)
            {
                sampled.Add(points[i]);
            }

            // 保证末尾点存在，避免截断导致形态缺失。
            sampled.Add(points[lastIndex]);

            return sampled;
        }

        private static Color ToUiColor(Vortice.Mathematics.Color4 color)
        {
            byte r = (byte)Math.Clamp((int)Math.Round(color.R * 255), 0, 255);
            byte g = (byte)Math.Clamp((int)Math.Round(color.G * 255), 0, 255);
            byte b = (byte)Math.Clamp((int)Math.Round(color.B * 255), 0, 255);
            return Color.FromArgb(255, r, g, b);
        }
    }
}
