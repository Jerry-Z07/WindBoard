using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WindBoard.Models.Export;

namespace WindBoard.Services.Export
{
    /// <summary>
    /// 导出渲染器：将页面内容渲染为位图
    /// </summary>
    public sealed class ExportRenderer
    {
        /// <summary>
        /// 渲染单页为位图
        /// </summary>
        /// <param name="page">要渲染的页面</param>
        /// <param name="options">图片导出选项</param>
        /// <returns>渲染后的位图</returns>
        public BitmapSource RenderPage(BoardPage page, ImageExportOptions options)
        {
            int targetWidth = options.Width;
            int targetHeight = options.Height;
            bool keepAspectRatio = options.KeepAspectRatio;
            Color backgroundColor = options.BackgroundColor;

            // 1. 计算内容边界
            Rect contentBounds = CalculateContentBounds(page);

            // 如果没有内容，返回空白图片
            if (contentBounds.IsEmpty)
            {
                return CreateEmptyBitmap(targetWidth, targetHeight, backgroundColor);
            }

            // 2. 添加边距（内容区域的 5%）
            double paddingFactor = 0.05;
            double padding = Math.Max(contentBounds.Width, contentBounds.Height) * paddingFactor;
            padding = Math.Max(padding, 20); // 最小 20 像素
            contentBounds.Inflate(padding, padding);

            // 3. 计算缩放比例
            double scaleX = targetWidth / contentBounds.Width;
            double scaleY = targetHeight / contentBounds.Height;
            double scale;
            double offsetX = 0;
            double offsetY = 0;

            if (keepAspectRatio)
            {
                scale = Math.Min(scaleX, scaleY);
                // 居中
                double renderedWidth = contentBounds.Width * scale;
                double renderedHeight = contentBounds.Height * scale;
                offsetX = (targetWidth - renderedWidth) / 2.0;
                offsetY = (targetHeight - renderedHeight) / 2.0;
            }
            else
            {
                scale = Math.Min(scaleX, scaleY);
                offsetX = (targetWidth - contentBounds.Width * scale) / 2.0;
                offsetY = (targetHeight - contentBounds.Height * scale) / 2.0;
            }

            // 4. 渲染
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // 背景
                dc.DrawRectangle(
                    new SolidColorBrush(backgroundColor),
                    null,
                    new Rect(0, 0, targetWidth, targetHeight));

                // 变换：平移到居中位置，然后缩放，然后平移到内容起点
                dc.PushTransform(new TranslateTransform(offsetX, offsetY));
                dc.PushTransform(new ScaleTransform(scale, scale));
                dc.PushTransform(new TranslateTransform(-contentBounds.X, -contentBounds.Y));

                // 渲染底层附件（非置顶）
                RenderAttachments(dc, page.Attachments.Where(a => !a.IsPinnedTop).OrderBy(a => a.ZIndex));

                // 渲染笔迹
                if (page.Strokes != null && page.Strokes.Count > 0)
                {
                    page.Strokes.Draw(dc);
                }

                // 渲染置顶附件
                RenderAttachments(dc, page.Attachments.Where(a => a.IsPinnedTop).OrderBy(a => a.ZIndex));

                // 还原变换
                dc.Pop();
                dc.Pop();
                dc.Pop();
            }

