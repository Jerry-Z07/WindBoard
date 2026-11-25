using System;

namespace WindBoard.Features.Import.Models
{
    /// <summary>
    /// 导入流程的提交结果，只描述一个确定的导入动作（元素导入 / WBIX / WBI）。
    /// 上层据此分发，不依赖具体交互 UI 的实现细节。
    /// </summary>
    internal abstract record ImportDialogSubmission
    {
        internal sealed record Elements(ImportElementsRequest Request) : ImportDialogSubmission;

        internal sealed record Wbix(ImportWbixRequest Request) : ImportDialogSubmission;

        internal sealed record Wbi(ImportWbiRequest Request) : ImportDialogSubmission;
    }
}

