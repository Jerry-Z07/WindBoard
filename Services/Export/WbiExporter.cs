using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;
using WindBoard.Core.Ink.Adapters;
using WindBoard.Models.Export;
using WindBoard.Models.Ink;
using WindBoard.Models.Wbi;

namespace WindBoard.Services.Export
{
    /// <summary>
    /// WBI 格式导出器
    /// </summary>
    public sealed class WbiExporter
    {
        private const string ManifestFileName = "manifest.json";
        private const string PagesFolder = "pages";
        private const string AssetsFolder = "assets";

        /// <summary>
        /// 导出为 WBI 文件
        /// </summary>
        public async Task ExportAsync(
            IList<BoardPage> pages,
            string filePath,
            WbiExportOptions options,
            IProgress<ExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (pages == null || pages.Count == 0)
                throw new ArgumentException(LocalizationService.Instance.GetString("Export_NoPages"), nameof(pages));

            // 使用临时文件，成功后再移动到目标位置
            string tempPath = Path.GetTempFileName();
            // GetTempFileName 会创建空文件，但 ZipFile.Open(Create) 需要文件不存在
            File.Delete(tempPath);

            try
            {
                await Task.Run(() =>
                {
                    using var archive = ZipFile.Open(tempPath, ZipArchiveMode.Create);

                    var manifest = new WbiManifest
                    {
                        Version = "1.0",
                        MinCompatibleVersion = "1.0",
                        AppVersion = AppVersionInfo.Version,
                        CreatedAt = DateTime.UtcNow,
                        PageCount = pages.Count,
                        IncludeImageAssets = options.IncludeImageAssets
                    };

                    var assetFiles = new HashSet<string>();
                    bool usesInkModelPayload = false;

                    for (int i = 0; i < pages.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var page = pages[i];
                        string pageId = $"page_{(i + 1):D3}";

                        progress?.Report(new ExportProgress
                        {
                            CurrentPage = i + 1,
                            TotalPages = pages.Count,
                            StatusMessage = LocalizationService.Instance.Format("Export_ExportingPage_Format", i + 1)
                        });

                        // 添加页面引用到清单
                        manifest.Pages.Add(new WbiPageRef
                        {
                            Id = pageId,
                            Number = page.Number
                        });

                        string? strokesFileName = null;
                        string? inkFileName = null;
                        StrokeCollection? strokesToExport = null;

                        if (ShouldExportInkModel(page))
                        {
                            usesInkModelPayload = true;
                            inkFileName = $"{pageId}.ink.json";
                            ExportInkModel(archive, inkFileName, page.InkStrokes, options);
                        }
                        else
                        {
                            strokesToExport = GetInkStrokesForExport(page);
                            if (strokesToExport != null && strokesToExport.Count > 0)
                            {
                                strokesFileName = $"{pageId}.isf";
                            }
                        }

                        var pageData = ExportPageData(page, pageId, options, archive, assetFiles, strokesFileName, inkFileName);

                        // 保存页面 JSON
                        string pageJsonPath = $"{PagesFolder}/{pageId}.json";
                        var pageEntry = archive.CreateEntry(pageJsonPath, GetCompressionLevel(options.CompressionLevel));
                        using (var writer = new StreamWriter(pageEntry.Open()))
                        {
                            string json = JsonConvert.SerializeObject(pageData, Formatting.Indented);
                            writer.Write(json);
                        }

                        // 导出笔迹数据
                        if (strokesToExport != null && strokesToExport.Count > 0 && !string.IsNullOrEmpty(strokesFileName))
                        {
                            string isfPath = $"{PagesFolder}/{strokesFileName}";
                            var isfEntry = archive.CreateEntry(isfPath, GetCompressionLevel(options.CompressionLevel));
                            using (var stream = isfEntry.Open())
                            {
                                strokesToExport.Save(stream);
                            }
                        }
                    }

                    if (usesInkModelPayload)
                    {
                        manifest.Version = "1.1";
                        manifest.MinCompatibleVersion = "1.1";
                    }

                    // 保存清单
                    var manifestEntry = archive.CreateEntry(ManifestFileName, GetCompressionLevel(options.CompressionLevel));
                    using (var writer = new StreamWriter(manifestEntry.Open()))
                    {
                        string json = JsonConvert.SerializeObject(manifest, Formatting.Indented);
                        writer.Write(json);
                    }

                }, cancellationToken);

                // 移动到目标位置
                if (File.Exists(filePath))
                    File.Delete(filePath);
                File.Move(tempPath, filePath);

                progress?.Report(new ExportProgress
                {
                    CurrentPage = pages.Count,
                    TotalPages = pages.Count,
                    StatusMessage = LocalizationService.Instance.GetString("Export_Completed")
                });
            }
            finally
            {
                // 清理临时文件
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }

        /// <summary>
        /// 预估导出文件大小（字节）
        /// </summary>
        public long EstimateFileSize(IList<BoardPage> pages, WbiExportOptions options)
        {
            long totalSize = 0;

            foreach (var page in pages)
            {
                // 笔迹数据估算（每个点约 20 字节，压缩后约 5 字节）
                int totalPoints = page.Strokes?.Sum(s => s.StylusPoints.Count) ?? 0;
                if (totalPoints == 0)
                {
                    totalPoints = page.InkStrokes?.Sum(s => s.Points.Count) ?? 0;
                }
                totalSize += totalPoints * 5;

                // 附件估算
                foreach (var att in page.Attachments)
                {
                    if (options.IncludeImageAssets && att.Type == BoardAttachmentType.Image)
                    {
                        // 图片：根据尺寸估算（压缩后）
                        totalSize += EstimateImageSize(att, options);
                    }
                    else
                    {
                        // 仅元数据
                        totalSize += 200;
                    }
                }

                // 页面 JSON 元数据
                totalSize += 500;
            }

            // 清单文件
            totalSize += 500;

            // ZIP 开销
            totalSize = (long)(totalSize * 1.05);

            return totalSize;
        }

        private WbiPageData ExportPageData(
            BoardPage page,
            string pageId,
            WbiExportOptions options,
            ZipArchive archive,
            HashSet<string> assetFiles,
            string? strokesFileName,
            string? inkFileName)
        {
            var pageData = new WbiPageData
            {
                Number = page.Number,
                CanvasWidth = page.CanvasWidth,
                CanvasHeight = page.CanvasHeight,
                Zoom = page.Zoom,
                PanX = page.PanX,
                PanY = page.PanY
            };

            // 笔迹文件引用
            pageData.StrokesFile = strokesFileName;
            pageData.InkFile = inkFileName;

            // 导出附件
            foreach (var att in page.Attachments)
            {
                var attData = new WbiAttachmentData
                {
                    Id = att.Id,
                    Type = att.Type.ToString(),
                    X = att.X,
                    Y = att.Y,
                    Width = att.Width,
                    Height = att.Height,
                    ZIndex = att.ZIndex,
                    IsPinnedTop = att.IsPinnedTop
                };

                switch (att.Type)
                {
                    case BoardAttachmentType.Image:
                        if (options.IncludeImageAssets && !string.IsNullOrEmpty(att.FilePath) && File.Exists(att.FilePath))
                        {
                            // 嵌入图片文件
                            string assetFileName = $"{att.Id}{Path.GetExtension(att.FilePath)}";
                            if (!assetFiles.Contains(assetFileName))
                            {
                                assetFiles.Add(assetFileName);
                                string assetPath = $"{AssetsFolder}/{assetFileName}";

                                // 压缩图片并写入
                                var compressedData = CompressImage(att.FilePath, options);
                                var assetEntry = archive.CreateEntry(assetPath, CompressionLevel.NoCompression); // 图片已压缩
                                using (var stream = assetEntry.Open())
                                {
                                    stream.Write(compressedData, 0, compressedData.Length);
                                }
                            }
                            attData.AssetFile = assetFileName;
                        }
                        else
                        {
                            // 仅保存路径
                            attData.FilePath = att.FilePath;
                        }
                        break;

                    case BoardAttachmentType.Video:
                        // 视频仅保存路径
                        attData.FilePath = att.FilePath;
                        break;

                    case BoardAttachmentType.Text:
                        attData.Text = att.Text;
                        break;

                    case BoardAttachmentType.Link:
                        attData.Url = att.Url;
                        break;
                }

                pageData.Attachments.Add(attData);
            }

            return pageData;
        }

        private static StrokeCollection? GetInkStrokesForExport(BoardPage page)
        {
            if (page.Strokes != null && page.Strokes.Count > 0)
            {
                return page.Strokes;
            }

            if (page.InkStrokes != null && page.InkStrokes.Count > 0)
            {
                double zoom = page.Zoom <= 0 ? 1.0 : page.Zoom;
                return WpfStrokeAdapter.ToStrokeCollection(page.InkStrokes, zoom);
            }

            return null;
        }

        private static bool ShouldExportInkModel(BoardPage page)
        {
            if (page.InkStrokes == null || page.InkStrokes.Count == 0) return false;
            return page.Strokes == null || page.Strokes.Count == 0;
        }

        private void ExportInkModel(ZipArchive archive, string inkFileName, List<InkStrokeModel> strokes, WbiExportOptions options)
        {
            if (strokes == null || strokes.Count == 0) return;

            var payload = BuildInkPayload(strokes);
            string inkPath = $"{PagesFolder}/{inkFileName}";
            var inkEntry = archive.CreateEntry(inkPath, GetCompressionLevel(options.CompressionLevel));
            using (var writer = new StreamWriter(inkEntry.Open()))
            {
                writer.Write(JsonConvert.SerializeObject(payload, Formatting.None));
            }
        }

        private static WbiInkPayload BuildInkPayload(List<InkStrokeModel> strokes)
        {
            var payload = new WbiInkPayload
            {
                Version = "1.0"
            };

            for (int i = 0; i < strokes.Count; i++)
            {
                var s = strokes[i];
                if (s == null) continue;

                var style = s.Style;
                var stroke = new WbiInkStrokeData
                {
                    Id = s.Id,
                    ZoomAtCreation = s.ZoomAtCreation,
                    BrushKind = style.BrushKind,
                    ColorArgb = PackColor(style.Color),
                    LogicalThicknessDip = style.LogicalThicknessDip,
                    UsesPressure = style.UsesPressure
                };

                var pts = s.Points;
                for (int j = 0; j < pts.Count; j++)
                {
                    var p = pts[j];
                    stroke.Points.Add(new WbiInkPointData
                    {
                        X = p.X,
                        Y = p.Y,
                        Pressure = p.Pressure,
                        TimestampTicks = p.TimestampTicks
                    });
                }

                payload.Strokes.Add(stroke);
            }

            return payload;
        }

        private static uint PackColor(Color color)
        {
            return ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
        }

        private byte[] CompressImage(string filePath, WbiExportOptions options)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(filePath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;

                // 限制最大尺寸
                bitmap.DecodePixelWidth = options.MaxImageDimension;
                bitmap.EndInit();
                bitmap.Freeze();

                // 编码为 JPEG
                var encoder = new JpegBitmapEncoder { QualityLevel = options.ImageQuality };
                encoder.Frames.Add(BitmapFrame.Create(bitmap));

                using var ms = new MemoryStream();
                encoder.Save(ms);
                return ms.ToArray();
            }
            catch
            {
                // 失败时返回原始文件
                return File.ReadAllBytes(filePath);
            }
        }

        private long EstimateImageSize(BoardAttachment att, WbiExportOptions options)
        {
            // 估算压缩后大小
            double pixels = Math.Min(att.Width * att.Height, options.MaxImageDimension * options.MaxImageDimension);
            double bytesPerPixel = options.ImageQuality / 100.0 * 0.3; // JPEG 压缩估算
            return (long)(pixels * bytesPerPixel);
        }

        private CompressionLevel GetCompressionLevel(WbiCompressionLevel level)
        {
            return level switch
            {
                WbiCompressionLevel.None => CompressionLevel.NoCompression,
                WbiCompressionLevel.Fast => CompressionLevel.Fastest,
                WbiCompressionLevel.Standard => CompressionLevel.Optimal,
                WbiCompressionLevel.Maximum => CompressionLevel.SmallestSize,
                _ => CompressionLevel.Optimal
            };
        }

    }
}
