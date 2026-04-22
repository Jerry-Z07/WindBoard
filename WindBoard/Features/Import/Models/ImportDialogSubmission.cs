using System;

namespace WindBoard.Features.Import.Models
{
    /// <summary>
    /// 导入流程的提交结果：
    /// 只返回一个确定的导入动作（元素导入 / WBIX / WBI），
    /// 避免上层依赖具体的交互 UI 实现细节。
    /// </summary>
    internal abstract record ImportDialogSubmission
    {
        internal sealed record Elements(ImportElementsRequest Request) : ImportDialogSubmission;

        internal sealed record Wbix(ImportWbixRequest Request) : ImportDialogSubmission;

        internal sealed record Wbi(ImportWbiRequest Request) : ImportDialogSubmission;
    }
}