            // 5. 生成位图
            var rtb = new RenderTargetBitmap(targetWidth, targetHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        /// <summary>
        /// 渲染单页为 PNG 字节数组
        /// </summary>
        public byte[] RenderPageToPng(BoardPage page, ImageExportOptions options)
        {
            var bitmap = RenderPage(page, options);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }

        /// <summary>
        /// 渲染单页为 JPEG 字节数组
        /// </summary>
        public byte[] RenderPageToJpeg(BoardPage page, ImageExportOptions options)
        {
            var bitmap = RenderPage(page, options);
            var encoder = new JpegBitmapEncoder { QualityLevel = options.JpegQuality };
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }

        /// <summary>
        /// 计算页面内容边界（包括笔迹和附件）
        /// </summary>
        public Rect CalculateContentBounds(BoardPage page)
        {
            Rect bounds = Rect.Empty;

            // 笔迹边界
            if (page.Strokes != null && page.Strokes.Count > 0)
            {
                bounds = page.Strokes.GetBounds();
            }

            // 附件边界
            foreach (var att in page.Attachments)
            {
                var attRect = new Rect(att.X, att.Y, att.Width, att.Height);
                bounds = bounds.IsEmpty ? attRect : Rect.Union(bounds, attRect);
            }

            return bounds;
        }

        /// <summary>
        /// 预估导出图片文件大小（字节）
        /// </summary>
        public long EstimateFileSize(BoardPage page, ImageExportOptions options)
        {
            // 粗略估算：PNG 约为像素数 * 0.5，JPEG 约为像素数 * 0.15
            long pixels = (long)options.Width * options.Height;
            double factor = options.Format == ExportFormat.Png ? 0.5 : 0.15;

            // 内容复杂度加成
            int strokeCount = page.Strokes?.Count ?? 0;
            int attachmentCount = page.Attachments.Count;
            double complexityFactor = 1.0 + strokeCount * 0.001 + attachmentCount * 0.05;

            return (long)(pixels * factor * complexityFactor);
        }

        private BitmapSource CreateEmptyBitmap(int width, int height, Color backgroundColor)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(backgroundColor), null, new Rect(0, 0, width, height));
            }

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        private void RenderAttachments(DrawingContext dc, IEnumerable<BoardAttachment> attachments)
        {
            foreach (var att in attachments)
            {
                switch (att.Type)
                {
                    case BoardAttachmentType.Image:
                        RenderImageAttachment(dc, att);
                        break;
                    case BoardAttachmentType.Text:
                        RenderTextAttachment(dc, att);
                        break;
                    case BoardAttachmentType.Video:
                        RenderVideoPlaceholder(dc, att);
                        break;
                    case BoardAttachmentType.Link:
                        RenderLinkPlaceholder(dc, att);
                        break;
                }
            }
        }

        private void RenderImageAttachment(DrawingContext dc, BoardAttachment att)
        {
            if (att.Image != null)
            {
                dc.DrawImage(att.Image, new Rect(att.X, att.Y, att.Width, att.Height));
            }
            else if (!string.IsNullOrEmpty(att.FilePath) && File.Exists(att.FilePath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(att.FilePath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    dc.DrawImage(bitmap, new Rect(att.X, att.Y, att.Width, att.Height));
                }
                catch
                {
                    RenderPlaceholder(dc, att, "图片加载失败", Colors.Gray);
                }
            }
            else
            {
                RenderPlaceholder(dc, att, "图片", Colors.Gray);
            }
        }

        private void RenderTextAttachment(DrawingContext dc, BoardAttachment att)
        {
            // 背景
            var bgBrush = new SolidColorBrush(Color.FromArgb(200, 40, 40, 45));
            bgBrush.Freeze();
            dc.DrawRoundedRectangle(bgBrush, null, new Rect(att.X, att.Y, att.Width, att.Height), 8, 8);

            // 文本
            if (!string.IsNullOrEmpty(att.Text))
            {
                var ft = new FormattedText(
                    att.Text,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Microsoft YaHei"),
                    14,
                    Brushes.White,
                    96);
                ft.MaxTextWidth = att.Width - 16;
                ft.MaxTextHeight = att.Height - 16;
                dc.DrawText(ft, new Point(att.X + 8, att.Y + 8));
            }
        }

        private void RenderVideoPlaceholder(DrawingContext dc, BoardAttachment att)
        {
            RenderPlaceholder(dc, att, "视频: " + (att.DisplayName ?? "未知"), Color.FromRgb(60, 60, 80));
        }

        private void RenderLinkPlaceholder(DrawingContext dc, BoardAttachment att)
        {
            RenderPlaceholder(dc, att, att.Url ?? "链接", Color.FromRgb(40, 80, 100));
        }

        private void RenderPlaceholder(DrawingContext dc, BoardAttachment att, string text, Color bgColor)
        {
            var bgBrush = new SolidColorBrush(bgColor);
            bgBrush.Freeze();
            dc.DrawRoundedRectangle(bgBrush, null, new Rect(att.X, att.Y, att.Width, att.Height), 8, 8);

            var ft = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei"),
                12,
                Brushes.White,
                96);
            ft.MaxTextWidth = att.Width - 16;

            double textX = att.X + (att.Width - ft.Width) / 2;
            double textY = att.Y + (att.Height - ft.Height) / 2;
            dc.DrawText(ft, new Point(textX, textY));
        }
    }
}
