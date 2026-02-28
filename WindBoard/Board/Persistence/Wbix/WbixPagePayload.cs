using System;
using System.Collections.Generic;
using System.Text.Json;
using WindBoard.Board.Persistence;

namespace WindBoard.Board.Persistence.Wbix
{
    /// <summary>
    /// 页面数据文件（pages/page-XXX.json）。
    /// 
    /// 说明：
    /// - v1/v2 都包含 strokes（笔迹）；
    /// - elements 用于承载“文本/链接/媒体/文件”等页面元素，并保留未来扩展位。
    /// </summary>
    internal sealed record WbixPagePayload(
        Guid Id,
        IReadOnlyList<StrokeSnapshot> Strokes,
        IReadOnlyList<WbixPageElement>? Elements);

    /// <summary>
    /// 页面元素（预留）。
    /// 
    /// Data 使用 JsonElement 以便未来以“半结构化”方式落盘，不提前锁死 schema。
    /// </summary>
    internal sealed record WbixPageElement(string Type, JsonElement Data);
}
