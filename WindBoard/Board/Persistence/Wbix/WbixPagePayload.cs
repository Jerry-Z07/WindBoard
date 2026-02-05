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
    /// - v1/v2 仅包含 strokes；
    /// - elements 预留用于未来承载“图片/视频/便签/图形”等页面元素。
    /// </summary>
    internal sealed record WbixPagePayload(
        Guid Id,
        IReadOnlyList<StrokeSnapshot> Strokes,
        IReadOnlyList<WbixPageElement> Elements);

    /// <summary>
    /// 页面元素（预留）。
    /// 
    /// Data 使用 JsonElement 以便未来以“半结构化”方式落盘，不提前锁死 schema。
    /// </summary>
    internal sealed record WbixPageElement(string Type, JsonElement Data);
}
