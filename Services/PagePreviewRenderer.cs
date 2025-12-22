using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WindBoard.Services
{
    /// <summary>
    /// 页面缩略图渲染服务：将 StrokeCollection 渲染为固定尺寸的预览图。
    /// </summary>
    public class PagePreviewRenderer
    {
        /// <summary>
        /// 渲染预览图。
        /// </summary>
        /// <param name="strokes">用于渲染的笔迹集合（可为 null 或空）。</param>
        /// <param name="width">目标宽度（像素）。</param>
        /// <param name="height">目标高度（像素）。</param>
        /// <param name="padding">内容内边距（像素）。</param>
        /// <returns>渲染后的位图（已 Freeze）。</returns>
        public ImageSource Render(StrokeCollection? strokes, int width = 220, int height = 120, double padding = 10)
        {
            int w = width;
            int h = height;

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // 背景：和白板更接近的深色
                dc.DrawRoundedRectangle(
                    new SolidColorBrush(Color.FromRgb(0x0F, 0x12, 0x16)),
                    null,
                    new Rect(0, 0, w, h),
                    10, 10);

                if (strokes != null && strokes.Count > 0)
                {
                    Rect bounds = strokes.GetBounds();

                    // 防止 0 宽高
                    double bw = Math.Max(bounds.Width, 1);
                    double bh = Math.Max(bounds.Height, 1);

                    double scale = Math.Min((w - 2 * padding) / bw, (h - 2 * padding) / bh);

                    // 将 strokes bounds 贴到 padding 内并居中
                    double targetW = bw * scale;
                    double targetH = bh * scale;

                    double tx = padding + (w - 2 * padding - targetW) / 2.0;
                    double ty = padding + (h - 2 * padding - targetH) / 2.0;

                    dc.PushTransform(new TranslateTransform(tx, ty));
                    dc.PushTransform(new ScaleTransform(scale, scale));
                    dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));

                    strokes.Draw(dc);

                    // 还原 transform
                    dc.Pop(); dc.Pop(); dc.Pop();
                }
            }

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }
    }
}
