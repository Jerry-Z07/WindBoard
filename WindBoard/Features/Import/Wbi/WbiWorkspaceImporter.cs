using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WindBoard.Board;
using WindBoard.Board.Editing;
using WindBoard.Board.Elements;
using WindBoard.Features.Import.Services;
using WindBoard.Localization;
using WindBoard.Logging;
using Vortice.Mathematics;
using Windows.Storage;
using Windows.UI.Input.Inking;

namespace WindBoard.Features.Import.Wbi
{
    /// <summary>
    /// WBI 工作区导入结果。
    /// </summary>
    internal sealed class WbiWorkspaceImportResult
    {
        public bool Success { get; set; }

        public string? ErrorMessage { get; set; }

        public List<BoardPage> Pages { get; } = new();

        /// <summary>
        /// 缺失/无法读取的外部资源（例如：视频路径不存在、图片外链丢失等）。
        /// </summary>
        public List<string> MissingResources { get; } = new();

        public WbiManifest? Manifest { get; set; }
    }

    /// <summary>
    /// 旧格式 WBI（.wbi）工作区导入器：
    /// - 读取 Zip + JSON（snake_case）；
    /// - 从 ISF 解析笔迹并转换为新版本 Stroke；
    /// - 将附件映射为新版本元素卡片（图片/视频/文本/链接）。
    /// </summary>
    internal sealed class WbiWorkspaceImporter
    {
        private const string ManifestEntryName = "manifest.json";
        private const string PagesFolder = "pages";
        private const string AssetsFolder = "assets";

        private static readonly Version MaxSupportedVersion = new(1, 0);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };

        /// <summary>
        /// WBI 导入上下文：把 Zip/Manifest/取消令牌/缺失资源与临时解压目录收敛到一个对象中，
        /// 用于降低方法参数数量，并让导入链路的职责更清晰。
        /// </summary>
        private sealed class WbiImportContext
        {
            internal WbiImportContext(ZipArchive archive, WbiManifest manifest, List<string> missingResources, CancellationToken cancellationToken)
            {
                Archive = archive ?? throw new ArgumentNullException(nameof(archive));
                Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
                MissingResources = missingResources ?? throw new ArgumentNullException(nameof(missingResources));
                CancellationToken = cancellationToken;
            }

            internal ZipArchive Archive { get; }

            internal WbiManifest Manifest { get; }

            internal List<string> MissingResources { get; }

            internal CancellationToken CancellationToken { get; }

            private string? _extractFolder;

            internal string GetOrCreateExtractFolder()
            {
                if (!string.IsNullOrWhiteSpace(_extractFolder))
                {
                    return _extractFolder!;
                }

                string folder = Path.Combine(Path.GetTempPath(), "WindBoard_Import_WBI_" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(folder);
                _extractFolder = folder;
                return folder;
            }
        }

        public async Task<WbiWorkspaceImportResult> ImportAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var result = new WbiWorkspaceImportResult();

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                result.ErrorMessage = L10n.Get("Import_Wbix_ParseFailed_Message");
                return result;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

                ZipArchiveEntry? manifestEntry = archive.GetEntry(ManifestEntryName);
                if (manifestEntry is null)
                {
                    result.ErrorMessage = L10n.Get("Import_Wbix_ParseFailed_Message");
                    return result;
                }

                WbiManifest manifest;
                await using (Stream ms = manifestEntry.Open())
                {
                    manifest = await JsonSerializer.DeserializeAsync<WbiManifest>(ms, JsonOptions, cancellationToken)
                        ?? throw new InvalidDataException("WBI manifest.json 解析失败。");
                }

                if (!IsVersionCompatible(manifest.MinCompatibleVersion))
                {
                    result.ErrorMessage = L10n.Get("Import_Wbix_ParseFailed_Message");
                    return result;
                }

                result.Manifest = manifest;

                var context = new WbiImportContext(archive, manifest, result.MissingResources, cancellationToken);

