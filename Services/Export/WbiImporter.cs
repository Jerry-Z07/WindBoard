using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Ink;
using System.Windows.Input;
using Newtonsoft.Json;
using WindBoard.Core.Ink;
using WindBoard.Models.Export;
using WindBoard.Models.InkV2;
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
        private static readonly Version MaxSupportedVersion = new Version(2, 0);

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
                result.ErrorMessage = LocalizationService.Instance.GetString("WbiImporter_FileNotFound");
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
                        result.ErrorMessage = LocalizationService.Instance.GetString("WbiImporter_InvalidMissingManifest");
                        return;
                    }

                    WbiManifest manifest;
                    using (var reader = new StreamReader(manifestEntry.Open()))
                    {
                        string json = reader.ReadToEnd();
                        manifest = JsonConvert.DeserializeObject<WbiManifest>(json)
                            ?? throw new InvalidDataException(LocalizationService.Instance.GetString("WbiImporter_ManifestParseFailed"));
                    }

                    // 版本检查
                    if (!IsVersionCompatible(manifest.MinCompatibleVersion))
                    {
                        string appName = AppDisplayNames.GetAppNameFromSettings();
                        result.ErrorMessage = LocalizationService.Instance.Format(
                            "WbiImporter_RequireNewerVersion_Format",
                            appName,
                            manifest.MinCompatibleVersion ?? string.Empty);
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
                            StatusMessage = LocalizationService.Instance.Format("WbiImporter_ImportingPage_Format", i + 1)
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
                    StatusMessage = LocalizationService.Instance.GetString("WbiImporter_Completed")
                });
            }
            catch (OperationCanceledException)
            {
                result.ErrorMessage = LocalizationService.Instance.GetString("WbiImporter_Canceled");
            }
            catch (Exception ex)
            {
                result.ErrorMessage = LocalizationService.Instance.Format("WbiImporter_Failed_Format", ex.Message);
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
                    ?? throw new InvalidDataException(
                        LocalizationService.Instance.Format("WbiImporter_PageDataParseFailed_Format", pageRef.Id));
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

            // 导入 v2 笔迹
            if (!string.IsNullOrEmpty(pageData.InkFile))
            {
                string inkPath = $"{PagesFolder}/{pageData.InkFile}";
                var inkEntry = archive.GetEntry(inkPath);
                if (inkEntry != null)
                {
                    using var reader = new StreamReader(inkEntry.Open());
                    string json = reader.ReadToEnd();
                    var inkData = JsonConvert.DeserializeObject<WbiInkV2DocumentData>(json);
                    if (inkData != null)
                    {
                        page.Ink = ImportInkV2(inkData);
                        page.InkSpatialIndex.Rebuild(page.Ink);
                        page.ContentVersion++;
                    }
                }
            }
            // 导入旧版 .isf（legacy）并迁移为 v2 ink
            else if (!string.IsNullOrEmpty(pageData.StrokesFile))
            {
                string isfPath = $"{PagesFolder}/{pageData.StrokesFile}";
                var isfEntry = archive.GetEntry(isfPath);
                if (isfEntry != null)
                {
                    // StrokeCollection 需要可定位的流，ZipArchiveEntry.Open() 返回的 DeflateStream 不可定位
                    // 因此需要先将数据读取到 MemoryStream 中
                    using var zipStream = isfEntry.Open();

                    int preallocateCapacity = 0;
                    long isfLength = isfEntry.Length;
                    if (isfLength > 0 && isfLength <= int.MaxValue)
                    {
                        preallocateCapacity = (int)isfLength;
                    }

                    using var memoryStream = preallocateCapacity > 0
                        ? new MemoryStream(preallocateCapacity)
                        : new MemoryStream();
                    zipStream.CopyTo(memoryStream);
                    memoryStream.Position = 0;

                    var strokes = new StrokeCollection(memoryStream);
                    page.Ink = ConvertLegacyIsfToInkDocument(strokes, page.Zoom);
                    page.InkSpatialIndex.Rebuild(page.Ink);
                    page.ContentVersion++;
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

        private static InkDocument ImportInkV2(WbiInkV2DocumentData data)
        {
            var document = new InkDocument();
            if (data.Strokes == null || data.Strokes.Count == 0) return document;

            document.Strokes.Capacity = Math.Max(document.Strokes.Capacity, data.Strokes.Count);

            for (int si = 0; si < data.Strokes.Count; si++)
            {
                WbiInkV2StrokeData strokeData = data.Strokes[si];
                InkTool tool = ImportTool(strokeData.Tool);
                var stroke = new InkStroke(strokeData.Id, tool);

                if (strokeData.Fragments != null)
                {
                    for (int fi = 0; fi < strokeData.Fragments.Count; fi++)
                    {
                        WbiInkV2FragmentData fragmentData = strokeData.Fragments[fi];
                        var fragment = new InkFragment(fragmentData.Id);

                        if (fragmentData.Points != null)
                        {
                            fragment.Points.Capacity = Math.Max(fragment.Points.Capacity, fragmentData.Points.Count);
                            for (int pi = 0; pi < fragmentData.Points.Count; pi++)
                            {
                                WbiInkV2PointData p = fragmentData.Points[pi];
                                float pressure = float.IsNaN(p.Pressure) || float.IsInfinity(p.Pressure) ? 0.5f : Math.Clamp(p.Pressure, 0.0f, 1.0f);
                                fragment.Points.Add(new InkPoint(p.XDip, p.YDip, pressure, p.TimestampTicks));
                            }
                        }

                        if (fragment.Points.Count >= 2)
                        {
                            stroke.Fragments.Add(fragment);
                        }
                    }
                }

                if (stroke.Fragments.Count > 0)
                {
                    document.Strokes.Add(stroke);
                }
            }

            return document;
        }

        private static InkTool ImportTool(WbiInkV2ToolData? tool)
        {
            if (tool == null) return InkTool.CreateDefault();

            double baseThickness = tool.BaseThicknessDip;
            if (baseThickness <= 0 || double.IsNaN(baseThickness) || double.IsInfinity(baseThickness))
            {
                baseThickness = 1.0;
            }

            InkThicknessSemantics semantics = Enum.IsDefined(typeof(InkThicknessSemantics), tool.ThicknessSemantics)
                ? (InkThicknessSemantics)tool.ThicknessSemantics
                : InkThicknessSemantics.ViewInvariant;

            InkBrushKind brushKind = Enum.IsDefined(typeof(InkBrushKind), tool.BrushKind)
                ? (InkBrushKind)tool.BrushKind
                : InkBrushKind.Pen;

            float pressureNominal = tool.PressureNominal;
            if (float.IsNaN(pressureNominal) || float.IsInfinity(pressureNominal) || pressureNominal <= 0.05f || pressureNominal > 1.0f)
            {
                pressureNominal = 1.0f;
            }

            return new InkTool(
                ColorArgb: tool.ColorArgb,
                BaseThicknessDip: baseThickness,
                ThicknessSemantics: semantics,
                BrushKind: brushKind,
                UsesPressure: tool.UsesPressure,
                PressureNominal: pressureNominal);
        }

        private static InkDocument ConvertLegacyIsfToInkDocument(StrokeCollection strokes, double currentZoom)
        {
            var document = new InkDocument();
            if (strokes == null || strokes.Count == 0) return document;

            double zoom = currentZoom;
            if (zoom <= 0 || double.IsNaN(zoom) || double.IsInfinity(zoom)) zoom = 1.0;

            document.Strokes.Capacity = Math.Max(document.Strokes.Capacity, strokes.Count);

            for (int si = 0; si < strokes.Count; si++)
            {
                Stroke s = strokes[si];

                if (s.StylusPoints == null || s.StylusPoints.Count < 2)
                {
                    continue;
                }

                InkThicknessSemantics semantics = InkThicknessSemantics.ViewInvariant;
                if (StrokeInkSemanticsMetadata.TryGetThicknessSemantics(s, out var storedSemantics))
                {
                    semantics = storedSemantics;
                }

                var da = s.DrawingAttributes;
                uint colorArgb = ((uint)da.Color.A << 24) | ((uint)da.Color.R << 16) | ((uint)da.Color.G << 8) | da.Color.B;

                double w = da.Width;
                double h = da.Height;
                if (w <= 0 || double.IsNaN(w) || double.IsInfinity(w)) w = 1.0;
                if (h <= 0 || double.IsNaN(h) || double.IsInfinity(h)) h = 1.0;

                double baseThickness = (w + h) * 0.5;
                if (semantics == InkThicknessSemantics.ViewInvariant)
                {
                    baseThickness = StrokeThicknessMetadata.GetOrCreateLogicalThicknessDip(s, zoom);
                }

                bool ignorePressure = da.IgnorePressure;
                bool anyPressureVariance = false;
                float prevPressure = 0.5f;

                var fragment = new InkFragment(
                    StrokeInkSemanticsMetadata.TryGetInkFragmentId(s, out Guid fragmentId) ? fragmentId : Guid.NewGuid());

                for (int pi = 0; pi < s.StylusPoints.Count; pi++)
                {
                    StylusPoint p = s.StylusPoints[pi];
                    float pressure = ignorePressure ? 0.5f : Math.Clamp(p.PressureFactor, 0.0f, 1.0f);
                    if (pi == 0)
                    {
                        prevPressure = pressure;
                    }
                    else if (Math.Abs(pressure - prevPressure) > 0.001f)
                    {
                        anyPressureVariance = true;
                    }

                    fragment.Points.Add(new InkPoint(p.X, p.Y, pressure, timestampTicks: 0));
                    prevPressure = pressure;
                }

                bool usesPressure = !ignorePressure && anyPressureVariance;

                InkBrushKind brushKind = da.IsHighlighter ? InkBrushKind.Highlighter : InkBrushKind.Pen;

                var tool = new InkTool(
                    ColorArgb: colorArgb,
                    BaseThicknessDip: baseThickness,
                    ThicknessSemantics: semantics,
                    BrushKind: brushKind,
                    UsesPressure: usesPressure,
                    PressureNominal: 1.0f);

                Guid strokeId = StrokeInkSemanticsMetadata.TryGetInkStrokeId(s, out Guid storedStrokeId)
                    ? storedStrokeId
                    : Guid.NewGuid();

                var stroke = new InkStroke(strokeId, tool);
                stroke.Fragments.Add(fragment);
                document.Strokes.Add(stroke);
            }

            return document;
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
                            missingResources.Add(LocalizationService.Instance.Format("WbiImporter_MissingResource_Image_Format", attData.FilePath));
                        }
                    }
                    break;

                case BoardAttachmentType.Video:
                    att.FilePath = attData.FilePath;
                    if (!string.IsNullOrEmpty(attData.FilePath) && !File.Exists(attData.FilePath))
                    {
                        missingResources.Add(LocalizationService.Instance.Format("WbiImporter_MissingResource_Video_Format", attData.FilePath));
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
