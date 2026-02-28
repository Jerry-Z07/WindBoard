using System;

namespace WindBoard.Features.Import.Models
{
    /// <summary>
    /// <see cref="UI.ImportDialog"/> 的提交结果：
    /// 对话框关闭时只返回一个确定的导入动作（元素导入 / WBIX / WBI），
    /// 避免上层需要理解对话框内部的队列与状态机细节。
    /// </summary>
    internal abstract record ImportDialogSubmission
    {
        internal sealed record Elements(ImportElementsRequest Request) : ImportDialogSubmission;

        internal sealed record Wbix(ImportWbixRequest Request) : ImportDialogSubmission;

        internal sealed record Wbi(ImportWbiRequest Request) : ImportDialogSubmission;
    }
}

