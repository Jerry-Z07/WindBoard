using System;
using System.Collections.Generic;
using System.Text.Json;
using WindBoard.Board.Persistence;

namespace WindBoard.Board.Persistence.Wbix
{
    /// <summary>
    /// 页面数据文件（pages/page-XXX.json）：v1/v2 的 strokes 为扁平形态，v3 起带 Kind 标识（InkItemSnapshot），读取兼容旧形态。
    /// elements 承载文本/链接/媒体/文件等页面元素，并保留扩展位。
    /// </summary>
    internal sealed record WbixPagePayload(
        Guid Id,
        IReadOnlyList<InkItemSnapshot> Strokes,
        IReadOnlyList<WbixPageElement>? Elements);

    /// <summary>
    /// 页面元素（预留）。
    /// 
    /// Data 使用 JsonElement 以便未来以“半结构化”方式落盘，不提前锁死 schema。
    /// </summary>
    internal sealed record WbixPageElement(string Type, JsonElement Data);
}