                // 逐页导入：缺页允许跳过（与旧版行为一致），但解析失败会终止导入。
                for (int i = 0; i < manifest.Pages.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    WbiPageRef pageRef = manifest.Pages[i];
                    BoardPage? page = await ImportPageAsync(context, pageRef);
                    if (page is not null)
                    {
                        result.Pages.Add(page);
                    }
                }

                if (result.Pages.Count <= 0)
                {
                    result.ErrorMessage = L10n.Get("Import_Wbix_ParseFailed_Message");
                    return result;
                }

                result.Success = true;
            }
            catch (OperationCanceledException)
            {
                result.ErrorMessage = L10n.Get("Common_Cancel");
            }
            catch (Exception ex)
            {
                AppLog.Error("WBI", $"导入失败：'{filePath}'", ex);
                result.ErrorMessage = L10n.Get("Import_Wbix_ParseFailed_Message");
            }

            return result;
        }

        private static bool IsVersionCompatible(string? minVersion)
        {
            if (string.IsNullOrWhiteSpace(minVersion))
            {
                return true;
            }

            if (!Version.TryParse(minVersion, out Version? required))
            {
                return false;
            }

            return required <= MaxSupportedVersion;
        }

        private static async Task<BoardPage?> ImportPageAsync(WbiImportContext context, WbiPageRef pageRef)
        {
            CancellationToken cancellationToken = context.CancellationToken;
            cancellationToken.ThrowIfCancellationRequested();

            string pageId = (pageRef.Id ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(pageId))
            {
                AppLog.Warn("WBI", "页面引用缺少 id，已跳过。");
                return null;
            }

            string pageJsonPath = $"{PagesFolder}/{pageId}.json";
            ZipArchiveEntry? pageEntry = context.Archive.GetEntry(pageJsonPath);
            if (pageEntry is null)
            {
                AppLog.Warn("WBI", $"缺少页面文件：'{pageJsonPath}'");
                return null;
            }

            WbiPageData pageData;
            await using (Stream s = pageEntry.Open())
            {
                pageData = await JsonSerializer.DeserializeAsync<WbiPageData>(s, JsonOptions, cancellationToken)
                    ?? throw new InvalidDataException($"WBI 页面解析失败：'{pageJsonPath}'");
            }

            // 新版本页面只保留 strokes + elements，不承载 canvas/viewport 元数据；
            // 忽略 zoom/pan，避免因坐标系差异导致视图跳转异常。
            var page = new BoardPage();
            BoardSession session = page.Session;

            await ImportStrokesAsync(context, session, pageData);
            await ImportAttachmentsAsync(context, session, pageData.Attachments);

            return page;
        }

        private static async Task ImportStrokesAsync(WbiImportContext context, BoardSession session, WbiPageData pageData)
        {
            if (string.IsNullOrWhiteSpace(pageData.StrokesFile))
            {
                return;
            }

            string isfPath = $"{PagesFolder}/{pageData.StrokesFile}";
            ZipArchiveEntry? isfEntry = context.Archive.GetEntry(isfPath);
            if (isfEntry is null)
            {
                AppLog.Warn("WBI", $"缺少笔迹文件：'{isfPath}'");
                return;
            }

            IReadOnlyList<Stroke> strokes = await TryLoadIsfStrokesAsync(isfEntry, context.CancellationToken);
            for (int i = 0; i < strokes.Count; i++)
            {
                session.Document.Strokes.Add(strokes[i]);
            }
        }

        private static async Task ImportAttachmentsAsync(WbiImportContext context, BoardSession session, IReadOnlyList<WbiAttachmentData>? attachments)
        {
            if (attachments is not { Count: > 0 })
            {
                return;
            }

            var imported = new List<(BoardElement element, bool aboveInk, int zIndex, int order)>(attachments.Count);

            for (int i = 0; i < attachments.Count; i++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                WbiAttachmentData att = attachments[i];
                BoardElement? element = await TryCreateElementFromAttachmentAsync(context, att);
                if (element is null)
                {
                    continue;
                }

                // 位置/尺寸：WBI 记录的是世界坐标（DIP 近似），与新版本世界坐标直接对齐。
                element.PositionWorld = new Vector2(ToFiniteFloat(att.X, fallback: 0), ToFiniteFloat(att.Y, fallback: 0));
                element.SizeWorld = new Vector2(
                    Math.Max(1.0f, ToFiniteFloat(att.Width, fallback: 320)),
                    Math.Max(1.0f, ToFiniteFloat(att.Height, fallback: 180)));

                imported.Add((element, aboveInk: att.IsPinnedTop, zIndex: att.ZIndex, order: i));
            }

            // 按 ZIndex 排序（同值保持原始顺序），保证绘制与命中测试的层级稳定。
            foreach ((BoardElement element, bool aboveInk, _, _) in imported
                .OrderBy(x => x.zIndex)
                .ThenBy(x => x.order))
            {
                if (aboveInk)
                {
                    session.Document.ElementsAboveInk.Add(element);
                }
                else
                {
                    session.Document.ElementsBelowInk.Add(element);
                }
            }
        }

        private static async Task<BoardElement?> TryCreateElementFromAttachmentAsync(WbiImportContext context, WbiAttachmentData att)
        {
            string type = (att.Type ?? string.Empty).Trim();
            if (type.Length == 0)
            {
                return null;
            }

            BoardElement? element = null;

            // 旧格式仅有 Image/Video/Text/Link，但这里做防御性兜底。
            switch (type.ToUpperInvariant())
            {
                case "IMAGE":
                    element = await CreateImageElementAsync(context, att);
                    break;

                case "VIDEO":
                    element = CreateVideoElement(att, context.MissingResources);
                    break;

                case "TEXT":
                    element = new BoardTextElement { Text = att.Text ?? string.Empty };
                    break;

                case "LINK":
                    element = new BoardLinkElement { Url = att.Url ?? string.Empty };
                    break;

                default:
                    if (!string.IsNullOrWhiteSpace(att.FilePath))
                    {
                        element = new BoardFileElement
                        {
                            SourcePath = att.FilePath!,
                            DisplayName = Path.GetFileName(att.FilePath),
                        };
                    }
                    else
                    {
                        AppLog.Warn("WBI", $"未知附件类型：type='{att.Type}'");
                    }
                    break;
            }

            return element;
        }

        private static BoardElement CreateVideoElement(WbiAttachmentData att, List<string> missingResources)
        {
            string path = att.FilePath ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(path) && !File.Exists(path))
            {
                missingResources.Add($"视频文件不存在：{path}");
            }

            return new BoardMediaElement
            {
                Kind = BoardMediaKind.Video,
                SourcePath = path,
                DisplayName = string.IsNullOrWhiteSpace(path) ? "视频" : Path.GetFileName(path),
            };
        }

        private static async Task<BoardElement> CreateImageElementAsync(WbiImportContext context, WbiAttachmentData att)
        {
            WbiManifest manifest = context.Manifest;
            CancellationToken cancellationToken = context.CancellationToken;

            string? sourcePath = null;
            string displayName = "图片";

            if (manifest.IncludeImageAssets && !string.IsNullOrWhiteSpace(att.AssetFile))
            {
                string assetName = Path.GetFileName(att.AssetFile);
                string assetEntryPath = $"{AssetsFolder}/{assetName}";
                ZipArchiveEntry? assetEntry = context.Archive.GetEntry(assetEntryPath);
                if (assetEntry is not null)
                {
                    string extractFolder = context.GetOrCreateExtractFolder();
                    string extractPath = Path.Combine(extractFolder, assetName);

                    try
                    {
                        await using Stream src = assetEntry.Open();
                        await using var dst = new FileStream(extractPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                        await src.CopyToAsync(dst, cancellationToken);
                        sourcePath = extractPath;
                        displayName = assetName;
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn("WBI", $"提取图片资源失败：'{assetEntryPath}'", ex);
                    }
                }
                else
                {
                    context.MissingResources.Add($"缺少内嵌图片资源：{assetEntryPath}");
                }
            }
            else if (!string.IsNullOrWhiteSpace(att.FilePath))
            {
                sourcePath = att.FilePath;
                displayName = Path.GetFileName(att.FilePath);

                if (!File.Exists(att.FilePath))
                {
                    context.MissingResources.Add($"图片文件不存在：{att.FilePath}");
                }
            }

            var element = new BoardMediaElement
            {
                Kind = BoardMediaKind.Image,
                SourcePath = sourcePath ?? string.Empty,
                DisplayName = displayName,
            };

            // 图片像素属于“可选增强”：解码失败不应阻断导入。
            if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath))
            {
                try
                {
                    StorageFile file = await StorageFile.GetFileFromPathAsync(sourcePath);
                    (byte[] pixels, int w, int h)? decoded = await ImageImportDecoder.TryDecodeToBgra8PremulAsync(file, maxPixelEdge: 2048);
                    element.PixelWidth = decoded?.w ?? 0;
                    element.PixelHeight = decoded?.h ?? 0;
                    element.Bgra8PremulPixels = decoded?.pixels;
                }
                catch (Exception ex)
                {
                    AppLog.Warn("WBI", $"图片解码失败：'{sourcePath}'", ex);
                }
            }

            return element;
        }

        private static async Task<IReadOnlyList<Stroke>> TryLoadIsfStrokesAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                using Stream stream = entry.Open();
                using var input = stream.AsInputStream();

                var container = new InkStrokeContainer();
                await container.LoadAsync(input);

                IReadOnlyList<InkStroke> inkStrokes = container.GetStrokes();
                if (inkStrokes.Count == 0)
                {
                    return Array.Empty<Stroke>();
                }

                var strokes = new List<Stroke>(inkStrokes.Count);
                for (int i = 0; i < inkStrokes.Count; i++)
                {
                    Stroke stroke = ConvertInkStroke(inkStrokes[i]);
                    strokes.Add(stroke);
                }

                return strokes;
            }
            catch (Exception ex)
            {
                AppLog.Warn("WBI", $"ISF 解析失败：entry='{entry.FullName}'", ex);
                return Array.Empty<Stroke>();
            }
        }

        private static Stroke ConvertInkStroke(InkStroke inkStroke)
        {
            InkDrawingAttributes attr = inkStroke.DrawingAttributes;
            Windows.UI.Color c = attr.Color;

            float r = c.R / 255.0f;
            float g = c.G / 255.0f;
            float b = c.B / 255.0f;
            float a = c.A / 255.0f;

            double size = (attr.Size.Width + attr.Size.Height) / 2.0;
            float baseSize = (float)Math.Max(0.25, size);
            bool enablePressure = !attr.IgnorePressure;

            var stroke = new Stroke
            {
                Color = new Color4(r, g, b, a),
                BaseSize = baseSize,
                EnablePressure = enablePressure,
            };

            IReadOnlyList<InkPoint> points = inkStroke.GetInkPoints();
            for (int i = 0; i < points.Count; i++)
            {
                InkPoint p = points[i];
                stroke.Points.Add(new StrokePoint(ToVector2(p.Position), p.Pressure));
            }

            stroke.RecalculateBoundsFromPoints();
            return stroke;
        }

        private static Vector2 ToVector2(Windows.Foundation.Point p)
        {
            return new Vector2((float)p.X, (float)p.Y);
        }

        private static float ToFiniteFloat(double value, float fallback)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return fallback;
            }

            // double -> float 可能溢出，溢出时返回兜底值避免生成 NaN/Infinity。
            if (value > float.MaxValue)
            {
                return fallback;
            }

            if (value < float.MinValue)
            {
                return fallback;
            }

            return (float)value;
        }
    }
}
