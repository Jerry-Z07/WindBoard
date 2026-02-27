using System;
using System.Collections.Generic;
using System.Numerics;
using WindBoard.Board.Elements;

namespace WindBoard.Board.Persistence
{
    /// <summary>
    /// 工作区快照（用于多页面导入/导出）。
    /// 
    /// 说明：
    /// - 快照是“数据态”，不包含运行态对象（渲染器、输入控制器等）
    /// - 当前覆盖：笔迹 + 页面元素；并预留视口元数据（仅记录，可用于导出等）
    /// </summary>
    internal sealed record BoardWorkspaceSnapshot(
        IReadOnlyList<BoardPageSnapshot> Pages,
        int CurrentIndex,
        Vector2? ViewportCameraWorld = null,
        float? ViewportZoom = null,
        Vector2? ViewportSizeDip = null);

    /// <summary>
    /// 页面快照。
    /// </summary>
    /// <param name="Id">
    /// 页面 ID（与 <see cref="Editing.BoardPage.Id"/> 对齐）。
    /// 该字段用于后续导入/导出时维持页面身份稳定（例如资源引用、跨集合定位等）。
    /// </param>
    /// <param name="Strokes">笔迹数据。</param>
    /// <param name="ElementsBelowInk">元素（笔迹下方层）。顺序即绘制/命中测试顺序。</param>
    /// <param name="ElementsAboveInk">元素（笔迹上方层）。顺序即绘制/命中测试顺序。</param>
    internal sealed record BoardPageSnapshot(
        Guid Id,
        IReadOnlyList<StrokeSnapshot> Strokes,
        IReadOnlyList<BoardElementSnapshot>? ElementsBelowInk = null,
        IReadOnlyList<BoardElementSnapshot>? ElementsAboveInk = null);

    internal sealed record StrokeSnapshot(
        IReadOnlyList<StrokePointSnapshot> Points,
        Vector4 ColorRgba,
        float BaseSize,
        bool EnablePressure);

    internal readonly record struct StrokePointSnapshot(Vector2 Position, float Pressure);

    /// <summary>
    /// 页面元素快照（非笔迹对象）。
    /// </summary>
    /// <remarks>
    /// 说明：
    /// - 目前覆盖文本/链接/媒体/文件四类，满足 WBI 与 WBIX 的互通需求；
    /// - 更复杂的元素（便签/图形等）可在后续扩展。
    /// </remarks>
    internal abstract record BoardElementSnapshot(
        Guid Id,
        Vector2 PositionWorld,
        Vector2 SizeWorld,
        int Order);

    internal sealed record BoardTextElementSnapshot(
        Guid Id,
        Vector2 PositionWorld,
        Vector2 SizeWorld,
        int Order,
        string Text)
        : BoardElementSnapshot(Id, PositionWorld, SizeWorld, Order);

    internal sealed record BoardLinkElementSnapshot(
        Guid Id,
        Vector2 PositionWorld,
        Vector2 SizeWorld,
        int Order,
        string Url,
        string? Title)
        : BoardElementSnapshot(Id, PositionWorld, SizeWorld, Order);

    internal sealed record BoardMediaElementSnapshot(
        Guid Id,
        Vector2 PositionWorld,
        Vector2 SizeWorld,
        int Order,
        BoardMediaKind Kind,
        string SourcePath,
        string DisplayName)
        : BoardElementSnapshot(Id, PositionWorld, SizeWorld, Order);

    internal sealed record BoardFileElementSnapshot(
        Guid Id,
        Vector2 PositionWorld,
        Vector2 SizeWorld,
        int Order,
        string SourcePath,
        string DisplayName)
        : BoardElementSnapshot(Id, PositionWorld, SizeWorld, Order);
}
