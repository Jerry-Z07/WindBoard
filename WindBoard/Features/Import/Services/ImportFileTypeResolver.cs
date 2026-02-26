using System;
using System.IO;

namespace WindBoard.Features.Import.Services
{
    /// <summary>
    /// 导入文件类型识别器：将文件名（扩展名）映射到导入队列分类与导入行为。
    /// </summary>
    internal enum ImportFileContentKind
    {
        Wbix,
        Wbi,
        Image,
        Video,
        Audio,
        Text,
        UrlShortcut,
        Other,
    }

    /// <summary>
    /// 统一的文件类型识别逻辑，避免导入 UI/服务层各自维护一份扩展名列表。
    /// </summary>
    internal static class ImportFileTypeResolver
    {
        internal static ImportFileContentKind Resolve(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return ImportFileContentKind.Other;
            }

            string ext = Path.GetExtension(fileName);
            if (string.IsNullOrWhiteSpace(ext))
            {
                return ImportFileContentKind.Other;
            }

            ext = ext.ToLowerInvariant();

            // 工作区文件：独立导入流程（互斥）。
            if (string.Equals(ext, ".wbix", StringComparison.Ordinal))
            {
                return ImportFileContentKind.Wbix;
            }

            if (string.Equals(ext, ".wbi", StringComparison.Ordinal))
            {
                return ImportFileContentKind.Wbi;
            }

            // 图片。
            if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff" or ".webp")
            {
                return ImportFileContentKind.Image;
            }

            // 视频。
            if (ext is ".mp4" or ".mov" or ".mkv" or ".wmv" or ".avi" or ".webm")
            {
                return ImportFileContentKind.Video;
            }

            // 音频。
            if (ext is ".mp3" or ".wav" or ".m4a" or ".aac" or ".flac" or ".ogg")
            {
                return ImportFileContentKind.Audio;
            }

            // 文本（含日志/markdown/json 等常见格式）。
            if (ext is ".txt" or ".md" or ".log" or ".json")
            {
                return ImportFileContentKind.Text;
            }

            // Windows Internet Shortcut（.url）：通常期望导入为链接，解析失败时再按文本兜底。
            if (string.Equals(ext, ".url", StringComparison.Ordinal))
            {
                return ImportFileContentKind.UrlShortcut;
            }

            return ImportFileContentKind.Other;
        }
    }
}

