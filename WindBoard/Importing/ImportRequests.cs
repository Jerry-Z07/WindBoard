using System.Collections.Generic;
using Windows.Storage;

namespace WindBoard.Importing
{
    /// <summary>
    /// 统一导入对话框（ImportDialog）提交的“元素导入”请求。
    /// </summary>
    internal sealed record ImportElementsRequest(
        IReadOnlyList<StorageFile> ImageFiles,
        IReadOnlyList<StorageFile> MediaFiles,
        IReadOnlyList<StorageFile> TextFiles,
        string? TextContent,
        string? LinkLines);

    /// <summary>
    /// WBIX 导入模式。
    /// </summary>
    internal enum ImportWbixMode
    {
        ReplaceCurrentPage,
        AppendAfterLastPage,
    }

    /// <summary>
    /// 统一导入对话框（ImportDialog）提交的“WBIX 导入”请求。
    /// </summary>
    internal sealed record ImportWbixRequest(
        StorageFile File,
        ImportWbixMode Mode);
}

