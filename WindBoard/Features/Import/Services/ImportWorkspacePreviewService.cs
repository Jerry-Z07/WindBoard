using System;
using System.IO;
using System.Threading.Tasks;
using WindBoard.Board.Persistence.Wbix;
using WindBoard.Features.Import.Wbi;
using Windows.Storage;

namespace WindBoard.Features.Import.Services
{
    /// <summary>
    /// 工作区预览类型。
    /// </summary>
    internal enum ImportWorkspacePreviewKind
    {
        Wbix,
        Wbi,
    }

    /// <summary>
    /// 工作区文件（WBIX/WBI）预览信息（用于导入对话框展示）。
    /// </summary>
    internal sealed record ImportWorkspacePreview(
        ImportWorkspacePreviewKind Kind,
        int PageCount,
        string Version,
        DateTime CreatedAt,
        byte[]? CoverPngBytes);

    /// <summary>
    /// 工作区预览读取服务：
    /// - 统一从 WBIX/WBI 读取 manifest 与可选封面；
    /// - 将不同格式的字段归一化为 UI 可直接展示的结构。
    /// </summary>
    internal static class ImportWorkspacePreviewService
    {
        public static async Task<ImportWorkspacePreview?> TryLoadAsync(StorageFile file)
        {
            if (file is null || string.IsNullOrWhiteSpace(file.Path))
            {
                return null;
            }

            string ext = Path.GetExtension(file.Name);
            ImportWorkspacePreview? result = null;

            if (string.Equals(ext, ".wbix", StringComparison.OrdinalIgnoreCase))
            {
                WbixPreviewReader.WbixPreview? preview = await WbixPreviewReader.TryReadAsync(file.Path);
                if (preview is not null)
                {
                    int pageCount = preview.Manifest.Pages?.Count ?? 0;
                    string version = preview.Manifest.Version.ToString();
                    DateTime created = preview.Manifest.CreatedUtc.UtcDateTime;

                    result = new ImportWorkspacePreview(
                        Kind: ImportWorkspacePreviewKind.Wbix,
                        PageCount: pageCount,
                        Version: version,
                        CreatedAt: created,
                        CoverPngBytes: preview.CoverPngBytes);
                }
            }
            else if (string.Equals(ext, ".wbi", StringComparison.OrdinalIgnoreCase))
            {
                WbiPreviewReader.WbiPreview? preview = await WbiPreviewReader.TryReadAsync(file.Path);
                if (preview is not null)
                {
                    int pageCount = preview.Manifest.Pages?.Count ?? preview.Manifest.PageCount;
                    string version = preview.Manifest.Version ?? "1.0";
                    DateTime created = preview.Manifest.CreatedAt.Kind == DateTimeKind.Unspecified
                        ? DateTime.SpecifyKind(preview.Manifest.CreatedAt, DateTimeKind.Utc)
                        : preview.Manifest.CreatedAt;

                    result = new ImportWorkspacePreview(
                        Kind: ImportWorkspacePreviewKind.Wbi,
                        PageCount: pageCount,
                        Version: version,
                        CreatedAt: created,
                        CoverPngBytes: null);
                }
            }

            return result;
        }
    }
}
