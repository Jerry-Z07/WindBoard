using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Ink;
using Newtonsoft.Json;
using WindBoard.Models.Export;
using WindBoard.Models.Wbi;

namespace WindBoard.Services.Export
{
    /// <summary>
    /// WBI 导入结果
    /// </summary>
    public sealed class WbiImportResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public List<BoardPage> Pages { get; set; } = new();
        public WbiManifest? Manifest { get; set; }

        /// <summary>
        /// 未找到的资源文件列表（视频/链接等外部资源）
        /// </summary>
        public List<string> MissingResources { get; set; } = new();
    }

    /// <summary>
    /// WBI 格式导入器
    /// </summary>
    public sealed class WbiImporter
    {
        private const string ManifestFileName = "manifest.json";
        private const string PagesFolder = "pages";
        private const string AssetsFolder = "assets";

        /// <summary>当前支持的最高版本</summary>
        private static readonly Version MaxSupportedVersion = new Version(1, 0);

        /// <summary>
        /// 从 WBI 文件导入
        /// </summary>
        public async Task<WbiImportResult> ImportAsync(
            string filePath,
            string? assetExtractFolder = null,
            IProgress<ExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new WbiImportResult();

            if (!File.Exists(filePath))
            {
                result.ErrorMessage = "文件不存在";
                return result;
            }

            try
            {
                await Task.Run(() =>
                {
                    using var archive = ZipFile.OpenRead(filePath);

                    // 1. 读取并验证清单
                    var manifestEntry = archive.GetEntry(ManifestFileName);
                    if (manifestEntry == null)
                    {
                        result.ErrorMessage = "无效的 WBI 文件：缺少清单";
                        return;
                    }

                    WbiManifest manifest;
                    using (var reader = new StreamReader(manifestEntry.Open()))
                    {
                        string json = reader.ReadToEnd();
                        manifest = JsonConvert.DeserializeObject<WbiManifest>(json)
                            ?? throw new InvalidDataException("无法解析清单文件");
                    }

                    // 版本检查
                    if (!IsVersionCompatible(manifest.MinCompatibleVersion))
                    {
                        result.ErrorMessage = $"此文件需要更新版本的 WindBoard 才能打开（最低版本: {manifest.MinCompatibleVersion}）";
                        return;
                    }

                    result.Manifest = manifest;

                    // 2. 准备资源提取目录
                    string extractFolder = assetExtractFolder
                        ?? Path.Combine(Path.GetTempPath(), "WindBoard_Import_" + Guid.NewGuid().ToString("N")[..8]);

                    if (!Directory.Exists(extractFolder))
                        Directory.CreateDirectory(extractFolder);

                    // 3. 逐页导入
                    for (int i = 0; i < manifest.Pages.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var pageRef = manifest.Pages[i];
                        progress?.Report(new ExportProgress
                        {
                            CurrentPage = i + 1,
                            TotalPages = manifest.PageCount,
                            StatusMessage = $"正在导入第 {i + 1} 页..."
                        });

                        var page = ImportPage(archive, pageRef, extractFolder, manifest.IncludeImageAssets, result.MissingResources);
                        if (page != null)
                        {
                            result.Pages.Add(page);
                        }
                    }

                    result.Success = true;

                }, cancellationToken);

                progress?.Report(new ExportProgress
                {
                    CurrentPage = result.Pages.Count,
                    TotalPages = result.Pages.Count,
                    StatusMessage = "导入完成"
                });
            }
            catch (OperationCanceledException)
            {
                result.ErrorMessage = "导入已取消";
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"导入失败: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// 检查 WBI 文件信息（不完整导入）
        /// </summary>
        public WbiManifest? GetManifest(string filePath)
        {
            try
            {
                using var archive = ZipFile.OpenRead(filePath);
                var manifestEntry = archive.GetEntry(ManifestFileName);
                if (manifestEntry == null) return null;

                using var reader = new StreamReader(manifestEntry.Open());
                string json = reader.ReadToEnd();
                return JsonConvert.DeserializeObject<WbiManifest>(json);
            }
            catch
            {
                return null;
            }
        }

        private bool IsVersionCompatible(string? minVersion)
        {
            if (string.IsNullOrEmpty(minVersion)) return true;

            try
            {
                var required = new Version(minVersion);
                return required <= MaxSupportedVersion;
            }
            catch
            {
                return false;
            }
        }

        private BoardPage? ImportPage(
            ZipArchive archive,
            WbiPageRef pageRef,
            string extractFolder,
            bool hasImageAssets,
            List<string> missingResources)
        {
            // 读取页面 JSON
            string pageJsonPath = $"{PagesFolder}/{pageRef.Id}.json";
            var pageEntry = archive.GetEntry(pageJsonPath);
            if (pageEntry == null) return null;

            WbiPageData pageData;
            using (var reader = new StreamReader(pageEntry.Open()))
            {
                string json = reader.ReadToEnd();
                pageData = JsonConvert.DeserializeObject<WbiPageData>(json)
                    ?? throw new InvalidDataException($"无法解析页面数据: {pageRef.Id}");
            }

            // 创建 BoardPage
            var page = new BoardPage
            {
                Number = pageData.Number,
                CanvasWidth = pageData.CanvasWidth,
                CanvasHeight = pageData.CanvasHeight,
                Zoom = pageData.Zoom,
                PanX = pageData.PanX,
                PanY = pageData.PanY
            };

            // 导入笔迹
            if (!string.IsNullOrEmpty(pageData.StrokesFile))
            {
                string isfPath = $"{PagesFolder}/{pageData.StrokesFile}";
                var isfEntry = archive.GetEntry(isfPath);
                if (isfEntry != null)
                {
                    // StrokeCollection 需要可定位的流，ZipArchiveEntry.Open() 返回的 DeflateStream 不可定位
                    // 因此需要先将数据读取到 MemoryStream 中
                    using var zipStream = isfEntry.Open();
                    using var memoryStream = new MemoryStream();
                    zipStream.CopyTo(memoryStream);
                    memoryStream.Position = 0;
                    page.Strokes = new StrokeCollection(memoryStream);
                }
            }

            // 导入附件
            foreach (var attData in pageData.Attachments)
            {
                var att = ImportAttachment(archive, attData, extractFolder, hasImageAssets, missingResources);
                if (att != null)
                {
                    page.Attachments.Add(att);
                }
            }

            return page;
        }

        private BoardAttachment? ImportAttachment(
            ZipArchive archive,
            WbiAttachmentData attData,
            string extractFolder,
            bool hasImageAssets,
            List<string> missingResources)
        {
            if (!Enum.TryParse<BoardAttachmentType>(attData.Type, out var attType))
                return null;

            var att = new BoardAttachment
            {
                Id = attData.Id,
                Type = attType,
                X = attData.X,
                Y = attData.Y,
                Width = attData.Width,
                Height = attData.Height,
                ZIndex = attData.ZIndex,
                IsPinnedTop = attData.IsPinnedTop
            };

            switch (attType)
            {
                case BoardAttachmentType.Image:
                    if (hasImageAssets && !string.IsNullOrEmpty(attData.AssetFile))
                    {
                        // 从资源中提取图片
                        string assetPath = $"{AssetsFolder}/{attData.AssetFile}";
                        var assetEntry = archive.GetEntry(assetPath);
                        if (assetEntry != null)
                        {
                            string extractPath = Path.Combine(extractFolder, attData.AssetFile);
                            assetEntry.ExtractToFile(extractPath, overwrite: true);
                            att.FilePath = extractPath;
                        }
                    }
                    else if (!string.IsNullOrEmpty(attData.FilePath))
                    {
                        att.FilePath = attData.FilePath;
                        if (!File.Exists(attData.FilePath))
                        {
                            missingResources.Add($"图片: {attData.FilePath}");
                        }
                    }
                    break;

                case BoardAttachmentType.Video:
                    att.FilePath = attData.FilePath;
                    if (!string.IsNullOrEmpty(attData.FilePath) && !File.Exists(attData.FilePath))
                    {
                        missingResources.Add($"视频: {attData.FilePath}");
                    }
                    break;

                case BoardAttachmentType.Text:
                    att.Text = attData.Text;
                    break;

                case BoardAttachmentType.Link:
                    att.Url = attData.Url;
                    break;
            }

            return att;
        }
    }
}
