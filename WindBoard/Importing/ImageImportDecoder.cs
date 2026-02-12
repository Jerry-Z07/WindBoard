using System;
using System.Threading.Tasks;
using WindBoard.Logging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace WindBoard.Importing
{
    /// <summary>
    /// 图片导入解码器：将任意图片解码为 BGRA8 + Premultiplied 像素缓冲，便于渲染/展示层直接使用。
    /// </summary>
    internal static class ImageImportDecoder
    {
        /// <summary>
        /// 将图片解码为 BGRA8（预乘 Alpha）像素。
        /// </summary>
        /// <param name="file">图片文件。</param>
        /// <param name="maxPixelEdge">最大边长限制（超过则按比例缩放）。</param>
        /// <returns>成功返回像素与尺寸；失败返回 null。</returns>
        public static async Task<(byte[] pixels, int w, int h)?> TryDecodeToBgra8PremulAsync(StorageFile file, int maxPixelEdge)
        {
            if (file is null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            if (maxPixelEdge <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPixelEdge));
            }

            try
            {
                using IRandomAccessStream stream = await file.OpenReadAsync();
                BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);

                uint w = decoder.PixelWidth;
                uint h = decoder.PixelHeight;
                uint maxEdge = Math.Max(w, h);

                double scale = 1.0;
                if (maxEdge > (uint)maxPixelEdge)
                {
                    scale = (double)maxPixelEdge / maxEdge;
                }

                uint sw = (uint)Math.Max(1.0, Math.Round(w * scale));
                uint sh = (uint)Math.Max(1.0, Math.Round(h * scale));

                var transform = new BitmapTransform
                {
                    ScaledWidth = sw,
                    ScaledHeight = sh,
                    InterpolationMode = BitmapInterpolationMode.Fant,
                };

                PixelDataProvider provider = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    transform,
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.DoNotColorManage);

                byte[] pixels = provider.DetachPixelData();
                return (pixels, (int)sw, (int)sh);
            }
            catch (Exception ex)
            {
                AppLog.Error("Import", $"图片解码失败：'{file.Path}'", ex);
                return null;
            }
        }
    }
}
